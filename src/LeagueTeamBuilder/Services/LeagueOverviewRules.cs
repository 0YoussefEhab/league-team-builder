using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Step 8 pure rules (no EF, no clock, no randomness), so they are unit-testable without a database.
    // Decisions: PLAN-league.md, "Step 8 - owner answers".

    public enum LeagueLaneTier { Mains = 0, KindaGood = 1, Basics = 2 }

    public record LaneLevel(LeagueLane Lane, int Level, int Priority = 0);

    // Lane levels 1..5 (5 = best). Owner decisions 2 and 3.
    public static class LeagueLaneLevels
    {
        public const int Min = 1, Max = 5;
        public static readonly LeagueLane[] AllLanes =
            { LeagueLane.Top, LeagueLane.Jungle, LeagueLane.Mid, LeagueLane.Adc, LeagueLane.Support };

        // One-time conversion of the old lane order: priority 1 = level 5 ... priority 5 = level 1.
        public static int LevelFromPriority(int priority) => Clamp(6 - priority);

        public static int Clamp(int level) => Math.Min(Max, Math.Max(Min, level));

        // Pyramid: 4-5 Mains, 3 Kinda good, 1-2 Basics.
        public static LeagueLaneTier TierOf(int level) => Clamp(level) >= 4 ? LeagueLaneTier.Mains
            : Clamp(level) == 3 ? LeagueLaneTier.KindaGood : LeagueLaneTier.Basics;

        // Builder / Clash reading of a level: 4-5 good lane, 3 acceptable, 1-2 last resort.
        public static bool IsGood(int level) => Clamp(level) >= 4;
        public static bool IsAcceptable(int level) => Clamp(level) == 3;
        public static bool IsLastResort(int level) => Clamp(level) <= 2;

        // Team builder cost of a lane by its level (8f): 5 = 0, 4 = 1, 3 = 4, 2 = 6, 1 = 8. A lane with no row is
        // level 1, so it costs 8 (the old OffLaneCost of 12 is gone).
        private static readonly int[] CostByLevel = { 8, 6, 4, 1, 0 };   // index = level - 1
        public static int CostForLevel(int level) => CostByLevel[Clamp(level) - 1];

        // Slider words (mockup SLIDER_LABEL).
        private static readonly string[] Words = { "Barely", "Basics", "Kinda good", "Strong", "Main" };
        public static string Word(int level) => Words[Clamp(level) - 1];

        // Always returns the five lanes (a lane with no row is level 1), highest level first inside each tier.
        public static Dictionary<LeagueLaneTier, List<LaneLevel>> Pyramid(IEnumerable<LaneLevel> rows)
        {
            var byLane = rows.GroupBy(r => r.Lane).ToDictionary(g => g.Key, g => g.First());
            var all = AllLanes.Select(l => byLane.TryGetValue(l, out var r)
                ? r with { Level = Clamp(r.Level) } : new LaneLevel(l, Min, 0)).ToList();
            var result = new Dictionary<LeagueLaneTier, List<LaneLevel>>
            {
                [LeagueLaneTier.Mains] = new(), [LeagueLaneTier.KindaGood] = new(), [LeagueLaneTier.Basics] = new(),
            };
            foreach (var r in all.OrderByDescending(r => r.Level).ThenBy(r => r.Priority == 0 ? int.MaxValue : r.Priority).ThenBy(r => (int)r.Lane))
                result[TierOf(r.Level)].Add(r);
            return result;
        }

        // One lane of a profile as it is written to LeagueProfileLanes (8f).
        public record LaneSaveRow(LeagueLane Lane, int Level, int Priority);

        // What a save writes: the rows (only lanes above level 1, highest level first, Priority 1..n) and the main lane.
        public record LaneSavePlan(IReadOnlyList<LaneSaveRow> Rows, LeagueLane Main);

        // Validates the five slider values and plans the rows (8f). levels = the posted level per lane (a lane missing
        // from the dictionary counts as level 1). oldPriority = the profile's current Priority per lane, used only to
        // break ties (a tie keeps the old order, then Top, Jungle, Mid, ADC, Support). Returns false with a message
        // when a level is outside 1-5 or no lane is above level 1.
        public static bool TryPlanSave(IReadOnlyDictionary<LeagueLane, int> levels,
            IReadOnlyDictionary<LeagueLane, int>? oldPriority, out LaneSavePlan plan, out string error)
        {
            plan = new LaneSavePlan(Array.Empty<LaneSaveRow>(), LeagueLane.Mid);
            error = "";
            foreach (var lane in AllLanes)
            {
                if (levels.TryGetValue(lane, out var v) && (v < Min || v > Max))
                {
                    error = "Each lane level must be between 1 and 5.";
                    return false;
                }
            }
            var played = AllLanes
                .Select(l => (Lane: l, Level: levels.TryGetValue(l, out var v) ? v : Min))
                .Where(x => x.Level > Min)
                .OrderByDescending(x => x.Level)
                .ThenBy(x => oldPriority != null && oldPriority.TryGetValue(x.Lane, out var pr) && pr > 0 ? pr : int.MaxValue)
                .ThenBy(x => (int)x.Lane)
                .ToList();
            if (played.Count == 0)
            {
                error = "Set at least one lane above level 1.";
                return false;
            }
            var rows = played.Select((x, i) => new LaneSaveRow(x.Lane, x.Level, i + 1)).ToList();
            plan = new LaneSavePlan(rows, rows[0].Lane);
            return true;
        }

        // The slider values of a profile for a form: a lane with no row is level 1.
        public static Dictionary<LeagueLane, int> LevelsOf(IEnumerable<LaneLevel> rows)
        {
            var d = AllLanes.ToDictionary(l => l, _ => Min);
            foreach (var r in rows) d[r.Lane] = Clamp(r.Level);
            return d;
        }

        // MainLane = the highest level; a tie goes to the lower Priority (old order), then to lane order.
        public static LeagueLane MainLane(IEnumerable<LaneLevel> rows, LeagueLane fallback = LeagueLane.Mid)
        {
            var best = rows.OrderByDescending(r => Clamp(r.Level))
                .ThenBy(r => r.Priority == 0 ? int.MaxValue : r.Priority).ThenBy(r => (int)r.Lane).FirstOrDefault();
            return best?.Lane ?? fallback;
        }
    }

    public record RankInput(int Id, string Name, LeagueRank CurTier, int? CurDiv, LeagueRank PeakTier, int? PeakDiv, int MvpCount);
    public record RankedEntry(RankInput Player, int Position);

    // Owner decisions 5 and 6 and the tie answer. Guests are included by the caller (they are just entries).
    public static class LeagueRanking
    {
        // Current rank (tier, then division), then peak rank, then more MVPs, then name A to Z.
        // Fully tied players (same current rank, peak rank and MVP count) share one position number
        // (1, 1, 3: standard competition ranking) and are listed A to Z.
        public static List<RankedEntry> Rank(IEnumerable<RankInput> players)
        {
            var sorted = players
                .OrderByDescending(p => LeagueRankRules.Value(p.CurTier, p.CurDiv))
                .ThenByDescending(p => LeagueRankRules.Value(p.PeakTier, p.PeakDiv))
                .ThenByDescending(p => p.MvpCount)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id)
                .ToList();
            var result = new List<RankedEntry>(sorted.Count);
            for (var i = 0; i < sorted.Count; i++)
            {
                var pos = i + 1;
                if (i > 0 && SameStanding(sorted[i - 1], sorted[i])) pos = result[i - 1].Position;
                result.Add(new RankedEntry(sorted[i], pos));
            }
            return result;
        }

        private static bool SameStanding(RankInput a, RankInput b) =>
            LeagueRankRules.Value(a.CurTier, a.CurDiv) == LeagueRankRules.Value(b.CurTier, b.CurDiv)
            && LeagueRankRules.Value(a.PeakTier, a.PeakDiv) == LeagueRankRules.Value(b.PeakTier, b.PeakDiv)
            && a.MvpCount == b.MvpCount;

        public static List<RankedEntry> TopThree(IEnumerable<RankInput> players) =>
            Rank(players).Take(3).ToList();

        // The #1 rank badge: everyone at position 1 (a full tie shares it).
        public static HashSet<int> RankBadgeHolders(IEnumerable<RankInput> players) =>
            Rank(players).Where(e => e.Position == 1).Select(e => e.Player.Id).ToHashSet();

        // The MVP badge: whoever has the most MVPs (ties: all of them). Nobody while the best count is 0.
        public static HashSet<int> MvpBadgeHolders(IEnumerable<RankInput> players)
        {
            var list = players.ToList();
            var max = list.Count == 0 ? 0 : list.Max(p => p.MvpCount);
            return max <= 0 ? new HashSet<int>() : list.Where(p => p.MvpCount == max).Select(p => p.Id).ToHashSet();
        }

        // Top 3 MVPs for the Overview: most MVPs first, name A to Z; players with 0 MVPs are never listed.
        public static List<RankInput> TopMvps(IEnumerable<RankInput> players) =>
            players.Where(p => p.MvpCount > 0)
                .OrderByDescending(p => p.MvpCount).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id)
                .Take(3).ToList();
    }
}
