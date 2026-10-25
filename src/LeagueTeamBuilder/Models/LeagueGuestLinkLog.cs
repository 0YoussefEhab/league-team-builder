using LeagueTeamBuilder.Services;

namespace LeagueTeamBuilder.Models
{
    // Step 4 (admin fixes): one row per guest link, written in the same transaction as the link, so a wrong
    // link can be undone. A merge deletes the guest row, so everything needed to rebuild it is kept in DataJson.
    // No foreign keys on purpose: the user or the profile may be deleted later, and the log must survive that.
    public class LeagueGuestLinkLog
    {
        public int Id { get; set; }
        public DateTime LinkedUtc { get; set; } = DateTime.UtcNow;

        // Audit-style label of the officer who linked (email or user name), max 200.
        public string LinkedByLabel { get; set; } = string.Empty;

        // The user the guest was linked to.
        public string UserId { get; set; } = string.Empty;
        public string UserLabel { get; set; } = string.Empty;

        public LeagueLinkKind Kind { get; set; }

        // The guest's id before the link. For a merge this row no longer exists; an undo creates a NEW row.
        public int GuestId { get; set; }
        // The profile the person ended up with (the guest row itself for BecomeProfile, the member's own for Merge).
        public int ProfileId { get; set; }
        public string GuestName { get; set; } = string.Empty;

        // JSON of LeagueGuestLinkData.
        public string DataJson { get; set; } = "{}";

        // Null = active. Set by Undo link; the row is kept as history.
        public DateTime? UndoneUtc { get; set; }
        public string? UndoneByLabel { get; set; }
    }

    // Everything an undo needs. Plain data so it serialises with System.Text.Json.
    public class LeagueGuestLinkData
    {
        // The guest as it was (rebuilt on a Merge undo; on a Become undo it restores the changed fields).
        public LeagueGuestSnapshot Guest { get; set; } = new();

        // Merge only: exactly what moved, so an undo moves back only these.
        public List<int> MatchPlayerIds { get; set; } = new();
        public List<int> MvpMatchIds { get; set; } = new();
        public List<int> TeamMvpMatchIds { get; set; } = new();
        public List<int> StatsMvpMatchIds { get; set; } = new();
        public List<int> StatsTeamMvpMatchIds { get; set; } = new();
        // Profiles whose "does better with" pointed at the guest and were re-pointed to the member.
        public List<int> DoBetterWithProfileIds { get; set; } = new();
        // The member's own "does better with" pointed at the guest and was cleared.
        public bool MemberDoBetterWithCleared { get; set; }

        // BecomeProfile only: values the link overwrote (null = not overwritten).
        public bool NameCopied { get; set; }
        public bool PictureCopied { get; set; }
    }

    public class LeagueGuestSnapshot
    {
        public string InGameName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public LeagueAvatarSource AvatarSource { get; set; }
        public LeagueRank CurTier { get; set; }
        public int? CurDiv { get; set; }
        public LeagueRank PeakTier { get; set; }
        public int? PeakDiv { get; set; }
        public LeagueLane MainLane { get; set; }
        public int? CustomTierId { get; set; }
        public int? DoBetterWithId { get; set; }
        public string? RiotId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public List<LeagueGuestLaneSnapshot> Lanes { get; set; } = new();
    }

    public class LeagueGuestLaneSnapshot
    {
        public LeagueLane Lane { get; set; }
        public int Priority { get; set; }
        public int Level { get; set; }
    }
}
