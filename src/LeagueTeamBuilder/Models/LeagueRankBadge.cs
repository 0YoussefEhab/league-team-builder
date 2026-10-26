namespace LeagueTeamBuilder.Models
{
    // Phase B1: the picture shown for one rank tier (Iron .. Challenger). One row per tier at most; a tier with no row, or
    // with a null ImageUrl, shows the built-in placeholder (see RankBadgeCatalog). The pictures are uploaded by an admin
    // and live under /uploads/rank-badges/. No Riot art ships with the project.
    public class LeagueRankBadge
    {
        // Primary key. Stored as the enum's int; never generated.
        public LeagueRank Tier { get; set; }
        // "/uploads/rank-badges/<random>.png" or null (= placeholder).
        public string? ImageUrl { get; set; }
    }
}
