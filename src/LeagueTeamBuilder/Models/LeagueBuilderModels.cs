namespace LeagueTeamBuilder.Models
{
    // Step 4c: data the team builder page and its JSON endpoint pass around (docs/LEAGUE_BUILD_SPEC.md R1:
    // the server generates, the browser only renders). Nothing here is stored. Lanes are always sent as the
    // LeagueLane index (0 Top .. 4 Support); the labels come once in LeagueBuilderPageModel.Lanes.

    // ---- page data (embedded as JSON in Views/League/Builder.cshtml) ----

    public class LeagueBuilderPageModel
    {
        public List<LeagueBuilderModeDto> Modes { get; set; } = new();
        public List<LeagueBuilderPlayerDto> Players { get; set; } = new();
        // Display label per LeagueLane index ("ADC" for Adc).
        public List<string> Lanes { get; set; } = new();
        // Step 4d: matches that are submitted or live (newest first). Empty unless the viewer may start, finish or delete.
        public List<LeagueMatchDto> OpenMatches { get; set; } = new();
    }

    public class LeagueBuilderModeDto
    {
        // "normal" / "aram" / "arena".
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        // Largest roster the mode accepts (Normal is capped at 10).
        public int Cap { get; set; }
        // Arena only.
        public int? Teams { get; set; }
        public string? MapUrl { get; set; }
        public double MapAspect { get; set; } = 16d / 9d;
        public List<LeagueBuilderSpotDto> Spots { get; set; } = new();
    }

    public class LeagueBuilderSpotDto
    {
        public int Team { get; set; }
        public int Slot { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class LeagueBuilderPlayerDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsGuest { get; set; }
        public string? Avatar { get; set; }
        // Display only: "Gold II", "Master".
        public string CurLabel { get; set; } = "";
        public string PeakLabel { get; set; } = "";
        // Badge file name without extension, e.g. "gold" (the picture address comes from RankBadgeCatalog.ByKey()).
        public string CurKey { get; set; } = "";
        public string PeakKey { get; set; } = "";
        // Lane indexes in priority order; Main is the first.
        public int Main { get; set; }
        public List<int> Lanes { get; set; } = new();
        // Champions listed on the main lane (the clash rule looks at "fewer than 3").
        public int MainPool { get; set; }
        public int? DoBetterWithId { get; set; }
        // Step 8g: holds the #1 rank badge / the MVP badge (LeagueBadges). Shown as small icons on the slots.
        public bool RankKing { get; set; }
        public bool MvpKing { get; set; }
        // Step 13: holds the Clash winner badge (the players of the best Clash team on the Overview).
        public bool ClashKing { get; set; }
    }

    // ---- JSON endpoint: POST /League/Generate ----

    public class LeagueBuilderSlotDto
    {
        public int? Lane { get; set; }
        public int? PlayerId { get; set; }
    }

    public class LeagueBuilderTeamDto
    {
        public string Name { get; set; } = "";
        // Team strength (sum of player values), only for the two rank / tier balance modes.
        public double? Strength { get; set; }
        public List<LeagueBuilderSlotDto> Slots { get; set; } = new();
    }

    // A lane clash: both players main this lane and both list fewer than 3 champions there.
    public class LeagueBuilderClashRef
    {
        public int Lane { get; set; }
        public int A { get; set; }
        public int B { get; set; }
    }

    // "Swap one to a secondary lane": this player takes this lane.
    public class LeagueBuilderSwapRef
    {
        public int PlayerId { get; set; }
        public int Lane { get; set; }
    }

    public class LeagueBuilderRequest
    {
        // generate = build new teams from PlayerIds.
        // evaluate = recheck conflicts and strengths for the posted Teams (after a manual swap or removal).
        // clashSame / clashSwap = apply option 1 / option 2 of the lane-clash dialog to the posted Teams.
        public string Op { get; set; } = "generate";
        // "normal" / "aram" / "arena".
        public string Mode { get; set; } = "normal";
        // 1 random, 2 lanes, 3 rank + lanes, 4 custom tiers + lanes.
        public int Balance { get; set; } = 2;
        public bool AvoidClashes { get; set; } = true;
        public List<int> PlayerIds { get; set; } = new();
        public List<LeagueBuilderTeamDto>? Teams { get; set; }
        // Pairs the user chose to keep against each other: [[idA, idB], ...].
        public List<List<int>>? Ignored { get; set; }
        public LeagueBuilderClashRef? Clash { get; set; }
        public LeagueBuilderSwapRef? Swap { get; set; }
    }

    public class LeagueBuilderClashDto
    {
        public int Lane { get; set; }
        public int A { get; set; }
        public int B { get; set; }
        // Option 1 is only offered when a layout exists where both still play a lane they list.
        public bool CanSameTeam { get; set; }
        // The player who moves across to keep the team sizes equal (option 1).
        public int? MovedPlayerId { get; set; }
        // Option 2 choices.
        public List<LeagueBuilderSwapRef> Swaps { get; set; } = new();
    }

    public class LeagueBuilderResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        // e.g. "Lanes don't apply to ARAM, so this mode was assigned randomly."
        public string? Note { get; set; }
        public string Mode { get; set; } = "";
        public int Balance { get; set; }
        public List<LeagueBuilderTeamDto> Teams { get; set; } = new();
        // Every clash still on the board (empty unless Normal + Lanes balance + clash check on).
        public List<LeagueBuilderClashRef> Conflicts { get; set; } = new();
        // Details for the first clash, for the dialog.
        public LeagueBuilderClashDto? Clash { get; set; }
    }
}
