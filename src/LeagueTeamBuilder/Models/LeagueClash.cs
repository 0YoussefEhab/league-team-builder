namespace LeagueTeamBuilder.Models
{
    // M4: a scheduled Clash (docs/LEAGUE_BUILD_SPEC.md section 4). One event owns up to ~4 LeagueMatch rows
    // (the games of the series, LeagueMatch.ClashEventId + GameNumber).
    public class LeagueClashEvent
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime StartsAtUtc { get; set; }
        // Only Normal or Aram are allowed (service rule); Arena has no Clash. Stored as int like the rest.
        public LeagueMode Mode { get; set; }
        public LeagueClashStatus Status { get; set; }

        // Set when game 1 is started: answers lock from here on.
        public DateTime? StartedUtc { get; set; }
        // Set by End clash (Done) and by Cancel (Cancelled).
        public DateTime? EndedUtc { get; set; }
        public DateTime? CancelledUtc { get; set; }

        public DateTime CreatedUtc { get; set; }
        // No FK: deleting a user never touches league history.
        public string? CreatedByUserId { get; set; }

        public List<LeagueClashResponse> Responses { get; set; } = new();
        public List<LeagueMatch> Matches { get; set; } = new();
        // Step 11b: the teams playing this clash (several teams can play the same clash). Rows are created by Step 11c.
        public List<LeagueClashTeam> Teams { get; set; } = new();
    }

    // Step 11b: one team of a clash ("Team one", then the ones the owner adds). The lineup is not stored here: a lineup lives in the
    // browser until it is saved as a game (LeagueMatch.ClashTeamId points back here). Name is 1-30 characters and unique per clash.
    public class LeagueClashTeam
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public LeagueClashEvent? Event { get; set; }
        public string Name { get; set; } = "";
        // 0 for the first team; the order the team cards are shown in.
        public int SortOrder { get; set; }
        public DateTime CreatedUtc { get; set; }

        public List<LeagueMatch> Matches { get; set; } = new();
    }

    // A free / busy answer. Unique per (event, profile). No row = free. Members answer for themselves (6c); since 8i a moderator saves a guest's answer.
    public class LeagueClashResponse
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public LeagueClashEvent? Event { get; set; }
        public int ProfileId { get; set; }
        public LeagueProfile? Profile { get; set; }
        public LeagueClashAnswer Status { get; set; }
        public DateTime RespondedUtc { get; set; }
    }
}
