namespace LeagueTeamBuilder.Models
{
    // Settings > Clash schedule form (create and edit). The browser sends the picked time twice:
    // StartsAtUtc (an ISO "...Z" string the script fills in from the local time) and StartsAtLocal
    // (the plain datetime-local value). Without JavaScript only the second one arrives and it is read as UTC.
    public class LeagueClashForm
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? StartsAtUtc { get; set; }
        public string? StartsAtLocal { get; set; }
        public LeagueMode Mode { get; set; } = LeagueMode.Normal;
    }

    public class LeagueClashRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime StartsAtUtc { get; set; }
        public LeagueMode Mode { get; set; }
        public LeagueClashStatus Status { get; set; }
        public int CanJoin { get; set; }
        public int Busy { get; set; }
        public int Games { get; set; }
        // Only an open clash (game 1 not started) can be edited or cancelled.
        public bool CanChange => Status == LeagueClashStatus.Open;
    }

    public class LeagueClashSchedulePageModel
    {
        public List<LeagueClashRow> Events { get; set; } = new();
        public LeagueClashForm Form { get; set; } = new();
        public Dictionary<string, string> Errors { get; set; } = new();
    }

    public class LeagueClashEditPageModel
    {
        public LeagueClashForm Form { get; set; } = new();
        public Dictionary<string, string> Errors { get; set; } = new();
    }

    // The member response page (spec section 4).
    public class LeagueClashRespondPageModel
    {
        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public DateTime StartsAtUtc { get; set; }
        public LeagueMode Mode { get; set; }
        public LeagueClashStatus Status { get; set; }
        // False when the signed-in user has no league profile (nothing to answer for).
        public bool HasProfile { get; set; }
        public LeagueClashAnswer? Answer { get; set; }
        // Answers can be changed only while the clash is Open (game 1 not started).
        public bool Locked => Status != LeagueClashStatus.Open;
    }

    // ---- Step 6d: the Clash page (docs/LEAGUE_BUILD_SPEC.md section 4). The server builds and checks, the browser renders. ----

    // One entry of the event select (Open and InProgress clashes only).
    public class LeagueClashEventOption
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime StartsAtUtc { get; set; }
        public LeagueMode Mode { get; set; }
        public LeagueClashStatus Status { get; set; }
    }

    // The series so far: a finished game is a win when the Clash team (team index 0) won, a loss when the opponents did.
    public class LeagueClashRecord
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        // Every game saved for the event, finished or not (6e fills this).
        public int Games { get; set; }
        public int Finished => Wins + Losses;
        public string Text => Finished == 0 ? "No games finished yet" : $"{Wins}W {Losses}L";
    }

    // Step 6e3: one saved game of the series, as the Games tile shows it. Status is the match status key
    // (submitted / live / done); Winner is the winning team index once done (0 = the Clash team won).
    public class LeagueClashGameDto
    {
        public int Id { get; set; }
        public int Number { get; set; }
        public string Status { get; set; } = "submitted";
        public int? Winner { get; set; }
        // Step 11c: the team that played it (null only for a game with no team, which the backfill should have removed).
        public int? TeamId { get; set; }
        // Step 11g: the league profile ids on the game's Clash team (team 0). The page uses the ones of unfinished games to keep
        // a player off two teams at once.
        public List<int> PlayerIds { get; set; } = new();
    }

    // Step 11c: one team of the selected clash, as the Clash page lists it. Games = how many games the team has saved.
    public class LeagueClashTeamDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
        public int Games { get; set; }
    }

    // Step 11c: POST /LeagueClash/CreateTeam, RenameTeam, DeleteTeam. JSON in, JSON out, like EndClash.
    public class LeagueClashTeamCreateRequest
    {
        public int EventId { get; set; }
        public string? Name { get; set; }
    }

    public class LeagueClashTeamRenameRequest
    {
        public int TeamId { get; set; }
        public string? Name { get; set; }
    }

    public class LeagueClashTeamDeleteRequest
    {
        public int TeamId { get; set; }
    }

    public class LeagueClashTeamResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        // The team after a create or rename (null after a delete or an error).
        public LeagueClashTeamDto? Team { get; set; }
    }

    public class LeagueClashPageModel
    {
        public List<LeagueClashEventOption> Events { get; set; } = new();
        public int? SelectedId { get; set; }
        public LeagueClashEventOption? Selected { get; set; }
        // Set when the link asked for a clash that is over, cancelled or gone.
        public string? Notice { get; set; }
        public List<LeagueBuilderPlayerDto> Players { get; set; } = new();
        public List<string> Lanes { get; set; } = new();
        // Profile ids that answered Busy / I can join for the selected clash (guests too since 8i: a moderator saves it). No answer = free.
        public List<int> AnsweredBusy { get; set; } = new();
        public List<int> AnsweredFree { get; set; } = new();
        // Custom tiers can be picked only when there are at least two (TeamBalancer falls back to rank below that).
        public bool HasTiers { get; set; }
        public LeagueClashRecord Record { get; set; } = new();
        // Step 6e3: the saved games of the selected clash, by game number. Step 11c: all teams' games; each has its TeamId.
        public List<LeagueClashGameDto> Games { get; set; } = new();
        // Step 11c: the teams of the selected clash, first team first. A clash with none gets "Team one" when the page opens.
        public List<LeagueClashTeamDto> Teams { get; set; } = new();
        // Step 6e6a: the map image of the selected clash's mode (null when none is set). Only the Discord picture uses it.
        public string? MapUrl { get; set; }
        // PLAN-leaderboard-export.md E4: the map picture needs the player places and the map shape of the mode (same as the Builder page).
        public double MapAspect { get; set; } = 16d / 9d;
        public List<LeagueBuilderSpotDto> Spots { get; set; } = new();
    }

    // POST /LeagueClash/Build. Op "build" = make the team from the pool and the busy set; Op "evaluate" = recheck the
    // posted Slots after a hand edit (strength, lane fit, warnings). Nothing is stored on the server.
    public class LeagueClashBuildRequest
    {
        public int EventId { get; set; }
        public string Op { get; set; } = "build";
        // "rank" or "tier".
        public string RankSystem { get; set; } = "rank";
        // The busy set the page is working with (answers plus the manual toggles). Guests can be in it since 8i.
        public List<int> Busy { get; set; } = new();
        public List<LeagueClashSlotDto>? Slots { get; set; }
    }

    public class LeagueClashSlotDto
    {
        // Lane index 0..4 for Normal, null for ARAM.
        public int? Lane { get; set; }
        public int? PlayerId { get; set; }
        public bool Manual { get; set; }
        // "Main", "Good lane", "OK lane" or "Last resort" (Normal only), and "ok" / "warn" for its colour.
        public string? Fit { get; set; }
        public string? FitKind { get; set; }
    }

    // Step 6e4a: POST /LeagueClash/EndClash. JSON in, JSON out, like Build.
    public class LeagueClashEndRequest
    {
        public int EventId { get; set; }
    }

    public class LeagueClashEndResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
    }

    // Step 8i: POST /LeagueClash/SetGuestBusy. JSON in, JSON out, like EndClash. Saves a guest's answer for the clash.
    public class LeagueClashGuestBusyRequest
    {
        public int EventId { get; set; }
        public int ProfileId { get; set; }
        public bool Busy { get; set; }
    }

    public class LeagueClashGuestBusyResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        // The saved answer, so the page can update itself.
        public bool Busy { get; set; }
    }

    // Step 6e4d: POST /LeagueClash/NextGame. Read-only: nothing is saved. The page sends the rank system it is using
    // and the busy set it is working with, so the rechecked team (strength, fit tags, "marked busy" warnings) matches
    // what the page shows.
    public class LeagueClashNextGameRequest
    {
        public int EventId { get; set; }
        // Step 11c: whose last game to copy. Null = the clash's first team.
        public int? TeamId { get; set; }
        // "rank" or "tier".
        public string RankSystem { get; set; } = "rank";
        public List<int> Busy { get; set; } = new();
    }

    // The last finished game's team, ready to load on screen. Team has the same shape as the answer to Build / evaluate.
    public class LeagueClashNextGameResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        // The number the next game will get, and the game the team was copied from.
        public int NextNumber { get; set; }
        public int FromGame { get; set; }
        public LeagueClashBuildResponse? Team { get; set; }
    }

    public class LeagueClashLogDto
    {
        public string Kind { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public class LeagueClashNoFitDto
    {
        public int BusyPlayerId { get; set; }
        public int ReplacementPlayerId { get; set; }
        public int Lane { get; set; }
    }

    public class LeagueClashBuildResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        // "normal" / "aram".
        public string Mode { get; set; } = "";
        public string RankSystem { get; set; } = "rank";
        public List<LeagueClashSlotDto> Slots { get; set; } = new();
        // Only on "build"; the page keeps the last log across hand edits.
        public List<LeagueClashLogDto> Log { get; set; } = new();
        public List<LeagueClashNoFitDto> NoFit { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public bool IsEmpty { get; set; }
        public double Strength { get; set; }
    }
}
