namespace LeagueTeamBuilder.Models
{
    // Per-mode settings (docs/LEAGUE_BUILD_SPEC.md sections 3 and 11). One row per LeagueMode,
    // created on first use with the defaults in LeagueModeRules (no seeding).
    public class LeagueModeSettings
    {
        public LeagueMode Mode { get; set; }
        public int PlayerCount { get; set; }
        // Arena only.
        public int? TeamCount { get; set; }
        public string? MapImageUrl { get; set; }
        // Width / height of the map image, read on the server when the image is uploaded.
        public double MapAspect { get; set; } = 16d / 9d;

        public List<LeagueMapSpot> Spots { get; set; } = new();
    }

    // Where a player circle sits on the map. X and Y are percentages (0-100) of the image.
    // Normal: Slot = lane index (Top 0 .. Support 4). ARAM: Slot = 0..ceil(players / 2) - 1.
    // Team is 0 or 1. Arena has no spots.
    public class LeagueMapSpot
    {
        public LeagueMode Mode { get; set; }
        public LeagueModeSettings? Settings { get; set; }
        public int Team { get; set; }
        public int Slot { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }
}
