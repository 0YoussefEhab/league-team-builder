using LeagueTeamBuilder.Services;

namespace LeagueTeamBuilder.Models
{
    // Step 5d: the "My league stats" block on the league profile page. Built from the player rows of FINISHED
    // matches that are still linked to the profile (LeagueMatchPlayer.ProfileId). Spec section 14: a stat is shown
    // only when it exists, and averages use only the matches that have that stat ("based on N matches").

    public record LeagueStatsChampionUse(string Name, string? IconUrl, int Games, int Wins, int? WinRate);

    public record LeagueStatsLaneUse(string Label, int Games, int Wins, int? WinRate);

    // MVP redesign Step 5d: MVPs, games and win record of one person in ONE group (custom draft, Clash, or custom ARAM/Arena).
    public record LeagueBucketStat(int Mvps = 0, int Games = 0, int Wins = 0, int Losses = 0)
    {
        public int Decided => Wins + Losses;
        // Whole percent, null when no game of this group has a recorded winner.
        public int? WinRate => LeagueLeaderboardRules.WinRatePercent(Wins, Losses);
    }

    // The three groups are never added together (STEP-0-MVP-REDESIGN.md, "Counts").
    public class LeagueBucketStats
    {
        public LeagueBucketStat Draft { get; set; } = new();
        public LeagueBucketStat Clash { get; set; } = new();
        public LeagueBucketStat Aram { get; set; } = new();
    }

    public class LeaguePlayerStats
    {
        // MVP redesign Step 5d: MVPs and win records per group. Filled by LeagueHistoryService.ComputePlayerStats.
        // Games / Decided / Wins / Losses / WinRate below still cover ALL finished matches.
        public LeagueBucketStats Buckets { get; set; } = new();

        // Finished matches this profile played.
        public int Games { get; set; }
        // Of those, matches with a recorded winner. Wins + Losses = Decided.
        public int Decided { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        // Percent, null when no match has a recorded winner.
        public int? WinRate { get; set; }

        // Matches where K, D and A were all entered; the averages and the ratio come from these only.
        public int KdaMatches { get; set; }
        public double? AvgKills { get; set; }
        public double? AvgDeaths { get; set; }
        public double? AvgAssists { get; set; }
        // (K + A) / D over those matches, "Perfect" when there were no deaths, null when there is no K/D/A.
        public string? KdaRatio { get; set; }

        // Champions (most played first, top 5) and how many matches had a champion entered at all.
        public int ChampionMatches { get; set; }
        public List<LeagueStatsChampionUse> Champions { get; set; } = new();

        // Lane actually played (builder lane when none was entered); ARAM and Arena have no lane.
        public int LaneMatches { get; set; }
        public List<LeagueStatsLaneUse> Lanes { get; set; } = new();

        // Newest first, at most 10: "W", "L", or "-" when the match has no recorded winner.
        public List<string> Recent { get; set; } = new();
    }

    public static class LeagueStatsFormat
    {
        public static string One(double? v) => v is double d
            ? d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-";

        public static string Matches(int n) => n == 1 ? "1 match" : n + " matches";
    }
}
