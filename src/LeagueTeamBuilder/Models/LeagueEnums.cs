namespace LeagueTeamBuilder.Models
{
    // Order matches the prototype's RANKS array (docs/league-team-builder-preview.html) and the
    // placeholder badges in wwwroot/img/ranks/default/{name-lowercase}.svg (RankBadgeCatalog). Stored as int; do not reorder.
    // Master and above have no division (LeagueProfile.CurDiv/PeakDiv stay null).
    public enum LeagueRank
    {
        Iron = 0, Bronze = 1, Silver = 2, Gold = 3, Platinum = 4,
        Emerald = 5, Diamond = 6, Master = 7, Grandmaster = 8, Challenger = 9,
    }

    // Matches the prototype's LANES array. Stored as int; do not reorder.
    public enum LeagueLane
    {
        Top = 0, Jungle = 1, Mid = 2, Adc = 3, Support = 4,
    }

    // Where a league profile's picture came from. Copied = URL string read once from
    // ApplicationUser.AvatarUrl; the league never deletes or alters that file (spec section 9).
    public enum LeagueAvatarSource
    {
        None = 0, Copied = 1, Uploaded = 2,
    }

    // Game modes with their own settings (docs/LEAGUE_BUILD_SPEC.md sections 3 and 11). Stored as int; do not reorder.
    public enum LeagueMode
    {
        Normal = 0, Aram = 1, Arena = 2,
    }

    // M3: matches. Stored as int; do not reorder.
    public enum LeagueMatchKind
    {
        Custom = 0, Clash = 1,
    }

    // Lifecycle (spec section 4/8): submitted -> live -> done.
    public enum LeagueMatchStatus
    {
        Submitted = 0, Live = 1, Done = 2,
    }

    // M4: Clash events (spec section 4). Stored as int; do not reorder.
    // Open -> InProgress (game 1 started, answers locked) -> Done (End clash). Cancelled = called off by an admin.
    public enum LeagueClashStatus
    {
        Open = 0, InProgress = 1, Done = 2, Cancelled = 3,
    }

    // A member's answer on the response page. No row = no answer = treated as free. Stored as int.
    public enum LeagueClashAnswer
    {
        Free = 0, Busy = 1,
    }
}
