namespace LeagueTeamBuilder.Models
{
    // League Team Builder profile (docs/LEAGUE_BUILD_SPEC.md, section 3). Completely separate from
    // the main profile: nothing in League writes to ApplicationUser. A guest is a row with
    // IsGuest = true and UserId = null; linking a guest to a real user flips both (spec section 6).
    public class LeagueProfile
    {
        public int Id { get; set; }

        // Null for guests. One user has at most one league profile (unique index).
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        public bool IsGuest { get; set; }

        public string InGameName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public LeagueAvatarSource AvatarSource { get; set; } = LeagueAvatarSource.None;

        public LeagueRank CurTier { get; set; } = LeagueRank.Iron;
        public int? CurDiv { get; set; }
        public LeagueRank PeakTier { get; set; } = LeagueRank.Iron;
        public int? PeakDiv { get; set; }

        public LeagueLane MainLane { get; set; } = LeagueLane.Mid;

        // "Does better with" another profile; cleared if that profile is deleted or merged away.
        public int? DoBetterWithId { get; set; }
        public LeagueProfile? DoBetterWith { get; set; }

        // Admin-defined custom tier (overrides rank when set). Deleting a tier must reassign its
        // players first (service logic); the FK is Restrict so a bug fails loudly.
        public int? CustomTierId { get; set; }
        public LeagueCustomTier? CustomTier { get; set; }

        // Optional, unused in v1 (spec sections 14 and 15).
        public string? RiotId { get; set; }
        public string? RiotPuuid { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public List<LeagueProfileLane> Lanes { get; set; } = new();
        public List<LeagueProfileChamp> Champs { get; set; } = new();
    }

    // Priority 1 = main lane, 2 = second, and so on (1..5).
    // Level (Step 8, 1..5) = how much the player likes / plays the lane; 5 is best. Pyramid: 4-5 Mains,
    // 3 Kinda good, 1-2 Basics (LeagueLaneLevels). A lane with no row counts as level 1.
    // Priority stays until Step 8f so nothing that reads it breaks; until then it is written as 6 - Priority.
    public class LeagueProfileLane
    {
        public int ProfileId { get; set; }
        public LeagueProfile? Profile { get; set; }
        public LeagueLane Lane { get; set; }
        public int Priority { get; set; }
        public int Level { get; set; } = 1;
    }

    public class LeagueProfileChamp
    {
        public int ProfileId { get; set; }
        public LeagueProfile? Profile { get; set; }
        public LeagueLane Lane { get; set; }
        public int ChampionId { get; set; }
        public LeagueChampion? Champion { get; set; }
    }

    // Managed catalog (no free text). Referenced champions are disabled instead of deleted.
    public class LeagueChampion
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? IconUrl { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class LeagueCustomTier
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }
}
