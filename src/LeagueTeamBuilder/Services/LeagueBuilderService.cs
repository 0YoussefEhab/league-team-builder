using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Step 4c: glue between the builder page (Views/League/Builder.cshtml + wwwroot/js/league-builder.js) and
    // TeamBalancer. It loads the player pool and the mode settings, and runs one request of the JSON endpoint
    // (spec R1: the server generates and checks, the browser only renders). A request here never changes data;
    // saving a match is LeagueMatchService (Step 4d).
    public class LeagueBuilderService
    {
        public const int MaxPlayersPerRequest = 100;
        public const int MaxTeams = 40;
        public const int MaxIgnoredPairs = 200;
        public const int MaxTeamNameLength = 40;

        // Display label per LeagueLane index.
        public static readonly string[] LaneLabels = { "Top", "Jungle", "Mid", "ADC", "Support" };
        private static readonly string[] Roman = { "", "I", "II", "III", "IV" };

        private readonly ApplicationDbContext _db;
        private readonly LeagueSettingsService _settings;
        private readonly TeamBalancer _balancer;

        public LeagueBuilderService(ApplicationDbContext db, LeagueSettingsService settings, TeamBalancer balancer)
        {
            _db = db;
            _settings = settings;
            _balancer = balancer;
        }

        public static string ModeKey(LeagueMode mode) => mode.ToString().ToLowerInvariant();

        // Normal Draft has 5 lanes per team, so it tops out at 10 whatever the setting says (same rule as TeamBalancer).
        public static int EffectiveCap(LeagueMode mode, int playerCount) =>
            mode == LeagueMode.Normal ? Math.Min(playerCount, 10) : playerCount;

        public static string RankLabel(LeagueRank rank, int div) =>
            LeagueRankMath.HasDivision(rank) ? $"{rank} {Roman[Math.Clamp(div, 1, 4)]}" : rank.ToString();

        public static bool TryParseMode(string? key, out LeagueMode mode)
        {
            switch ((key ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "normal": mode = LeagueMode.Normal; return true;
                case "aram": mode = LeagueMode.Aram; return true;
                case "arena": mode = LeagueMode.Arena; return true;
                default: mode = LeagueMode.Normal; return false;
            }
        }

        // ── Page data ─────────────────────────────────────────────────────────────────────────────────────

        public async Task<LeagueBuilderPageModel> GetPageAsync()
        {
            var model = new LeagueBuilderPageModel { Lanes = LaneLabels.ToList() };

            foreach (var m in await _settings.GetAllAsync())
            {
                model.Modes.Add(new LeagueBuilderModeDto
                {
                    Key = ModeKey(m.Mode),
                    Name = LeagueModeRules.Name(m.Mode),
                    Cap = EffectiveCap(m.Mode, m.PlayerCount),
                    Teams = m.Mode == LeagueMode.Arena ? m.TeamCount : null,
                    MapUrl = m.MapImageUrl,
                    MapAspect = m.MapAspect > 0 ? m.MapAspect : 16d / 9d,
                    Spots = m.Spots.OrderBy(s => s.Team).ThenBy(s => s.Slot)
                        .Select(s => new LeagueBuilderSpotDto { Team = s.Team, Slot = s.Slot, X = s.X, Y = s.Y })
                        .ToList(),
                });
            }

            // Members with a league profile plus guests (plan decision for Step 4).
            var profiles = await _db.LeagueProfiles.AsNoTracking()
                .Include(p => p.Lanes)
                .Include(p => p.Champs)
                .OrderBy(p => p.InGameName)
                .ToListAsync();

            var badges = await LeagueBadges.LoadAsync(_db);
            foreach (var p in profiles)
            {
                var bp = BalancerPlayer.FromProfile(p);
                model.Players.Add(new LeagueBuilderPlayerDto
                {
                    Id = p.Id,
                    Name = p.InGameName,
                    IsGuest = p.IsGuest,
                    Avatar = string.IsNullOrWhiteSpace(p.AvatarUrl) ? null : p.AvatarUrl,
                    CurLabel = RankLabel(bp.CurRank, bp.CurDiv),
                    PeakLabel = RankLabel(bp.PeakRank, bp.PeakDiv),
                    CurKey = bp.CurRank.ToString().ToLowerInvariant(),
                    PeakKey = bp.PeakRank.ToString().ToLowerInvariant(),
                    Main = (int)bp.Lanes[0],
                    Lanes = bp.Lanes.Select(l => (int)l).ToList(),
                    MainPool = bp.Pool(bp.Lanes[0]),
                    DoBetterWithId = p.DoBetterWithId,
                    RankKing = badges.IsRankKing(p.Id),
                    MvpKing = badges.IsMvpKing(p.Id),
                    ClashKing = badges.IsClashKing(p.Id),
                });
            }
            return model;
        }

        // ── One request of POST /League/Generate ──────────────────────────────────────────────────────────

        public async Task<LeagueBuilderResponse> RunAsync(LeagueBuilderRequest req, Random? rng = null)
        {
            if (!TryParseMode(req.Mode, out var mode)) return Fail("Unknown game mode.");
            var ids = CollectIds(req);
            if (ids.Count > MaxPlayersPerRequest) return Fail("Too many players in one request.");

            var modes = await _settings.GetAllAsync();
            var ms = modes.First(m => m.Mode == mode);
            var tiers = await _db.LeagueCustomTiers.AsNoTracking().ToListAsync();
            var tierIndexes = BalancerPlayer.TierIndexes(tiers);
            var profiles = await _db.LeagueProfiles.AsNoTracking()
                .Include(p => p.Lanes)
                .Include(p => p.Champs)
                .Where(p => ids.Contains(p.Id))
                .ToListAsync();
            var pool = profiles.Select(p => BalancerPlayer.FromProfile(p, tierIndexes)).ToDictionary(p => p.Id);

            return Process(_balancer, req, pool, ms.PlayerCount, ms.TeamCount, tiers.Count, rng);
        }

        private static bool IsGenerate(LeagueBuilderRequest req) =>
            string.Equals((req.Op ?? string.Empty).Trim(), "generate", StringComparison.OrdinalIgnoreCase);

        // Every player id the request refers to. Generate uses only the roster; the other actions also
        // include whoever is on the posted teams.
        public static List<int> CollectIds(LeagueBuilderRequest req)
        {
            var ids = new HashSet<int>(req.PlayerIds ?? new List<int>());
            if (!IsGenerate(req))
            {
                foreach (var t in req.Teams ?? new List<LeagueBuilderTeamDto>())
                    foreach (var s in t?.Slots ?? new List<LeagueBuilderSlotDto>())
                        if (s?.PlayerId is int id) ids.Add(id);
            }
            return ids.ToList();
        }

        // The whole request / response logic, with no database: the pool is passed in, so the tests can drive it
        // with a seeded Random.
        public static LeagueBuilderResponse Process(
            TeamBalancer balancer, LeagueBuilderRequest req, IReadOnlyDictionary<int, BalancerPlayer> pool,
            int modePlayerCount, int? arenaTeams, int tierCount, Random? rng = null)
        {
            if (!TryParseMode(req.Mode, out var mode)) return Fail("Unknown game mode.");
            if (req.Balance < 1 || req.Balance > 4) return Fail("Unknown balance mode.");
            var balance = (LeagueBalance)req.Balance;
            var op = (req.Op ?? string.Empty).Trim().ToLowerInvariant();
            if (op != "generate" && op != "evaluate" && op != "clashsame" && op != "clashswap")
                return Fail("Unknown action.");

            var ids = CollectIds(req);
            if (ids.Count > MaxPlayersPerRequest) return Fail("Too many players in one request.");
            if (ids.Any(id => !pool.ContainsKey(id))) return Fail("A selected player no longer exists. Reload the page.");

            var breq = new BalancerRequest
            {
                Mode = mode,
                Balance = balance,
                AvoidLaneClashes = req.AvoidClashes,
                ModePlayerCount = modePlayerCount,
                ArenaTeamCount = arenaTeams,
                TierCount = tierCount,
                Players = ids.Select(id => pool[id]).ToList(),
            };

            // The clash check exists only for Normal Draft with "Random, respecting lanes" and the toggle on.
            var clashOn = mode == LeagueMode.Normal && balance == LeagueBalance.Lanes && req.AvoidClashes;

            if (op == "generate")
            {
                var generated = balancer.Generate(breq, rng);
                if (!generated.Ok) return Fail(generated.Error!);
                return Build(balancer, breq, generated, clashOn, null, null);
            }

            var (res, error) = ParseTeams(req.Teams, mode, balance);
            if (res == null) return Fail(error!);

            var ignored = new HashSet<(int, int)>();
            foreach (var pair in (req.Ignored ?? new List<List<int>>()).Take(MaxIgnoredPairs))
                if (pair != null && pair.Count == 2) ignored.Add(TeamBalancer.PairKey(pair[0], pair[1]));

            if (op == "evaluate") return Build(balancer, breq, res, clashOn, ignored, null);

            // clashSame / clashSwap
            if (!clashOn)
                return Fail("Lane clashes only apply to Normal Draft with \"Random, respecting lanes\" and the clash check turned on.");
            if (req.Clash == null || req.Clash.Lane < 0 || req.Clash.Lane > 4) return Fail("Missing lane clash.");

            var wanted = TeamBalancer.PairKey(req.Clash.A, req.Clash.B);
            var conflict = balancer.FindConflicts(breq, res, ignored)
                .FirstOrDefault(c => (int)c.Lane == req.Clash.Lane && TeamBalancer.PairKey(c.PlayerA, c.PlayerB) == wanted);
            if (conflict == null) return Fail("That lane clash is no longer on the board.");

            if (op == "clashsame")
            {
                var plan = balancer.PlanSameTeam(breq, res, conflict);
                if (plan == null) return Fail("No move keeps both players on a lane they play.");
                balancer.ApplySameTeam(breq, res, plan);
            }
            else
            {
                if (req.Swap == null) return Fail("Missing lane swap.");
                var swap = balancer.SecondaryLaneSwaps(breq, conflict)
                    .FirstOrDefault(s => s.PlayerId == req.Swap.PlayerId && (int)s.Lane == req.Swap.Lane);
                if (swap == null) return Fail("That lane swap isn't available.");
                if (!balancer.ApplySecondaryLaneSwap(res, swap)) return Fail("That lane swap couldn't be applied.");
            }

            return Build(balancer, breq, res, clashOn, ignored, null);
        }

        // Turns posted teams back into a BalancerResult, rejecting anything the builder could not have produced.
        // Public since Step 4d: submitting a match runs the same check.
        public static (BalancerResult? Res, string? Error) ParseTeams(List<LeagueBuilderTeamDto>? teams, LeagueMode mode, LeagueBalance balance)
        {
            if (teams == null || teams.Count == 0) return (null, "There are no teams to check.");
            if (teams.Count > MaxTeams) return (null, "Too many teams.");
            if (mode == LeagueMode.Normal && teams.Count != 2) return (null, "Normal Draft has exactly two teams.");

            var seen = new HashSet<int>();
            var res = new BalancerResult { Mode = mode, Balance = balance };

            for (var ti = 0; ti < teams.Count; ti++)
            {
                var t = teams[ti];
                if (t?.Slots == null || t.Slots.Count == 0 || t.Slots.Count > MaxPlayersPerRequest)
                    return (null, "A team has no slots.");
                if (mode == LeagueMode.Normal && t.Slots.Count != 5)
                    return (null, "Normal Draft teams have five lane slots.");

                var name = (t.Name ?? string.Empty).Trim();
                if (name.Length == 0) name = "Team " + (ti + 1);
                if (name.Length > MaxTeamNameLength) name = name.Substring(0, MaxTeamNameLength);
                var team = new BalancerTeam { Name = name };

                for (var si = 0; si < t.Slots.Count; si++)
                {
                    var s = t.Slots[si];
                    if (s == null) return (null, "A slot is invalid.");
                    LeagueLane? lane = null;
                    if (mode == LeagueMode.Normal)
                    {
                        if (s.Lane != si) return (null, "The lane slots are out of order.");
                        lane = (LeagueLane)si;
                    }
                    else if (s.Lane != null)
                    {
                        return (null, "Lanes only apply to Normal Draft.");
                    }
                    if (s.PlayerId is int pid && !seen.Add(pid)) return (null, "A player is on the board twice.");
                    team.Slots.Add(new BalancerSlot { Lane = lane, PlayerId = s.PlayerId });
                }
                res.Teams.Add(team);
            }
            return (res, null);
        }

        private static LeagueBuilderResponse Build(
            TeamBalancer balancer, BalancerRequest breq, BalancerResult res, bool clashOn,
            ISet<(int, int)>? ignored, List<LaneConflict>? conflicts)
        {
            conflicts ??= clashOn ? balancer.FindConflicts(breq, res, ignored) : new List<LaneConflict>();
            var showStrength = breq.Balance == LeagueBalance.Rank || breq.Balance == LeagueBalance.Tier;

            var resp = new LeagueBuilderResponse
            {
                Ok = true,
                Note = res.Note,
                Mode = ModeKey(res.Mode),
                Balance = (int)res.Balance,
                Teams = res.Teams.Select(t => new LeagueBuilderTeamDto
                {
                    Name = t.Name,
                    Strength = showStrength ? Math.Round(TeamBalancer.Strength(t, breq)) : null,
                    Slots = t.Slots.Select(s => new LeagueBuilderSlotDto
                    {
                        Lane = s.Lane is LeagueLane l ? (int)l : null,
                        PlayerId = s.PlayerId,
                    }).ToList(),
                }).ToList(),
                Conflicts = conflicts.Select(c => new LeagueBuilderClashRef { Lane = (int)c.Lane, A = c.PlayerA, B = c.PlayerB }).ToList(),
            };

            if (conflicts.Count > 0)
            {
                var c = conflicts[0];
                var plan = balancer.PlanSameTeam(breq, res, c);
                resp.Clash = new LeagueBuilderClashDto
                {
                    Lane = (int)c.Lane,
                    A = c.PlayerA,
                    B = c.PlayerB,
                    CanSameTeam = plan != null,
                    MovedPlayerId = plan?.MovedPlayerId,
                    Swaps = balancer.SecondaryLaneSwaps(breq, c)
                        .Select(s => new LeagueBuilderSwapRef { PlayerId = s.PlayerId, Lane = (int)s.Lane })
                        .ToList(),
                };
            }
            return resp;
        }

        private static LeagueBuilderResponse Fail(string message) => new LeagueBuilderResponse { Ok = false, Error = message };
    }
}
