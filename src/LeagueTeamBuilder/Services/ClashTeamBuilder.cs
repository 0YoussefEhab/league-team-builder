using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Which strength the Clash team is picked by (prototype C.rank): normal rank (60% current + 40% peak)
    // or the admin's custom tiers (players with no tier fall back to rank, like TeamBalancer.Value).
    public enum ClashRankSystem
    {
        Rank = 0,
        Tier = 1,
    }

    public class ClashRequest
    {
        // Only Normal (lanes + rank / tier) and Aram (5 strongest, rank only, no lanes) are allowed (spec section 4).
        public LeagueMode Mode { get; init; } = LeagueMode.Normal;
        public ClashRankSystem RankSystem { get; init; } = ClashRankSystem.Rank;
        public int TierCount { get; init; }
        // The pool: members and guests. Guests are always free; the caller decides who is busy.
        public IReadOnlyList<BalancerPlayer> Players { get; init; } = Array.Empty<BalancerPlayer>();
        public IReadOnlyCollection<int> Busy { get; init; } = Array.Empty<int>();
    }

    public class ClashSlot
    {
        public LeagueLane? Lane { get; set; }      // null for ARAM
        public int? PlayerId { get; set; }
        // "Edit me": nobody could cover this lane cleanly, the second option was put there (prototype manual).
        public bool Manual { get; set; }
    }

    // Kind: "" (plain), "ok" or "warn" (prototype log classes c).
    public record ClashLogLine(string Kind, string Text);

    // A slot where the busy player's lane could not be covered cleanly (prototype nofit entry).
    public record ClashNoFit(int BusyPlayerId, int ReplacementPlayerId, LeagueLane Lane);

    public enum ClashFit
    {
        Main, Second, Third, OffLane,
    }

    public class ClashResult
    {
        public LeagueMode Mode { get; set; }
        public ClashRankSystem RankSystem { get; set; }
        public List<ClashSlot> Slots { get; set; } = new();
        public List<ClashLogLine> Log { get; set; } = new();
        public List<ClashNoFit> NoFit { get; set; } = new();
        // True when there was nobody to build a team from (prototype: C.res = null).
        public bool IsEmpty { get; set; }
        // Sum of the picked players' values (prototype "strength").
        public double Strength { get; set; }

        // Stored on the match as the builder log (spec section 8, point 4).
        public string LogText() => string.Join("\n", Log.Select(l => l.Text));
    }

    // Server-side port of the prototype's Clash page code (docs/league-team-builder-preview.html: cval, cSorted,
    // gcost, assign, buildClash). Pure logic, no database, no RNG: ties keep the order of the player list, so the
    // same input always gives the same team (spec R1). Stateless, so it is registered as a singleton.
    //
    // Normal: the best 5 by strength that the lane rules allow, then each busy player is replaced by the next best
    // player who fits, with chain swaps so everybody stays on a main or second lane when possible.
    // ARAM (spec section 4): the 5 strongest players who are not busy, rank only, no lanes, no lane messages.
    public class ClashTeamBuilder
    {
        // Prototype FITC, now by lane level (Step 8f): level 5 = 0, level 4 = 2 (the old main / second), level 3 = 6
        // (the old third-or-later), level 1-2 = last resort (30, only at lvl 2).
        private static readonly int[] FitCost = { 0, 2, 6 };
        private const int Infinity = int.MaxValue;
        private const int TeamSize = 5;
        private static readonly IReadOnlyDictionary<int, LeagueLane> NoLanes = new Dictionary<int, LeagueLane>();

        // Prototype cval: tier value + 1% of the rank blend as a tie-break, or just the rank blend.
        public static double Value(BalancerPlayer p, ClashRankSystem system, int tierCount) =>
            system == ClashRankSystem.Tier
                ? TeamBalancer.Value(p, LeagueBalance.Tier, tierCount) + TeamBalancer.Blend(p) * .01
                : TeamBalancer.Blend(p);

        // Prototype gcost. lvl 0 = good lanes only (level 4-5), 1 = also an acceptable lane (3), 2 = anything (level 1-2 too).
        public static int LaneCost(BalancerPlayer p, LeagueLane lane, int lvl)
        {
            var level = p.LevelOf(lane);
            if (LeagueLaneLevels.IsLastResort(level)) return lvl >= 2 ? 30 : Infinity;
            if (level >= 5) return FitCost[0];
            if (level == 4) return FitCost[1];
            return lvl >= 1 ? FitCost[2] : Infinity;
        }

        // The "Main / Good lane / OK lane / Last resort" tag shown on a slot (prototype vClash, levels since 8f).
        // Main = the player's main lane (the highest level); the others by their level: 4-5 good, 3 OK, 1-2 last resort.
        public static ClashFit FitFor(BalancerPlayer p, LeagueLane lane)
        {
            if (p.Main == lane) return ClashFit.Main;
            var level = p.LevelOf(lane);
            return level >= 4 ? ClashFit.Second : level == 3 ? ClashFit.Third : ClashFit.OffLane;
        }

        // Strength of the players currently in the slots (after a manual swap or replacement).
        public static double StrengthOf(IEnumerable<ClashSlot> slots, ClashRequest req)
        {
            var byId = req.Players.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
            return slots.Sum(s => s.PlayerId is int id && byId.TryGetValue(id, out var p) ? Value(p, req.RankSystem, req.TierCount) : 0);
        }

        public ClashResult Build(ClashRequest req) =>
            req.Mode == LeagueMode.Aram ? BuildAram(req) : BuildNormal(req);

        // ---------------------------------------------------------------- Normal

        private ClashResult BuildNormal(ClashRequest req)
        {
            var result = new ClashResult { Mode = LeagueMode.Normal, RankSystem = req.RankSystem };
            var busy = new HashSet<int>(req.Busy);
            var sorted = Sorted(req, new HashSet<int>());
            var team = new List<BalancerPlayer>();
            var mx = 0;

            // Level 0 first (main or second lane only), then 1, then 2, always walking the strongest first.
            for (var l = 0; l < 3 && team.Count < TeamSize; l++)
            {
                foreach (var p in sorted)
                {
                    if (team.Count >= TeamSize) break;
                    if (team.Contains(p)) continue;
                    var trial = new List<BalancerPlayer>(team) { p };
                    if (Assign(trial, NoLanes, NoLanes, l) != null) { team.Add(p); mx = l; }
                }
            }

            if (team.Count == 0)
            {
                result.IsEmpty = true;
                result.Slots = OpenLaneSlots();
                return result;
            }

            var first = Assign(team, NoLanes, NoLanes, mx) ?? Assign(team, NoLanes, NoLanes, 2)!;
            var lanes = new Dictionary<int, LeagueLane>();
            for (var i = 0; i < team.Count; i++) lanes[team[i].Id] = first.Value.Lanes[i];

            var manual = new HashSet<int>();
            var log = result.Log;
            string Names() => string.Join(", ", Enumerable.Range(0, TeamSize)
                .Select(k => team.FirstOrDefault(p => lanes.TryGetValue(p.Id, out var ll) && (int)ll == k))
                .Where(p => p != null)
                .Select(p => Label(lanes[p!.Id]) + " " + p!.Name));
            log.Add(new ClashLogLine("", "Best possible lineup: " + Names() + "."));
            if (mx > 0)
                log.Add(new ClashLogLine("warn", "Not enough players for a perfect lane fit, so some start on a third or unlisted lane."));

            foreach (var b in team.Where(p => busy.Contains(p.Id)).ToList())
            {
                var lane = lanes[b.Id];
                var rem = team.Where(p => p != b).ToList();
                var prev = new Dictionary<int, LeagueLane>(lanes);
                prev.Remove(b.Id);
                lanes.Remove(b.Id);
                var pin = manual.Where(lanes.ContainsKey).ToDictionary(id => id, id => lanes[id]);
                var excluded = new HashSet<int>(busy);
                foreach (var r in rem) excluded.Add(r.Id);
                var cands = Sorted(req, excluded);
                var ok = false;
                log.Add(new ClashLogLine("", $"{b.Name} ({Label(lane)}) is busy."));

                for (var i = 0; i < cands.Count && !ok; i++)
                {
                    var c = cands[i];
                    var t = new List<BalancerPlayer>(rem) { c };
                    var r = Assign(t, prev, pin, 0);
                    if (r == null)
                    {
                        log.Add(new ClashLogLine("", $"Next best {c.Name} can't be fitted without pushing someone off their main or second lane."));
                        continue;
                    }
                    var nl = new Dictionary<int, LeagueLane>();
                    for (var j = 0; j < t.Count; j++) nl[t[j].Id] = r.Value.Lanes[j];
                    var moved = rem.Where(p => nl[p.Id] != prev[p.Id]).ToList();
                    foreach (var p in moved)
                        log.Add(new ClashLogLine("ok", $"{p.Name} moves {Label(prev[p.Id])} → {Label(nl[p.Id])} ({(p.Main == nl[p.Id] ? "main" : "second")} lane)."));
                    log.Add(new ClashLogLine("ok", moved.Count == 0 && nl[c.Id] == lane
                        ? $"{c.Name} takes over {Label(lane)} directly."
                        : $"{c.Name} joins at {Label(nl[c.Id])}."));
                    team = t;
                    lanes = nl;
                    ok = true;
                }

                if (!ok)
                {
                    // Nobody fits cleanly: the second option (or the only one) is put there and flagged for a manual edit.
                    var c = cands.Count > 1 ? cands[1] : cands.Count > 0 ? cands[0] : null;
                    if (c != null)
                    {
                        team = new List<BalancerPlayer>(rem) { c };
                        lanes[c.Id] = lane;
                        manual.Add(c.Id);
                        result.NoFit.Add(new ClashNoFit(b.Id, c.Id, lane));
                        log.Add(new ClashLogLine("warn", $"Nobody left can cover {Label(lane)} cleanly. {c.Name} (second option) takes {Label(lane)}. Edit this slot yourself."));
                    }
                    else
                    {
                        team = rem;
                        log.Add(new ClashLogLine("warn", $"No players left for {Label(lane)}. The slot stays open."));
                    }
                }
            }

            if (busy.Count == 0)
                log.Add(new ClashLogLine("", "Nobody is marked busy, so this is the strongest lineup the lane rules allow."));

            result.Slots = Enumerable.Range(0, TeamSize).Select(k =>
            {
                var lane = (LeagueLane)k;
                var p = team.FirstOrDefault(q => lanes.TryGetValue(q.Id, out var ll) && ll == lane);
                return new ClashSlot { Lane = lane, PlayerId = p?.Id, Manual = p != null && manual.Contains(p.Id) };
            }).ToList();
            result.Strength = StrengthOf(result.Slots, req);
            return result;
        }

        // Prototype assign: the cheapest way to give each player of the team a different lane (branch and bound).
        // prev = lanes before a replacement (moving costs 4 more), pin = lanes that must not change (manual slots).
        // Null when no assignment exists at this level. Ties keep the first one found.
        private static (int Cost, LeagueLane[] Lanes)? Assign(
            IReadOnlyList<BalancerPlayer> team,
            IReadOnlyDictionary<int, LeagueLane> prev,
            IReadOnlyDictionary<int, LeagueLane> pin,
            int lvl)
        {
            var n = team.Count;
            (int Cost, LeagueLane[] Lanes)? best = null;
            var used = new bool[TeamSize];
            var cur = new LeagueLane[n];

            void Rec(int i, int c)
            {
                if (best != null && c >= best.Value.Cost) return;
                if (i == n) { best = (c, (LeagueLane[])cur.Clone()); return; }
                var p = team[i];
                for (var k = 0; k < TeamSize; k++)
                {
                    if (used[k]) continue;
                    var lane = (LeagueLane)k;
                    var pinned = pin.TryGetValue(p.Id, out var pl);
                    if (pinned && pl != lane) continue;
                    var g = pinned ? 0 : LaneCost(p, lane, lvl);
                    if (g == Infinity) continue;
                    if (prev.TryGetValue(p.Id, out var pv) && pv != lane) g += 4;
                    used[k] = true;
                    cur[i] = lane;
                    Rec(i + 1, c + g);
                    used[k] = false;
                }
            }

            Rec(0, 0);
            return best;
        }

        // ---------------------------------------------------------------- ARAM

        private ClashResult BuildAram(ClashRequest req)
        {
            var result = new ClashResult { Mode = LeagueMode.Aram, RankSystem = req.RankSystem };
            var busy = new HashSet<int>(req.Busy);
            var all = Sorted(req, new HashSet<int>());
            if (all.Count == 0)
            {
                result.IsEmpty = true;
                result.Slots = Enumerable.Range(0, TeamSize).Select(_ => new ClashSlot()).ToList();
                return result;
            }

            var ideal = all.Take(TeamSize).ToList();
            var picked = Sorted(req, busy).Take(TeamSize).ToList();
            var system = req.RankSystem == ClashRankSystem.Tier ? "custom tier" : "rank";
            result.Log.Add(new ClashLogLine("", $"Strongest five by {system}: " + string.Join(", ", ideal.Select(p => p.Name)) + "."));

            // Busy players of the ideal five, in strength order, are replaced by the next best who are free.
            var out_ = ideal.Where(p => busy.Contains(p.Id)).ToList();
            var in_ = picked.Where(p => !ideal.Contains(p)).ToList();
            for (var i = 0; i < out_.Count; i++)
            {
                result.Log.Add(new ClashLogLine("", $"{out_[i].Name} is busy."));
                result.Log.Add(i < in_.Count
                    ? new ClashLogLine("ok", $"{in_[i].Name} steps in.")
                    : new ClashLogLine("warn", "Nobody left to take that place. The slot stays open."));
            }
            if (picked.Count < TeamSize && picked.Count > 0 && out_.Count == 0)
                result.Log.Add(new ClashLogLine("warn", $"Only {picked.Count} players are available, so some slots stay open."));
            if (busy.Count == 0)
                result.Log.Add(new ClashLogLine("", "Nobody is marked busy, so this is the strongest team."));

            result.Slots = Enumerable.Range(0, TeamSize)
                .Select(i => new ClashSlot { PlayerId = i < picked.Count ? picked[i].Id : null })
                .ToList();
            result.Strength = StrengthOf(result.Slots, req);
            return result;
        }

        // ---------------------------------------------------------------- helpers

        // Prototype cSorted: everybody not excluded, strongest first. LINQ OrderBy is stable, so equal strengths
        // keep the order of the list (deterministic).
        private static List<BalancerPlayer> Sorted(ClashRequest req, HashSet<int> excluded) =>
            req.Players.GroupBy(p => p.Id).Select(g => g.First())
                .Where(p => !excluded.Contains(p.Id))
                .OrderByDescending(p => Value(p, req.RankSystem, req.TierCount))
                .ToList();

        private static List<ClashSlot> OpenLaneSlots() =>
            Enumerable.Range(0, TeamSize).Select(k => new ClashSlot { Lane = (LeagueLane)k }).ToList();

        // "Top", "Jungle", "Mid", "ADC", "Support" (the prototype's LANES).
        private static string Label(LeagueLane lane) => lane == LeagueLane.Adc ? "ADC" : lane.ToString();
    }
}
