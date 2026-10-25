using System.Globalization;
using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Results/export step 12f: the day list and the 70% rule of the "Export matches" dialog on History. Pure (no EF, no clock),
    // covered by LeagueTeamBuilder.Tests/LeagueExportDayRulesTests.cs. Decisions: PLAN-league.md, "Step 12f - decisions".
    //   List = the finished CUSTOM games of one Cairo day that can be drawn (LeagueSessionImageRules.IsEligible), in play order,
    //   numbered 1..n. Tags: "Added later" (result entered more than 6 hours from the play time) and "Missing KDA".
    //   70% rule: players in EVERY ticked game / distinct players in all ticked games, both teams counted; a member by profile,
    //   a guest by name. 7 of 10 passes (>= 70%). One ticked game = 100%. The server decides, so the dialog and the pictures agree.
    //   Blocks: the ticked games in blocks of four, each keeping its number in the DAY (an unticked game 2 leaves a gap).
    public static class LeagueExportDayRules
    {
        public const double SessionOverlap = 0.7;
        public const int MaxTicked = 12;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // One picture's games and their numbers in the day (same length).
        public record ExportBlock(List<LeagueMatch> Games, List<int> Numbers);

        // ── Day key ("2026-10-17") ───────────────────────────────────────────────────────────────

        public static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd", Inv);

        public static bool TryParseDay(string? text, out DateOnly day)
        {
            day = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", Inv, DateTimeStyles.None, out day);
        }

        // ── Overlap and the 70% rule ─────────────────────────────────────────────────────────────

        // Everybody who played the game, both teams. A member is "p" + profile id, a guest "n" + trimmed lower-case name.
        public static HashSet<string> PlayerKeys(LeagueMatch m) =>
            m.Teams.SelectMany(t => t.Players).Select(LeagueSessionImageRules.PlayerKey).ToHashSet();

        // Players in every game / distinct players in all the games. No games, or nobody on them = 0.
        public static double Overlap(IEnumerable<ISet<string>> games)
        {
            var list = games.ToList();
            if (list.Count == 0) return 0;
            var all = list.SelectMany(g => g).ToHashSet();
            if (all.Count == 0) return 0;
            var everywhere = all.Count(k => list.All(g => g.Contains(k)));
            return everywhere / (double)all.Count;
        }

        // 7 of 10 passes; the small tolerance keeps 0.7 (7 / 10.0) on the passing side.
        public static bool IsOneSession(double overlap) => overlap >= SessionOverlap - 1e-9;

        public static int Percent(double overlap) => (int)Math.Round(overlap * 100, MidpointRounding.AwayFromZero);

        public static string Message(int ticked, int percent, bool oneSession)
        {
            if (ticked <= 0) return "Tick at least one match.";
            if (ticked == 1) return "1 match selected.";
            return oneSession
                ? $"{ticked} matches share {percent}% of the players. Grouped as one session."
                : $"These matches have different players ({percent}% shared).";
        }

        // "session" or "games": one game = single pictures, a passing overlap = one session picture, else single pictures.
        public static string DefaultMode(int ticked, bool oneSession) => ticked >= 2 && oneSession ? "session" : "games";

        // ── Rows ─────────────────────────────────────────────────────────────────────────────────

        // Any player of either team with kills, deaths or assists empty.
        public static bool MissingKda(LeagueMatch m) =>
            m.Teams.SelectMany(t => t.Players).Any(p => p.Kills == null || p.Deaths == null || p.Assists == null);

        // The checklist of a day: eligible games in play order, numbered 1..n.
        public static List<ExportDayRow> Rows(IEnumerable<LeagueMatch> dayMatches)
        {
            var rows = new List<ExportDayRow>();
            var n = 0;
            foreach (var m in LeagueSessionImageRules.Order(dayMatches))
            {
                n++;
                var winner = m.WinnerTeamIndex ?? 0;
                rows.Add(new ExportDayRow
                {
                    Id = m.Id,
                    Number = n,
                    ModeName = m.ModeName,
                    TimeText = LeagueDayRules.ToCairo(m.PlayedUtc).ToString("d MMM, HH:mm", Inv),
                    WinnerText = "Team " + (winner + 1) + " won",
                    AddedLater = LeagueDayRules.IsAddedLater(m.PlayedUtc, m.FinishedUtc),
                    MissingKda = MissingKda(m),
                });
            }
            return rows;
        }

        // ── Blocks ───────────────────────────────────────────────────────────────────────────────

        // games and numbers line up one to one (numbers = each game's number in the day). Fours, in the order given.
        public static List<ExportBlock> Blocks(IReadOnlyList<LeagueMatch> games, IReadOnlyList<int> numbers)
        {
            var blocks = new List<ExportBlock>();
            var size = LeagueSessionImageRules.GamesPerImage;
            for (var i = 0; i < games.Count; i += size)
            {
                var take = Math.Min(size, games.Count - i);
                blocks.Add(new ExportBlock(
                    games.Skip(i).Take(take).ToList(),
                    numbers.Skip(i).Take(take).ToList()));
            }
            return blocks;
        }
    }
}
