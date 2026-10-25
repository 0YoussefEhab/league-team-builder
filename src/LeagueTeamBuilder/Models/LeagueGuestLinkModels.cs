namespace LeagueTeamBuilder.Models
{
    // One row in the "Link to user" dialog (docs/LEAGUE_BUILD_SPEC.md section 6).
    public class LeagueLinkableUser
    {
        public string UserId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? Email { get; set; }

        // Set when the user already has a league profile: linking then merges the guest into it.
        public int? ProfileId { get; set; }
        public string? ProfileName { get; set; }
        public bool HasProfile => ProfileId.HasValue;
    }

    public class LeagueLinkRequest
    {
        public int GuestId { get; set; }
        public string UserId { get; set; } = "";

        // Only used when the guest becomes the user's profile (no merge): copy once from the main account.
        public bool UseUserName { get; set; }
        public bool UseUserPicture { get; set; }
    }

    public class LeagueLinkResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
        public bool Merged { get; set; }
        // The profile the person ends up with (the guest row when not merged, the user's own when merged).
        public int? ProfileId { get; set; }
        public int MatchEntriesMoved { get; set; }
        public int Notified { get; set; }
        // The link log row written for this link (0 on a failure). Used by Undo link.
        public int LogId { get; set; }
    }

    // Result of Undo link (admin fixes Step 4).
    public class LeagueUndoResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
        // The guest after the undo: the same row for a Become link, a NEW row for a Merge. "Change user" opens the Link screen for it.
        public int GuestId { get; set; }
        public LeagueTeamBuilder.Services.LeagueLinkKind Kind { get; set; }
        public int MatchEntriesMoved { get; set; }
    }

    // Admin fixes Step 4e: one row of League Settings > Link log (newest first).
    public class LeagueLinkLogRow
    {
        public int LogId { get; set; }
        public DateTime LinkedUtc { get; set; }
        public string LinkedBy { get; set; } = "";
        public string GuestName { get; set; } = "";
        public string UserLabel { get; set; } = "";
        public int ProfileId { get; set; }
        public LeagueTeamBuilder.Services.LeagueLinkKind Kind { get; set; }
        public bool Active => UndoneUtc is null;
        public DateTime? UndoneUtc { get; set; }
        public string? UndoneBy { get; set; }
    }

    public class LeagueLinkedGuestsPageModel
    {
        public List<LeagueLinkLogRow> Rows { get; set; } = new();
        public int ActiveCount { get; set; }
    }

    // The confirm screen of the manual Unlink (admin fixes Step 4d): a member profile that came from a guest,
    // for a link with no log entry.
    public class LeagueUnlinkPreview
    {
        public int ProfileId { get; set; }
        public string Name { get; set; } = "";
        public string UserLabel { get; set; } = "";
        public string? Email { get; set; }
        public int MatchEntries { get; set; }
        // Match entries made while the profile was still a guest (the sign that it came from a guest).
        public int GuestEntries { get; set; }
        public int Champions { get; set; }
        public bool HasPicture { get; set; }
        // Why Unlink is not offered (null = allowed).
        public string? BlockReason { get; set; }
        public bool CanUnlink => BlockReason is null;
    }

    // What the confirm step shows (Step 7b). Built from the same rules as the link itself.
    public class LeagueLinkPreview
    {
        public int GuestId { get; set; }
        public string GuestName { get; set; } = "";
        public LeagueLinkableUser User { get; set; } = new();
        public bool Merge => User.HasProfile;
        // Merge only: how many match entries of the guest move to the user's profile.
        public int MatchEntries { get; set; }
        // Not a merge only: the name the profile gets when "use the user's name" is ticked.
        public string? MemberName { get; set; }
        // Not a merge only: false when the user has no picture (or its address is too long to copy).
        public bool CanCopyPicture { get; set; }
    }

    // Views/LeagueSettings/LinkGuest.cshtml: the search list (Users) or the confirm step (Preview).
    public class LeagueLinkPageModel
    {
        public int GuestId { get; set; }
        public string GuestName { get; set; } = "";
        public string? Query { get; set; }
        public List<LeagueLinkableUser> Users { get; set; } = new();
        public LeagueLinkPreview? Preview { get; set; }
    }
}
