using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Step 8c: small pure text helpers shared by the League pages that show people (Overview now; Leaderboard and
    // Player pages later). Same behaviour as the helpers in docs/league-overview-mockup.html (hue, initial, rank text).
    public static class LeagueViewText
    {
        private static readonly string[] Roman = { "", "I", "II", "III", "IV" };

        // A steady colour for a person without a picture (mockup: (id * 47 + 20) % 360).
        public static int Hue(int id) => ((id * 47 + 20) % 360 + 360) % 360;

        // The first letter of the name, or "?" for an empty name.
        public static string Initial(string? name)
        {
            var t = (name ?? "").Trim();
            return t.Length == 0 ? "?" : char.ToUpperInvariant(t[0]).ToString();
        }

        // "Diamond II", "Master" (Master and above have no division).
        public static string RankText(LeagueRank rank, int? div) =>
            LeagueRankRules.HasDivision(rank) && div is >= 1 and <= 4 ? $"{rank} {Roman[div.Value]}" : rank.ToString();

        public static string LaneName(LeagueLane lane) => lane switch
        {
            LeagueLane.Top => "Top",
            LeagueLane.Jungle => "Jungle",
            LeagueLane.Mid => "Mid",
            LeagueLane.Adc => "ADC",
            LeagueLane.Support => "Support",
            _ => lane.ToString(),
        };

        // Short lane label for small pills (mockup LANE_SHORT): TOP, JNG, MID, ADC, SUP.
        public static string LaneShort(LeagueLane lane) => lane switch
        {
            LeagueLane.Top => "TOP",
            LeagueLane.Jungle => "JNG",
            LeagueLane.Mid => "MID",
            LeagueLane.Adc => "ADC",
            LeagueLane.Support => "SUP",
            _ => lane.ToString().ToUpperInvariant(),
        };

        // "1 game", "4 games".
        public static string Plural(int n, string one, string? many = null) => n == 1 ? $"1 {one}" : $"{n} {many ?? one + "s"}";

        // "26 Sep" (UTC date, invariant culture).
        public static string ShortDate(DateTime utc) => utc.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
    }
}
