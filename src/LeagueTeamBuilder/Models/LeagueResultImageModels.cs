namespace LeagueTeamBuilder.Models
{
    // Results/export step 3: what the "Game result" picture needs, built from the match popup's data
    // (Services/LeagueResultImageRules.Build) and drawn in the browser (wwwroot/js/league-result-image.js).
    // Badge: "mvp" = the winning team's game MVP, "team" = the Team MVP of the losing side, null = none.
    public record ResultImagePlayer(string Name, string? Lane, bool Guest, int? Kills, int? Deaths, int? Assists, string? Badge);

    public record ResultImageTeam(int Index, string Name, bool Won, int? Kills, List<ResultImagePlayer> Players);

    // Date = the Played-on day in Cairo ("17 Oct 2026"). Mode = "normal" (lanes) or "aram" (no lanes); it picks the background.
    public record ResultImageSpec(int MatchId, string Title, string Date, string Mode, bool HasLanes, int Winner, List<ResultImageTeam> Teams);

    // Step 12d: the Clash progress picture (spec section 18.2, image 3). One clash team's finished games, up to 4 columns.
    // Lane = "TOP" / "JGL" / "MID" / "ADC" / "SUP" in Normal Draft, null in ARAM. Mvp = the game's highlighted player of OUR team.
    public record ClashProgressPlayer(string Name, string? Lane, bool Guest, int? Kills, int? Deaths, int? Assists, bool Mvp);

    // Won = the Clash team won this game. Number = the team's game number (1..4).
    public record ClashProgressGame(int Number, bool Won, List<ClashProgressPlayer> Players);

    // Games always has MaxGames entries; null = not played yet (dashed "Not played yet" column).
    // SeriesMvpName is null when nobody qualifies or the best score is shared (LeagueSeriesMvpRules.Best).
    public record ClashProgressSpec(
        int EventId, int TeamId, string Title, string TeamName, string When, string Mode, bool HasLanes,
        int Wins, int Losses, int Played, int MaxGames, List<ClashProgressGame?> Games,
        string? SeriesMvpName, int SeriesMvpGames)
    {
        // The browser tells this picture from the game result by this field.
        public string Kind => "clash";
    }

    // GET /LeagueClash/ProgressImage: JSON out. Spec is null when Ok is false.
    public class LeagueClashProgressResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public ClashProgressSpec? Spec { get; set; }
    }

    // Step 12e: the Session collection picture (spec section 18.2, image 4). Up to four finished custom games of one Cairo day.
    // Lane = "TOP" / "JGL" / "MID" / "ADC" / "SUP" when the player had a lane, else null.
    public record SessionImagePlayer(string Name, string? Lane, int? Kills, int? Deaths, int? Assists);

    // Winner: 0 = side A (the first game's first team), 1 = side B. KillsA / KillsB are both null unless both are known.
    // Mvp = the winning team's game MVP, TeamMvp = the losing team's Team MVP (null = no stored pick).
    public record SessionImageGame(int Number, string ModeName, int Winner, int? KillsA, int? KillsB, SessionImagePlayer? Mvp, SessionImagePlayer? TeamMvp);

    // The best game MVP of the block by K/D/A score. Game = its number in the day.
    public record SessionImageBest(string Name, int Kills, int Deaths, int Assists, int Game);

    // Team 1 = side A (the first game's first team), Team 2 = side B; the picture always says "Team 1" / "Team 2".
    // FirstNumber = number of the first game in this picture (5 for "Games 5-8"), TotalGames = eligible games that day.
    public record SessionImageSpec(
        int MatchId, string Date, string Mode, bool HasLanes,
        int WinsA, int WinsB, int FirstNumber, int TotalGames, List<SessionImageGame> Games, SessionImageBest? Best)
    {
        // The browser tells this picture from the others by this field.
        public string Kind => "session";
    }

    // Step 12h2: GET /LeagueExport/Render?kind=result: the Game result picture data of one finished custom game. Spec is null when Ok is false.
    public class LeagueGameImageResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public ResultImageSpec? Spec { get; set; }
    }

    // GET /League/SessionImage: JSON out. Spec is null when Ok is false.
    public class LeagueSessionImageResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public SessionImageSpec? Spec { get; set; }
    }

    // Step 12f: one line of the day checklist in the Export matches dialog. Number = the game's number in the Cairo day (1..n).
    // TimeText = the play time in Cairo ("17 Oct, 20:00"); WinnerText = "Team 1 won".
    public class ExportDayRow
    {
        public int Id { get; set; }
        public int Number { get; set; }
        public string ModeName { get; set; } = "";
        public string TimeText { get; set; } = "";
        public string WinnerText { get; set; } = "";
        public bool AddedLater { get; set; }
        public bool MissingKda { get; set; }
    }

    // GET /League/ExportData: JSON out (12f). Rows = the day's checklist (always). Percent / OneSession / Message / DefaultMode describe the
    // ticked games (all of them when no ids were sent). Sessions (mode=session) or Games (mode=games) hold the pictures' data, in order.
    public class LeagueExportResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string Day { get; set; } = "";
        public string DayText { get; set; } = "";
        public List<ExportDayRow> Rows { get; set; } = new();
        public int Ticked { get; set; }
        public int Percent { get; set; }
        public bool OneSession { get; set; }
        public string Message { get; set; } = "";
        public string DefaultMode { get; set; } = "games";
        public List<SessionImageSpec> Sessions { get; set; } = new();
        public List<ResultImageSpec> Games { get; set; } = new();
    }
}
