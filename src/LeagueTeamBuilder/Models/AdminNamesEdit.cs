namespace LeagueTeamBuilder.Models
{
    // Step 3 of PLAN-admin-fixes.md: an admin changes names.
    //  - A user: UserId set. DisplayName is the account's. If IncludeLeague is true and the user has a
    //    League profile, InGameName and RiotId (Name#TAG) are changed on that profile too.
    //  - A guest: UserId null, ProfileId set. Only InGameName and RiotId exist.
    public class AdminNamesEdit
    {
        public string? UserId { get; set; }
        public string? DisplayName { get; set; }
        public bool IncludeLeague { get; set; }
        public int? ProfileId { get; set; }
        public string? InGameName { get; set; }
        public string? RiotId { get; set; }
    }

    public class AdminEditNamesPageModel
    {
        public AdminNamesEdit Form { get; set; } = new();
        public string Email { get; set; } = "";
        public bool HasLeagueProfile { get; set; }
        public Dictionary<string, string> Errors { get; set; } = new();
    }

    public class LeagueGuestNamesPageModel
    {
        public AdminNamesEdit Form { get; set; } = new();
        public Dictionary<string, string> Errors { get; set; } = new();
    }
}
