using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Step 6d: the Clash page (docs/LEAGUE_BUILD_SPEC.md section 4). Loads the pool, the answers and the series record,
    // and runs one request of POST /LeagueClash/Build. The page posts its own busy set (answers plus manual toggles), so the
    // server keeps no state, like the team builder. Nothing here changes data; saving games is 6e.
    public class LeagueClashBoardService
    {
        public const int TeamSize = 5;

        private readonly ApplicationDbContext _db;
        private readonly ClashTeamBuilder _builder;

        public LeagueClashBoardService(ApplicationDbContext db, ClashTeamBuilder builder)
        {
            _db = db;
            _builder = builder;
        }

        // ------------------------------------------------------------------ pure helpers (unit-tested)

        // Which clash the page opens on: one that is running (game 1 started), else the next upcoming open one, else the
        // most recent open one whose start time has passed. The select lets the user pick any other.
        public static int? PickDefaultEventId(IReadOnlyCollection<LeagueClashEventOption> events, DateTime nowUtc)
        {
            var live = events.Where(e => e.Status == LeagueClashStatus.InProgress).OrderBy(e => e.StartsAtUtc).FirstOrDefault();
            if (live != null) return live.Id;
            var open = events.Where(e => e.Status == LeagueClashStatus.Open).OrderBy(e => e.StartsAtUtc).ToList();
            var next = open.FirstOrDefault(e => e.StartsAtUtc >= nowUtc);
            return (next ?? open.LastOrDefault())?.Id;
        }

        // Win = the Clash team (index 0) won, loss = the opponents won. Games with no winner yet are ignored.
        public static LeagueClashRecord RecordOf(IEnumerable<int?> winnerIndexes, int gamesSaved)
        {
            var wins = 0;
            var losses = 0;
            foreach (var w in winnerIndexes)
            {
                if (w == null) continue;
                if (w == 0) wins++; else losses++;
            }
            return new LeagueClashRecord { Wins = wins, Losses = losses, Games = gamesSaved };
        }

        // Step 6e3: the saved games for the Games tile, in game order (a missing number sorts first, then by id).
        public static List<LeagueClashGameDto> GamesOf(IEnumerable<(int Id, int? Number, LeagueMatchStatus Status, int? Winner)> rows) =>
            rows.OrderBy(r => r.Number ?? 0).ThenBy(r => r.Id)
                .Select(r => new LeagueClashGameDto
                {
                    Id = r.Id,
                    Number = r.Number ?? 0,
                    Status = LeagueMatchService.StatusKey(r.Status),
                    Winner = r.Status == LeagueMatchStatus.Done ? r.Winner : null,
                }).ToList();

        public static bool TryParseSystem(string? text, out ClashRankSystem system)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "rank": system = ClashRankSystem.Rank; return true;
                case "tier": system = ClashRankSystem.Tier; return true;
                default: system = ClashRankSystem.Rank; return false;
            }
        }

        // The busy set the page sent, cleaned: only players that exist. Guests count since 8i (a moderator can mark one busy).
        public static List<int> CleanBusy(IEnumerable<int>? ids, IReadOnlyDictionary<int, BalancerPlayer> pool) =>
            (ids ?? Array.Empty<int>()).Distinct().Where(id => pool.ContainsKey(id)).ToList();

        public static string FitLabel(ClashFit fit) => fit switch
        {
            ClashFit.Main => "Main",
            ClashFit.Second => "Good lane",
            ClashFit.Third => "OK lane",
            _ => "Last resort",
        };

        public static string FitKind(ClashFit fit) => fit == ClashFit.Main ? "ok" : "warn";

        private static LeagueClashBuildResponse Fail(string error) => new() { Ok = false, Error = error };

        // The whole request / response logic with no database: the pool comes in (already in a fixed order, so ties are
        // stable), the mode comes from the event (never from the browser).
        public static LeagueClashBuildResponse Process(
            ClashTeamBuilder builder, LeagueClashBuildRequest req, LeagueMode mode,
            IReadOnlyList<BalancerPlayer> players, int tierCount)
        {
            if (!LeagueClashScheduleService.IsClashMode(mode)) return Fail("A clash is Normal Draft or ARAM.");
            if (!TryParseSystem(req.RankSystem, out var system)) return Fail("Unknown rank system.");
            if (system == ClashRankSystem.Tier && tierCount < 2)
                return Fail("Custom tiers need at least two tiers in Settings > Tiers. Pick rank instead.");
            var op = (req.Op ?? "").Trim().ToLowerInvariant();
            if (op != "build" && op != "evaluate") return Fail("Unknown action.");

            var pool = players.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
            var busy = CleanBusy(req.Busy, pool);
            var creq = new ClashRequest
            {
                Mode = mode,
                RankSystem = system,
                TierCount = tierCount,
                Players = players,
                Busy = busy,
            };
            var modeKey = LeagueBuilderService.ModeKey(mode);
            var systemKey = system == ClashRankSystem.Tier ? "tier" : "rank";

            if (op == "build")
            {
                var result = builder.Build(creq);
                return new LeagueClashBuildResponse
                {
                    Ok = true,
                    Mode = modeKey,
                    RankSystem = systemKey,
                    IsEmpty = result.IsEmpty,
                    Strength = Math.Round(result.Strength, 2),
                    Slots = result.Slots.Select(s => ToDto(s, mode, pool)).ToList(),
                    Log = result.Log.Select(l => new LeagueClashLogDto { Kind = l.Kind, Text = l.Text }).ToList(),
                    NoFit = result.NoFit.Select(n => new LeagueClashNoFitDto
                    {
                        BusyPlayerId = n.BusyPlayerId, ReplacementPlayerId = n.ReplacementPlayerId, Lane = (int)n.Lane,
                    }).ToList(),
                };
            }

            // evaluate: the team as it is on screen after a hand edit.
            var posted = req.Slots;
            if (posted == null || posted.Count != TeamSize) return Fail("The team needs exactly 5 slots.");
            var seen = new HashSet<int>();
            var slots = new List<ClashSlot>();
            for (var i = 0; i < TeamSize; i++)
            {
                var s = posted[i] ?? new LeagueClashSlotDto();
                if (s.PlayerId is int id)
                {
                    if (!pool.ContainsKey(id)) return Fail("A selected player no longer exists. Reload the page.");
                    if (!seen.Add(id)) return Fail("A player can only be on the team once.");
                }
                // Slot i is lane i in Normal; ARAM has no lanes. The browser's lane value is not trusted.
                slots.Add(new ClashSlot
                {
                    Lane = mode == LeagueMode.Normal ? (LeagueLane)i : null,
                    PlayerId = s.PlayerId,
                    Manual = s.Manual && s.PlayerId != null,
                });
            }
            var warnings = slots.Where(s => s.PlayerId is int id && busy.Contains(id))
                .Select(s => $"{pool[s.PlayerId!.Value].Name} is marked busy but is on the team.")
                .ToList();
            return new LeagueClashBuildResponse
            {
                Ok = true,
                Mode = modeKey,
                RankSystem = systemKey,
                IsEmpty = slots.All(s => s.PlayerId == null),
                Strength = Math.Round(ClashTeamBuilder.StrengthOf(slots, creq), 2),
                Slots = slots.Select(s => ToDto(s, mode, pool)).ToList(),
                Warnings = warnings,
            };
        }

        private static LeagueClashSlotDto ToDto(ClashSlot s, LeagueMode mode, IReadOnlyDictionary<int, BalancerPlayer> pool)
        {
            var dto = new LeagueClashSlotDto
            {
                Lane = s.Lane is LeagueLane l ? (int)l : null,
                PlayerId = s.PlayerId,
                Manual = s.Manual,
            };
            if (mode == LeagueMode.Normal && s.Lane is LeagueLane lane && s.PlayerId is int id && pool.TryGetValue(id, out var p))
            {
                var fit = ClashTeamBuilder.FitFor(p, lane);
                dto.Fit = FitLabel(fit);
                dto.FitKind = FitKind(fit);
            }
            return dto;
        }

        // ------------------------------------------------------------------ page data

        public async Task<LeagueClashPageModel> GetPageAsync(int? requestedId)
        {
            var model = new LeagueClashPageModel { Lanes = LeagueBuilderService.LaneLabels.ToList() };

            var events = await _db.LeagueClashEvents.AsNoTracking()
                .Where(e => e.Status == LeagueClashStatus.Open || e.Status == LeagueClashStatus.InProgress)
                .Select(e => new LeagueClashEventOption
                {
                    Id = e.Id, Title = e.Title, StartsAtUtc = e.StartsAtUtc, Mode = e.Mode, Status = e.Status,
                })
                .ToListAsync();
            model.Events = events.OrderBy(e => e.StartsAtUtc).ThenBy(e => e.Id).ToList();

            var selectedId = requestedId is int r && model.Events.Any(e => e.Id == r)
                ? r
                : PickDefaultEventId(model.Events, DateTime.UtcNow);
            if (requestedId != null && selectedId != requestedId && model.Events.Count > 0)
                model.Notice = "That clash is over, cancelled or no longer exists, so the next open one is shown.";
            if (selectedId == null) return model;

            model.SelectedId = selectedId;
            model.Selected = model.Events.First(e => e.Id == selectedId);

            var selectedMode = model.Selected.Mode;
            // The map image, its shape and the player places of the clash's mode (Arena has no places, so its list stays empty).
            var mapRow = await _db.LeagueModeSettings.AsNoTracking()
                .Where(m => m.Mode == selectedMode)
                .Select(m => new
                {
                    m.MapImageUrl,
                    m.MapAspect,
                    Spots = m.Spots.OrderBy(sp => sp.Team).ThenBy(sp => sp.Slot)
                        .Select(sp => new LeagueBuilderSpotDto { Team = sp.Team, Slot = sp.Slot, X = sp.X, Y = sp.Y })
                        .ToList(),
                })
                .FirstOrDefaultAsync();
            model.MapUrl = mapRow?.MapImageUrl;
            if (mapRow != null)
            {
                model.MapAspect = mapRow.MapAspect > 0 ? mapRow.MapAspect : 16d / 9d;
                model.Spots = mapRow.Spots;
            }

            var tiers = await _db.LeagueCustomTiers.AsNoTracking().ToListAsync();
            model.HasTiers = tiers.Count >= 2;

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
                    CurLabel = LeagueBuilderService.RankLabel(bp.CurRank, bp.CurDiv),
                    PeakLabel = LeagueBuilderService.RankLabel(bp.PeakRank, bp.PeakDiv),
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

            var answers = await _db.LeagueClashResponses.AsNoTracking()
                .Where(a => a.EventId == selectedId)
                .Select(a => new { a.ProfileId, a.Status })
                .ToListAsync();
            // Step 8i: guests too (a moderator saves a guest's answer on this page).
            model.AnsweredBusy = answers.Where(a => a.Status == LeagueClashAnswer.Busy).Select(a => a.ProfileId).ToList();
            model.AnsweredFree = answers.Where(a => a.Status == LeagueClashAnswer.Free).Select(a => a.ProfileId).ToList();

            // Step 11c: a clash with no team yet gets "Team one" now (old clashes were given it by the 11b migration).
            await LeagueClashTeamData.EnsureFirstTeamIdAsync(_db, selectedId.Value);

            var games = await _db.LeagueMatches.AsNoTracking()
                .Where(m => m.ClashEventId == selectedId)
                .Select(m => new
                {
                    m.Id, m.GameNumber, m.Status, m.WinnerTeamIndex, m.ClashTeamId,
                    // Step 11g: who plays the game (the Clash team, index 0).
                    Players = m.Teams.Where(t => t.Index == 0).SelectMany(t => t.Players)
                        .Where(p => p.ProfileId != null).Select(p => p.ProfileId!.Value).ToList(),
                })
                .ToListAsync();
            model.Record = RecordOf(games.Where(g => g.Status == LeagueMatchStatus.Done).Select(g => g.WinnerTeamIndex), games.Count);
            model.Games = GamesOf(games.Select(g => (g.Id, g.GameNumber, g.Status, g.WinnerTeamIndex)));
            var teamOfGame = games.ToDictionary(g => g.Id, g => g.ClashTeamId);
            var playersOfGame = games.ToDictionary(g => g.Id, g => g.Players);
            foreach (var g in model.Games)
            {
                g.TeamId = teamOfGame.TryGetValue(g.Id, out var tid) ? tid : null;
                if (playersOfGame.TryGetValue(g.Id, out var pl)) g.PlayerIds = pl;
            }

            var teams = await _db.LeagueClashTeams.AsNoTracking()
                .Where(t => t.EventId == selectedId)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
                .ToListAsync();
            model.Teams = teams.Select(t => new LeagueClashTeamDto
            {
                Id = t.Id, Name = t.Name, SortOrder = t.SortOrder, Games = games.Count(g => g.ClashTeamId == t.Id),
            }).ToList();
            return model;
        }

        // ------------------------------------------------------------------ one request of POST /LeagueClash/Build

        public async Task<LeagueClashBuildResponse> RunAsync(LeagueClashBuildRequest req)
        {
            var ev = await _db.LeagueClashEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == req.EventId);
            if (ev == null) return Fail("That clash no longer exists. Reload the page.");
            if (ev.Status != LeagueClashStatus.Open && ev.Status != LeagueClashStatus.InProgress)
                return Fail("That clash is over or cancelled. Reload the page.");
            if ((req.Slots?.Count ?? 0) > 50 || (req.Busy?.Count ?? 0) > LeagueBuilderService.MaxPlayersPerRequest * 5)
                return Fail("The request was too large.");

            var (players, tierCount) = await LoadPoolAsync();
            return Process(_builder, req, ev.Mode, players, tierCount);
        }

        // The pool in a fixed order (so ties are stable) and the number of custom tiers. Used by Build and Next game.
        private async Task<(List<BalancerPlayer> Players, int TierCount)> LoadPoolAsync()
        {
            var tiers = await _db.LeagueCustomTiers.AsNoTracking().ToListAsync();
            var tierIndexes = BalancerPlayer.TierIndexes(tiers);
            var profiles = await _db.LeagueProfiles.AsNoTracking()
                .Include(p => p.Lanes)
                .Include(p => p.Champs)
                .OrderBy(p => p.InGameName)
                .ThenBy(p => p.Id)
                .ToListAsync();
            return (profiles.Select(p => BalancerPlayer.FromProfile(p, tierIndexes)).ToList(), tiers.Count);
        }

        // ------------------------------------------------------------------ one request of POST /LeagueClash/NextGame (6e4d)

        // Read-only. Loads the clash and its games, picks the last finished game, turns its Clash team into slots and runs
        // them through the same recheck as a hand edit ("evaluate"), so strength and fit tags are current: ranks and lanes
        // may have changed since the game was saved. Nothing is stored; Save as game N (6e1) does the saving.
        public async Task<LeagueClashNextGameResponse> NextGameAsync(LeagueClashNextGameRequest req)
        {
            var ev = await _db.LeagueClashEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == req.EventId);
            if (ev == null) return NextFail("That clash no longer exists. Reload the page.");
            if ((req.Busy?.Count ?? 0) > LeagueBuilderService.MaxPlayersPerRequest * 5)
                return NextFail("The request was too large.");

            // Step 11c: the team whose last game is copied (none sent = the first team). Read-only: no team row is created here.
            int? teamId = req.TeamId;
            if (teamId is int asked)
            {
                var ok = await _db.LeagueClashTeams.AsNoTracking().AnyAsync(t => t.Id == asked && t.EventId == ev.Id);
                if (!ok) return NextFail("That team no longer exists in this clash. Reload the page.");
            }
            else
            {
                teamId = await LeagueClashTeamData.FirstTeamIdAsync(_db, ev.Id);
            }

            var games = await _db.LeagueMatches.AsNoTracking()
                .Include(m => m.Teams).ThenInclude(t => t.Players)
                .Where(m => m.ClashEventId == ev.Id && m.ClashTeamId == teamId)
                .ToListAsync();
            var (source, number, error) = LeagueClashGameRules.CheckNext(ev, games);
            if (error != null || source == null) return NextFail(error ?? "There is no game to copy the team from.");

            var (players, tierCount) = await LoadPoolAsync();
            var (slots, missing) = LeagueClashGameRules.SlotsFromGame(source, ev.Mode, players.Select(p => p.Id).ToHashSet());
            var evaluate = new LeagueClashBuildRequest
            {
                EventId = ev.Id, Op = "evaluate", RankSystem = req.RankSystem, Busy = req.Busy ?? new List<int>(), Slots = slots,
            };
            var team = Process(_builder, evaluate, ev.Mode, players, tierCount);
            if (!team.Ok) return NextFail(team.Error ?? "The team could not be checked.");
            foreach (var name in missing) team.Warnings.Add($"{name} is no longer in the pool, so that slot is empty.");
            return new LeagueClashNextGameResponse { Ok = true, NextNumber = number, FromGame = source.GameNumber ?? 0, Team = team };
        }

        private static LeagueClashNextGameResponse NextFail(string error) => new() { Ok = false, Error = error };

        // ------------------------------------------------------------------ one request of GET /LeagueClash/ProgressImage (12d)

        // Read-only. The data of the Clash progress picture for ONE team of a clash (none sent = the clash's first team).
        // Works for a clash in any status except cancelled; the rules (LeagueClashProgressRules) decide what is shown.
        public async Task<LeagueClashProgressResponse> ProgressImageAsync(int eventId, int? teamId)
        {
            var ev = await _db.LeagueClashEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null) return ProgressFail("That clash no longer exists. Reload the page.");
            if (ev.Status == LeagueClashStatus.Cancelled) return ProgressFail("That clash was cancelled.");

            var team = teamId is int asked
                ? await _db.LeagueClashTeams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == asked && t.EventId == ev.Id)
                : await _db.LeagueClashTeams.AsNoTracking().Where(t => t.EventId == ev.Id)
                    .OrderBy(t => t.SortOrder).ThenBy(t => t.Id).FirstOrDefaultAsync();
            if (team == null) return ProgressFail("That team no longer exists in this clash. Reload the page.");

            var games = await _db.LeagueMatches.AsNoTracking()
                .Include(m => m.Teams).ThenInclude(t => t.Players)
                .Where(m => m.ClashEventId == ev.Id && m.ClashTeamId == team.Id)
                .ToListAsync();
            var spec = LeagueClashProgressRules.Build(ev, team.Id, team.Name, games);
            if (spec == null) return ProgressFail("Finish game 1 first. There is no finished game to show yet.");
            return new LeagueClashProgressResponse { Ok = true, Spec = spec };
        }

        private static LeagueClashProgressResponse ProgressFail(string error) => new() { Ok = false, Error = error };
    }
}
