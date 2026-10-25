namespace LeagueTeamBuilder.Models
{
    // Step 5a: shapes of the History page and the match popup (docs/LEAGUE_BUILD_SPEC.md section 8).
    // Everything here is built from the match SNAPSHOT (names, ranks, lanes copied at submit time). The only
    // live data read is the player's picture (profile) and the champion's name and icon (catalog).

    public enum LeagueHistoryFilter { All, Done, Open, Missing }

    // Step 8h: the extra History filters. Draft = Normal Draft (mode key "normal").
    public enum LeagueHistoryKind { All, Custom, Clash }
    public enum LeagueHistoryMode { All, Draft, Aram, Arena }

    // What the visitor asked for (query string). Built by LeagueHistoryService.ParseQuery, which never throws.
    public class LeagueHistoryQuery
    {
        public LeagueHistoryFilter Filter { get; set; } = LeagueHistoryFilter.All;
        public LeagueHistoryKind Kind { get; set; } = LeagueHistoryKind.All;
        public LeagueHistoryMode Mode { get; set; } = LeagueHistoryMode.All;
        // Trimmed, at most LeagueHistoryService.MaxSearchLength characters; "" = no search.
        public string Search { get; set; } = "";
        // A LeagueProfile.Id; null = everybody.
        public int? PlayerId { get; set; }
        public int Page { get; set; } = 1;

        // Kind, mode, search or player is set (the status pill alone does not count).
        public bool HasExtra => Kind != LeagueHistoryKind.All || Mode != LeagueHistoryMode.All || Search.Length > 0 || PlayerId != null;
    }

    public class LeagueHistoryRow
    {
        public int Id { get; set; }
        public bool IsClash { get; set; }
        public string ModeName { get; set; } = "";
        // "submitted" / "live" / "done".
        public string Status { get; set; } = "";
        public DateTime PlayedUtc { get; set; }
        // Results/export step 1: the result was entered more than 6 hours from the played time (LeagueDayRules.IsAddedLater).
        public bool AddedLater { get; set; }
        // Only for a done match.
        public string? WinnerName { get; set; }
        // Team kills, only when the match has two teams and they were entered.
        public int? Kills0 { get; set; }
        public int? Kills1 { get; set; }
        public List<string> TeamNames { get; set; } = new();
        public int PlayerCount { get; set; }
        // Clash games only (Step 6e5): "Game 2 of 4" and the event's title. Null for a custom match.
        public int? GameNumber { get; set; }
        public string? GameLabel { get; set; }
        public string? ClashTitle { get; set; }
        // Step 11f, Clash only: the name of the clash team that played the game ("Team one"). Null for a custom match.
        public string? ClashTeamName { get; set; }
        // Done, and at least one player has no K/D/A entered yet (the "Matches missing stats" filter).
        public bool MissingStats { get; set; }
    }

    public class LeagueHistoryPageModel
    {
        public List<LeagueHistoryRow> Rows { get; set; } = new();
        public LeagueHistoryFilter Filter { get; set; } = LeagueHistoryFilter.All;
        public int Page { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int TotalCount { get; set; }
        // Count badge on the "Missing stats" filter, whatever filter is active.
        public int MissingCount { get; set; }
        // Step 8h: what was asked for, and the name of the player filter ("that player" when the profile is gone).
        public LeagueHistoryQuery Query { get; set; } = new();
        public string? PlayerName { get; set; }
        // LeagueMatch > Delete: shows the Delete button in the popup.
        public bool CanDelete { get; set; }
        // MVP redesign Step 5e: LeagueMatch > BackfillMvps shows the "Recompute MVPs" button (re-runs the MVP rule on every finished match without a hand-picked MVP).
        public bool CanRecomputeMvps { get; set; }
        // Step 12f: League > ExportData shows the three export buttons. Keys are Cairo days ("2026-10-17"); a count of 0 greys the button out.
        public bool CanExport { get; set; }
        public string ExportTodayKey { get; set; } = "";
        public string ExportYesterdayKey { get; set; } = "";
        public int ExportTodayCount { get; set; }
        public int ExportYesterdayCount { get; set; }
    }

    public record LeagueMatchTimelineStep(string Label, DateTime Utc);

    // One entry of the champion picker in the stats form (serialized into the page as JSON).
    public record LeagueStatsChampion(int Id, string Name, string? IconUrl, bool Retired);

    // Step 8a3: one line of the MVP section ("Match MVP (K/D/A): Alex") and one choice of the admin pick form.
    // Step 7: Why = the short "why this MVP" sentence under the line; null when there is nothing to explain (hand pick, K/D/A pick, old formula).
    public record LeagueMvpLine(string Label, string Name, string? Why = null);
    public record LeagueMvpOption(int ProfileId, string Name);

    public class LeagueMatchDetailPlayer
    {
        // Step 8a3: LeagueMatchPlayer.ProfileId (null once the profile is gone) and the MVP tags this player holds in this match.
        public int? ProfileId { get; set; }
        // Step 13a: the name in the game (Riot ID before the "#"); null when the profile has none or is gone.
        public string? GameName { get; set; }
        public List<string> MvpTags { get; set; } = new();
        public string Name { get; set; } = "";
        public bool IsGuest { get; set; }
        // "Top", "Jungle", "Mid", "ADC", "Support"; null for ARAM and Arena.
        public string? LaneLabel { get; set; }
        // Only when it was recorded and differs from the lane the builder gave.
        public string? PlayedLaneLabel { get; set; }
        public string? AvatarUrl { get; set; }
        public string Initial { get; set; } = "?";
        // Ranks the teams were balanced on (snapshot). RankKey names the badge file (e.g. "gold").
        public string? RankKey { get; set; }
        public string? RankLabel { get; set; }
        public string? PeakRankLabel { get; set; }
        public string? ChampionName { get; set; }
        public string? ChampionIconUrl { get; set; }
        // "5 / 2 / 7", with a dash for a part that was not entered. Null when none of the three exists.
        public string? Kda { get; set; }

        // Step 5c, for the stats form: LeagueMatchPlayer.Id, the raw saved values, and the lane the builder gave.
        public int PlayerId { get; set; }
        public int? ChampionId { get; set; }
        public int? Lane { get; set; }
        public int? PlayedLane { get; set; }
        public int? Kills { get; set; }
        public int? Deaths { get; set; }
        public int? Assists { get; set; }
        public int? Cs { get; set; }
        public int? DamageDealt { get; set; }
        public int? DamageTaken { get; set; }
        public int? Gold { get; set; }
        public int? VisionScore { get; set; }
        public int? ObjectiveDamage { get; set; }
        public string? Notes { get; set; }
        // "CS 180 · Damage 21,400 · ..." over the extra stats that were entered; null when none.
        public string? ExtraText { get; set; }
        // Step 13d: the other stats kept in ExtraJson (wards, turret damage...), for the stats form and as one line in the popup.
        public List<LeagueOtherStat> OtherStats { get; set; } = new();
        public string? OtherText { get; set; }
    }

    public class LeagueOtherStat
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public int Value { get; set; }
    }

    public class LeagueMatchDetailTeam
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public bool IsWinner { get; set; }
        public int? Kills { get; set; }
        public List<LeagueMatchDetailPlayer> Players { get; set; } = new();
        // "K 31 · D 25 · A 60" over the players who have that stat; null when nobody has any.
        public string? TotalsText { get; set; }
        // How many players the totals come from (shown as "from 3 of 5 players" when not everybody).
        public int StatPlayers { get; set; }
        // A team with nobody on it (the Opponents of a Clash game): the popup shows a short note instead of a list.
        public bool IsEmpty => Players.Count == 0;
    }

    public class LeagueMatchDetailModel
    {
        public int Id { get; set; }
        public bool IsClash { get; set; }
        public string ModeName { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime PlayedUtc { get; set; }
        // Results/export step 1: "Added later" tag, and the "Played on" form (LeagueMatch > EditDate, finished match).
        // PlayedOnInput = PlayedUtc as Cairo wall-clock time for <input type="datetime-local">.
        public bool AddedLater { get; set; }
        public bool CanEditDate { get; set; }
        public string PlayedOnInput { get; set; } = "";
        public string? WinnerName { get; set; }
        // Index of the winning team (the edit form preselects it).
        public int? WinnerIndex { get; set; }
        // When a finished match was last changed after it was finished (result edit or stats save).
        public DateTime? EditedUtc { get; set; }
        public int? DurationSeconds { get; set; }
        // Clash only (Step 6 adds the event title and the series record).
        public int? GameNumber { get; set; }
        // Step 6e5, Clash only: "Game 2 of 4", the event title and the series so far ("2W 1L", "3 games saved").
        public string? GameLabel { get; set; }
        public string? ClashTitle { get; set; }
        // Step 11f, Clash only: the clash team that played the game. SeriesText is then that team's record, not the whole clash's.
        public string? ClashTeamName { get; set; }
        public string? SeriesText { get; set; }
        public string? BuilderLog { get; set; }
        public List<LeagueMatchTimelineStep> Timeline { get; set; } = new();
        public List<LeagueMatchDetailTeam> Teams { get; set; } = new();
        public bool CanDelete { get; set; }
        // Results/export step 3: data for the "Game result" picture; null when this match cannot have one (see LeagueResultImageRules).
        public ResultImageSpec? ResultImage { get; set; }
        // Step 12e: show the Session image button (League > SessionImage ticked, and ResultImage is not null = a finished custom game with two teams).
        public bool CanSessionImage { get; set; }
        // Step 12g: a Clash game knows its clash and team, so the popup can offer the Clash progress picture (needs LeagueExport > Render).
        public int? ClashEventId { get; set; }
        public int? ClashTeamId { get; set; }
        public bool CanClashProgress { get; set; }
        // Step 8a3: the MVPs of a finished match (only the ones that exist), whether an admin picked them by hand, and the
        // admin pick form: LeagueMatch > SetMvp and the match is finished. Options = players with a profile, winning / losing team.
        public List<LeagueMvpLine> MvpLines { get; set; } = new();
        public bool MvpIsManual { get; set; }
        public bool CanSetMvp { get; set; }
        public int? MvpId { get; set; }
        public int? StatsMvpId { get; set; }
        public int? TeamMvpId { get; set; }
        public int? StatsTeamMvpId { get; set; }
        public List<LeagueMvpOption> WinnerOptions { get; set; } = new();
        public List<LeagueMvpOption> LoserOptions { get; set; } = new();
        // LeagueMatch > EditResult, and the match is finished.
        public bool CanEditResult { get; set; }
        // LeagueMatch > Stats, and the match is finished (Step 5c).
        public bool CanEditStats { get; set; }
        // Step 13a: LeagueMatch > ImportStats, the match is finished and has players ("Fill from screenshots").
        public bool CanImportStats { get; set; }
        // The builder gave lanes (Normal Draft); ARAM and Arena have none, so the form has no lane column.
        public bool HasLanes { get; set; }
        // Champion picker list for the stats form: active champions plus any retired one already used here.
        public List<LeagueStatsChampion> Champions { get; set; } = new();
    }

    // Date helpers shared by the views: a <time datetime> value that is always read as UTC (the database may hand
    // back a Kind of Unspecified) plus a plain-text fallback for when JavaScript is off.
    public static class LeagueHistoryFormat
    {
        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv);
        public static string Plain(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("d MMM yyyy HH:mm", Inv) + " UTC";
        // Results/export step 1: the History pages show Cairo time (league-time.js does it in the browser; this is the fallback).
        public static string PlainCairo(DateTime utc) => LeagueTeamBuilder.Services.LeagueDayRules.PlainCairo(utc);
    }
}
