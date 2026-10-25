using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Balancing modes, numbered as in the prototype (docs/league-team-builder-preview.html, gen / B.bal).
    public enum LeagueBalance
    {
        Random = 1,   // shuffle everyone
        Lanes = 2,    // random teams, but respecting each player's lanes
        Rank = 3,     // two teams: rank (60% current + 40% peak) + lanes
        Tier = 4,     // two teams: custom tiers + lanes
    }

    // One player as the balancer sees them. Plain data, no EF, so the logic is testable without a database.
    public class BalancerPlayer
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public bool IsGuest { get; init; }
        public LeagueRank CurRank { get; init; }
        public int CurDiv { get; init; }            // 0 for Master and above
        public LeagueRank PeakRank { get; init; }
        public int PeakDiv { get; init; }
        // The lanes the player plays (level above 1), best first: index 0 = main lane (highest level), 1 = next, and so on.
        public IReadOnlyList<LeagueLane> Lanes { get; init; } = Array.Empty<LeagueLane>();
        // Step 8f: the level (1-5) of each lane. Optional: when it is null the levels come from the order of Lanes
        // (5, 4, 3, 2, 1), so a player built by hand with only Lanes keeps the old costs. A lane that is missing is level 1.
        public IReadOnlyDictionary<LeagueLane, int>? LaneLevels { get; init; }
        // Number of catalog champions the player listed per lane (spec R6: the clash rule uses "fewer than 3").
        public IReadOnlyDictionary<LeagueLane, int> PoolSize { get; init; } = new Dictionary<LeagueLane, int>();
        // 0 = strongest custom tier. Null = no tier (falls back to rank, see TeamBalancer.Value).
        public int? TierIndex { get; init; }
        public int? DoBetterWithId { get; init; }

        public LeagueLane? Main => Lanes.Count > 0 ? Lanes[0] : null;
        public int Pool(LeagueLane lane) => PoolSize.TryGetValue(lane, out var n) ? n : 0;

        public int LevelOf(LeagueLane lane)
        {
            if (LaneLevels != null)
                return LaneLevels.TryGetValue(lane, out var v) ? LeagueLaneLevels.Clamp(v) : LeagueLaneLevels.Min;
            for (var k = 0; k < Lanes.Count; k++)
                if (Lanes[k] == lane) return LeagueLaneLevels.LevelFromPriority(k + 1);
            return LeagueLaneLevels.Min;
        }

        // True when the player plays the lane (level above 1).
        public bool Plays(LeagueLane lane) => LevelOf(lane) > LeagueLaneLevels.Min;

        // tierIndexById: custom tier id -> index in strength order (see TierIndexes).
        public static BalancerPlayer FromProfile(LeagueProfile p, IReadOnlyDictionary<int, int>? tierIndexById = null)
        {
            // Step 8f: best level first; a tie keeps the old order (Priority). Lanes at level 1 are "not played".
            var levels = LeagueLaneLevels.LevelsOf(p.Lanes.Select(l => new LaneLevel(l.Lane, l.Level, l.Priority)));
            var lanes = p.Lanes.Where(l => LeagueLaneLevels.Clamp(l.Level) > LeagueLaneLevels.Min)
                .OrderByDescending(l => LeagueLaneLevels.Clamp(l.Level)).ThenBy(l => l.Priority).ThenBy(l => (int)l.Lane)
                .Select(l => l.Lane).ToList();
            if (lanes.Count == 0)
            {
                // No lane above level 1 (should not happen): the stored main lane counts as the top lane.
                lanes.Add(p.MainLane);
                levels[p.MainLane] = LeagueLaneLevels.Max;
            }
            int? tier = null;
            if (p.CustomTierId is int tid && tierIndexById != null && tierIndexById.TryGetValue(tid, out var ti)) tier = ti;
            return new BalancerPlayer
            {
                Id = p.Id,
                Name = p.InGameName,
                IsGuest = p.IsGuest,
                CurRank = p.CurTier,
                CurDiv = LeagueRankMath.HasDivision(p.CurTier) ? (p.CurDiv ?? 4) : 0,
                PeakRank = p.PeakTier,
                PeakDiv = LeagueRankMath.HasDivision(p.PeakTier) ? (p.PeakDiv ?? 4) : 0,
                Lanes = lanes,
                LaneLevels = levels,
                PoolSize = p.Champs.GroupBy(c => c.Lane).ToDictionary(g => g.Key, g => g.Count()),
                TierIndex = tier,
                DoBetterWithId = p.DoBetterWithId,
            };
        }

        // Custom tier id -> index (0 = strongest). SortOrder 1 is the top tier (Step 3).
        public static Dictionary<int, int> TierIndexes(IEnumerable<LeagueCustomTier> tiers)
        {
            var d = new Dictionary<int, int>();
            var i = 0;
            foreach (var t in tiers.OrderBy(t => t.SortOrder).ThenBy(t => t.Id)) d[t.Id] = i++;
            return d;
        }
    }

    public static class LeagueRankMath
    {
        // Iron..Diamond have divisions IV..I; Master and above do not (prototype hasDiv: t <= 6).
        public static bool HasDivision(LeagueRank r) => (int)r <= (int)LeagueRank.Diamond;

        // Prototype rScore: tier * 4 + (4 - division). Division 4 is the lowest, 1 the highest.
        public static int Score(LeagueRank r, int div) => (int)r * 4 + (HasDivision(r) ? 4 - div : 0);
    }

    public class BalancerRequest
    {
        public LeagueMode Mode { get; init; } = LeagueMode.Normal;
        public LeagueBalance Balance { get; init; } = LeagueBalance.Lanes;
        // "Avoid same-lane clashes" (only used by Lanes balance in Normal mode).
        public bool AvoidLaneClashes { get; init; } = true;
        // From LeagueModeSettings: the configured player count (Normal is capped at 10) and Arena team count.
        public int ModePlayerCount { get; init; } = 10;
        public int? ArenaTeamCount { get; init; }
        // Number of custom tiers; players carry their own TierIndex.
        public int TierCount { get; init; }
        public IReadOnlyList<BalancerPlayer> Players { get; init; } = Array.Empty<BalancerPlayer>();

        private Dictionary<int, BalancerPlayer>? _byId;
        public IReadOnlyDictionary<int, BalancerPlayer> ById => _byId ??= Players.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
    }

    public class BalancerSlot
    {
        public LeagueLane? Lane { get; set; }       // null for ARAM / Arena
        public int? PlayerId { get; set; }
    }

    public class BalancerTeam
    {
        public string Name { get; set; } = "";
        public List<BalancerSlot> Slots { get; set; } = new();
    }

    public class BalancerResult
    {
        public LeagueMode Mode { get; set; }
        public LeagueBalance Balance { get; set; }
        public List<BalancerTeam> Teams { get; set; } = new();
        // Shown to the user ("Lanes don't apply to ARAM ..."). Null when there is nothing to say.
        public string? Note { get; set; }
        // Set instead of Teams when the roster cannot be balanced (too few / too many players).
        public string? Error { get; set; }
        public bool Ok => Error == null;
    }

    // Two players who both main the same lane and both have fewer than 3 champions there (spec R6).
    public record LaneConflict(LeagueLane Lane, int PlayerA, int PlayerB);

    // "Swap one to a secondary lane" option of the clash dialog.
    public record SecondaryLaneSwap(int PlayerId, LeagueLane Lane);

    // "Put both on the same team" option: the new rosters and lane assignments.
    public class SameTeamPlan
    {
        public List<int> TeamA { get; init; } = new();
        public List<int> TeamB { get; init; } = new();
        public int[] PermA { get; init; } = Array.Empty<int>();
        public int[] PermB { get; init; } = Array.Empty<int>();
        public int Total { get; init; }
        public int MovedPlayerId { get; init; }
    }

    // Server-side port of the prototype's generation and lane-clash code (spec R1). The server generates; the
    // browser only renders. Stateless, so it is registered as a singleton. The RNG is injectable: pass a seeded
    // System.Random to get a deterministic result in tests.
    public class TeamBalancer
    {
        // Cost of putting a player in a lane, by the level of that lane (Step 8f, LeagueLaneLevels.CostForLevel):
        // 5 = 0, 4 = 1, 3 = 4, 2 = 6, 1 = 8. A lane the player does not play is level 1. (Before 8f the cost came from the
        // position in the lane list, with 12 for an unlisted lane; converted profiles keep the same numbers.)
        private const int Attempts = 500;

        public static int LaneCostFor(BalancerPlayer p, LeagueLane lane) =>
            LeagueLaneLevels.CostForLevel(p.LevelOf(lane));

        // Prototype blend: 60% current + 40% peak.
        public static double Blend(BalancerPlayer p) =>
            LeagueRankMath.Score(p.CurRank, p.CurDiv) * .6 + LeagueRankMath.Score(p.PeakRank, p.PeakDiv) * .4;

        // Prototype valOf / tierVal. Tier balance scales the tier position onto the same 0..36 range as rank.
        // Deviation from the prototype (Step 3 decision): a player with no tier falls back to rank, and so does
        // everyone when there are fewer than 2 tiers (the prototype used 0 / the middle tier there).
        public static double Value(BalancerPlayer p, LeagueBalance balance, int tierCount)
        {
            if (balance != LeagueBalance.Tier) return Blend(p);
            if (tierCount < 2 || p.TierIndex is not int i) return Blend(p);
            i = Math.Clamp(i, 0, tierCount - 1);
            return (tierCount - 1 - i) * (36.0 / (tierCount - 1));
        }

        // Prototype bestPerm: the cheapest assignment of up to 5 players to the 5 lanes (branch and bound).
        // Asg[i] is the lane index (LeagueLane value) of team[i]. Ties keep the first one found.
        public static (int Cost, int[] Asg) BestPerm(IReadOnlyList<BalancerPlayer> team)
        {
            var n = team.Count;
            if (n > 5) throw new ArgumentException("A lane team has at most 5 players.", nameof(team));
            var best = int.MaxValue;
            var bestAsg = Array.Empty<int>();
            var used = new bool[5];
            var cur = new int[n];

            void Rec(int i, int c)
            {
                if (c >= best) return;
                if (i == n) { best = c; bestAsg = (int[])cur.Clone(); return; }
                for (var k = 0; k < 5; k++)
                {
                    if (used[k]) continue;
                    used[k] = true;
                    cur[i] = k;
                    Rec(i + 1, c + LaneCostFor(team[i], (LeagueLane)k));
                    used[k] = false;
                }
            }

            Rec(0, 0);
            return (best, bestAsg);
        }

        public static List<BalancerSlot> SlotsFromPerm(IReadOnlyList<BalancerPlayer> team, int[] asg)
        {
            var slots = Enumerable.Range(0, 5).Select(k => new BalancerSlot { Lane = (LeagueLane)k }).ToList();
            for (var i = 0; i < team.Count; i++) slots[asg[i]].PlayerId = team[i].Id;
            return slots;
        }

        public static List<T> Shuffle<T>(IEnumerable<T> source, Random rng)
        {
            var a = source.ToList();
            for (var i = a.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
            return a;
        }

        // ── Generation ────────────────────────────────────────────────────────────────────────────────────

        public BalancerResult Generate(BalancerRequest req, Random? rng = null)
        {
            rng ??= new Random();
            var name = LeagueModeRules.Name(req.Mode);
            var res = new BalancerResult { Mode = req.Mode, Balance = req.Balance };
            var P = req.Players.ToList();
            var cap = req.Mode == LeagueMode.Normal ? Math.Min(req.ModePlayerCount, 10) : req.ModePlayerCount;

            if (P.Count < 2) { res.Error = "Pick at least 2 players for the roster."; return res; }
            if (P.Count > cap) { res.Error = $"Roster has {P.Count} players but {name} allows {cap}. Remove {P.Count - cap}."; return res; }

            if (req.Mode == LeagueMode.Normal)
            {
                res.Teams = GenNormal(req, P, rng);
            }
            else
            {
                int T, size;
                if (req.Mode == LeagueMode.Arena)
                {
                    var wanted = req.ArenaTeamCount is int t && t > 0 ? t : (int)Math.Ceiling(req.ModePlayerCount / 2.0);
                    T = Math.Max(2, Math.Min(wanted, P.Count));
                    size = (int)Math.Ceiling(P.Count / (double)T);
                }
                else
                {
                    T = 2;
                    size = (int)Math.Ceiling(req.ModePlayerCount / 2.0);
                }
                res.Teams = GenOther(req, P, T, size, rng);
                if (req.Balance == LeagueBalance.Lanes) res.Note = "Lanes don't apply to " + name + ", so this mode was assigned randomly.";
            }
            return res;
        }

        private (List<BalancerSlot> Slots, int Cost) LaneSlots(List<BalancerPlayer> team, Random rng)
        {
            var t = Shuffle(team, rng);
            var b = BestPerm(t);
            return (SlotsFromPerm(t, b.Asg), b.Cost);
        }

        private static List<BalancerSlot> RandSlots(List<BalancerPlayer> team, Random rng)
        {
            var idx = Shuffle(Enumerable.Range(0, 5), rng);
            var slots = Enumerable.Range(0, 5).Select(k => new BalancerSlot { Lane = (LeagueLane)k }).ToList();
            for (var i = 0; i < team.Count; i++) slots[idx[i]].PlayerId = team[i].Id;
            return slots;
        }

        private List<BalancerTeam> GenNormal(BalancerRequest req, List<BalancerPlayer> P, Random rng)
        {
            List<BalancerSlot>? bestA = null, bestB = null;
            var bestObj = double.MaxValue;
            var n = req.Balance == LeagueBalance.Random ? 1 : Attempts;

            for (var k = 0; k < n; k++)
            {
                var sh = Shuffle(P, rng);
                var h = (sh.Count + 1) / 2;
                var A = sh.Take(h).ToList();
                var Bt = sh.Skip(h).ToList();
                List<BalancerSlot> sa, sb;
                double obj;

                if (req.Balance == LeagueBalance.Random)
                {
                    sa = RandSlots(A, rng);
                    sb = RandSlots(Bt, rng);
                    obj = 0;
                }
                else
                {
                    var la = LaneSlots(A, rng);
                    var lb = LaneSlots(Bt, rng);
                    sa = la.Slots; sb = lb.Slots;
                    var cost = la.Cost + lb.Cost;
                    if (req.Balance == LeagueBalance.Lanes)
                    {
                        obj = cost + (req.AvoidLaneClashes ? FindConflicts(req, sa, sb, null).Count * 8 : 0);
                    }
                    else
                    {
                        double Sum(List<BalancerPlayer> t) => t.Sum(p => Value(p, req.Balance, req.TierCount));
                        double ld = 0;
                        for (var i = 0; i < 5; i++)
                        {
                            if (sa[i].PlayerId is int ia && sb[i].PlayerId is int ib)
                                ld += Math.Abs(Value(req.ById[ia], req.Balance, req.TierCount) - Value(req.ById[ib], req.Balance, req.TierCount));
                        }
                        obj = Math.Abs(Sum(A) - Sum(Bt)) * 2 + cost * .5 + ld * .3;
                    }
                }

                if (bestA == null || obj < bestObj) { bestObj = obj; bestA = sa; bestB = sb; }
            }

            return new List<BalancerTeam>
            {
                new BalancerTeam { Name = "Team 1", Slots = bestA! },
                new BalancerTeam { Name = "Team 2", Slots = bestB! },
            };
        }

        private List<BalancerTeam> GenOther(BalancerRequest req, List<BalancerPlayer> P, int T, int size, Random rng)
        {
            List<List<BalancerPlayer>> groups;
            double Val(BalancerPlayer p) => Value(p, req.Balance, req.TierCount);

            if (req.Balance < LeagueBalance.Rank || P.Count < T)
            {
                var sh = Shuffle(P, rng);
                groups = Enumerable.Range(0, T).Select(i => sh.Where((_, j) => j % T == i).ToList()).ToList();
            }
            else if (T == 2)
            {
                List<BalancerPlayer>? bestA = null, bestB = null;
                var bestD = double.MaxValue;
                for (var k = 0; k < Attempts; k++)
                {
                    var sh = Shuffle(P, rng);
                    var h = (sh.Count + 1) / 2;
                    var A = sh.Take(h).ToList();
                    var Bt = sh.Skip(h).ToList();
                    var d = Math.Abs(A.Sum(p => Val(p)) - Bt.Sum(p => Val(p)));
                    if (bestA == null || d < bestD) { bestD = d; bestA = A; bestB = Bt; }
                }
                groups = new List<List<BalancerPlayer>> { bestA!, bestB! };
            }
            else
            {
                // Snake draft over the sorted list, with a little noise so repeated runs differ.
                var sorted = P.Select(p => (p, v: Val(p) + rng.NextDouble() * .8)).OrderByDescending(x => x.v).Select(x => x.p).ToList();
                groups = Enumerable.Range(0, T).Select(_ => new List<BalancerPlayer>()).ToList();
                for (var i = 0; i < sorted.Count; i++)
                {
                    var r = i / T;
                    groups[r % 2 == 1 ? T - 1 - i % T : i % T].Add(sorted[i]);
                }
            }

            return groups.Select((g, i) => new BalancerTeam
            {
                Name = (req.Mode == LeagueMode.Arena ? (size == 2 ? "Duo " : "Team ") : "Team ") + (i + 1),
                Slots = Enumerable.Range(0, size).Select(j => new BalancerSlot { Lane = null, PlayerId = j < g.Count ? g[j].Id : null }).ToList(),
            }).ToList();
        }

        // Sum of the player values of a team (what the builder shows next to the team name).
        public static double Strength(BalancerTeam team, BalancerRequest req) =>
            team.Slots.Sum(s => s.PlayerId is int id && req.ById.TryGetValue(id, out var p) ? Value(p, req.Balance, req.TierCount) : 0);

        // ── Lane-clash flow (Normal mode, Lanes balance) ──────────────────────────────────────────────────

        // Order-independent key for an "ignore this pair" decision.
        public static (int, int) PairKey(int a, int b) => a < b ? (a, b) : (b, a);

        public List<LaneConflict> FindConflicts(BalancerRequest req, BalancerResult res, ISet<(int, int)>? ignored = null)
        {
            if (res.Mode != LeagueMode.Normal || res.Teams.Count < 2) return new List<LaneConflict>();
            return FindConflicts(req, res.Teams[0].Slots, res.Teams[1].Slots, ignored);
        }

        private List<LaneConflict> FindConflicts(BalancerRequest req, List<BalancerSlot> sa, List<BalancerSlot> sb, ISet<(int, int)>? ignored)
        {
            var list = new List<LaneConflict>();
            for (var i = 0; i < 5 && i < sa.Count && i < sb.Count; i++)
            {
                var lane = (LeagueLane)i;
                if (sa[i].PlayerId is not int ia || sb[i].PlayerId is not int ib) continue;
                var a = req.ById[ia];
                var b = req.ById[ib];
                if (a.Main == lane && b.Main == lane && a.Pool(lane) < 3 && b.Pool(lane) < 3
                    && (ignored == null || !ignored.Contains(PairKey(a.Id, b.Id))))
                    list.Add(new LaneConflict(lane, a.Id, b.Id));
            }
            return list;
        }

        // Option 1 "Put both on the same team": move the second player onto team 1, send one other team-1 player
        // across to keep the sizes equal, and keep the cheapest lane layout. A secondary lane must exist for
        // both of them. Returns null when no such move works.
        public SameTeamPlan? PlanSameTeam(BalancerRequest req, BalancerResult res, LaneConflict c)
        {
            if (res.Teams.Count < 2) return null;
            var a = req.ById[c.PlayerA];
            var b = req.ById[c.PlayerB];
            var ta = res.Teams[0].Slots.Where(s => s.PlayerId != null).Select(s => s.PlayerId!.Value).ToList();
            var tb = res.Teams[1].Slots.Where(s => s.PlayerId != null).Select(s => s.PlayerId!.Value).ToList();
            SameTeamPlan? best = null;

            foreach (var cid in ta.Where(x => x != a.Id))
            {
                var nA = ta.Where(x => x != cid).Append(b.Id).ToList();
                var nB = tb.Where(x => x != b.Id).Append(cid).ToList();
                var pa = BestPerm(nA.Select(id => req.ById[id]).ToList());
                var pb = BestPerm(nB.Select(id => req.ById[id]).ToList());
                var ia = nA.IndexOf(a.Id);
                var ib = nA.IndexOf(b.Id);
                // Both must end up on a lane they play (level above 1); this was "cost >= OffLaneCost" before 8f.
                if (!a.Plays((LeagueLane)pa.Asg[ia]) || !b.Plays((LeagueLane)pa.Asg[ib])) continue;
                var tot = pa.Cost + pb.Cost;
                if (best == null || tot < best.Total)
                    best = new SameTeamPlan { TeamA = nA, TeamB = nB, PermA = pa.Asg, PermB = pb.Asg, Total = tot, MovedPlayerId = cid };
            }
            return best;
        }

        public void ApplySameTeam(BalancerRequest req, BalancerResult res, SameTeamPlan plan)
        {
            res.Teams[0].Slots = SlotsFromPerm(plan.TeamA.Select(id => req.ById[id]).ToList(), plan.PermA);
            res.Teams[1].Slots = SlotsFromPerm(plan.TeamB.Select(id => req.ById[id]).ToList(), plan.PermB);
        }

        // Option 2 "Swap one to a secondary lane": every non-main lane of either player is offered.
        public List<SecondaryLaneSwap> SecondaryLaneSwaps(BalancerRequest req, LaneConflict c)
        {
            return new[] { c.PlayerA, c.PlayerB }
                .SelectMany(id => req.ById[id].Lanes.Where(l => l != req.ById[id].Main).Select(l => new SecondaryLaneSwap(id, l)))
                .ToList();
        }

        // Swaps the player with whoever sits on the chosen lane of the same team. False if it cannot be applied.
        public bool ApplySecondaryLaneSwap(BalancerResult res, SecondaryLaneSwap sw)
        {
            var team = res.Teams.FirstOrDefault(t => t.Slots.Any(s => s.PlayerId == sw.PlayerId));
            if (team == null) return false;
            var i = team.Slots.FindIndex(s => s.PlayerId == sw.PlayerId);
            var j = team.Slots.FindIndex(s => s.Lane == sw.Lane);
            if (i < 0 || j < 0) return false;
            (team.Slots[i].PlayerId, team.Slots[j].PlayerId) = (team.Slots[j].PlayerId, team.Slots[i].PlayerId);
            return true;
        }
    }
}
