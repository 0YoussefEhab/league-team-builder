namespace LeagueTeamBuilder.Models
{
    // Step 4d: request / response shapes of the match lifecycle endpoints (POST /LeagueMatch/Submit, Start,
    // Finish, EditResult, Stats, Delete). The tables themselves are Models/LeagueMatch.cs (Step 4a). Lanes are LeagueLane indexes.

    public class LeagueMatchSubmitRequest
    {
        // "normal" / "aram" / "arena".
        public string Mode { get; set; } = "normal";
        // 1 random, 2 lanes, 3 rank + lanes, 4 custom tiers + lanes (only written into the builder log).
        public int Balance { get; set; } = 2;
        // The teams exactly as they are on the builder page.
        public List<LeagueBuilderTeamDto> Teams { get; set; } = new();

        // Step 6e1: set for a game of a Clash series. Then Mode, Balance and Teams are ignored: the mode comes from the
        // event and the team from ClashSlots (exactly 5 slots, like POST /LeagueClash/Build). ClashLog = the builder log
        // lines the Clash page shows (stored on the game, spec section 8).
        public int? ClashEventId { get; set; }
        // Step 11c: the team of the clash this game is for. Null = the clash's first team ("Team one").
        public int? ClashTeamId { get; set; }
        public List<LeagueClashSlotDto?>? ClashSlots { get; set; }
        public List<string?>? ClashLog { get; set; }
    }

    public class LeagueMatchIdRequest
    {
        public int Id { get; set; }
    }

    public class LeagueMatchFinishRequest
    {
        public int Id { get; set; }
        // Index of the winning team. Required.
        public int? Winner { get; set; }
        // Optional team kills, two-team matches only (spec section 14: "25-18").
        public int? Kills0 { get; set; }
        public int? Kills1 { get; set; }
        // Results/export step 1: "Played on", Cairo local time as the form sends it ("2026-10-16T21:05"). Blank = now.
        // Only Finish reads it, and only for someone allowed LeagueMatch > EditDate (EditResult ignores it).
        public string? PlayedOn { get; set; }
    }

    // Results/export step 1: POST /LeagueMatch/EditDate. Changes only the played date of a finished match.
    public class LeagueMatchDateRequest
    {
        public int Id { get; set; }
        // Cairo local time, "yyyy-MM-ddTHH:mm". Required here.
        public string? PlayedOn { get; set; }
    }

    // Step 5c: one save for the whole match. Rows are matched by LeagueMatchPlayer.Id (the id the popup form
    // carries); every part is optional and an empty part clears what was stored (spec section 14).
    public class LeagueMatchStatsRequest
    {
        public int Id { get; set; }
        // Match length in seconds (optional; empty clears it).
        public int? DurationSeconds { get; set; }
        public List<LeagueMatchStatsPlayerDto?> Players { get; set; } = new();
    }

    public class LeagueMatchStatsPlayerDto
    {
        // LeagueMatchPlayer.Id.
        public int PlayerId { get; set; }
        public int? ChampionId { get; set; }
        // LeagueLane index of the lane actually played.
        public int? Lane { get; set; }
        public int? Kills { get; set; }
        public int? Deaths { get; set; }
        public int? Assists { get; set; }
        // Optional extra stats (spec section 14); an empty value clears it.
        public int? Cs { get; set; }
        public int? DamageDealt { get; set; }
        public int? DamageTaken { get; set; }
        public int? Gold { get; set; }
        public int? VisionScore { get; set; }
        public int? ObjectiveDamage { get; set; }
        public string? Notes { get; set; }
        // Step 13d: the other stats (wards, turret damage...) as name -> whole number. null = the page did not send them: what is saved
        // stays as it is. Sent (even empty) = it replaces what is saved.
        public Dictionary<string, int>? Extras { get; set; }
    }

    public class LeagueMatchPlayerDto
    {
        public string Name { get; set; } = "";
        public bool IsGuest { get; set; }
        public int? Lane { get; set; }
    }

    public class LeagueMatchTeamDto
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public List<LeagueMatchPlayerDto> Players { get; set; } = new();
    }

    public class LeagueMatchDto
    {
        public int Id { get; set; }
        public string ModeKey { get; set; } = "";
        public string ModeName { get; set; } = "";
        // "submitted" / "live" / "done".
        public string Status { get; set; } = "";
        public DateTime SubmittedUtc { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }
        // Step 6e1: set for a game of a Clash series.
        public int? ClashEventId { get; set; }
        public int? GameNumber { get; set; }
        public int? Winner { get; set; }
        public int? Kills0 { get; set; }
        public int? Kills1 { get; set; }
        public List<LeagueMatchTeamDto> Teams { get; set; } = new();
    }

    public class LeagueMatchResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        // The match after the action (null after Delete).
        public LeagueMatchDto? Match { get; set; }
        public bool Deleted { get; set; }
        // Step 12c.1: a plain-words note for an action that worked but left something unset (Back to automatic found no MVP).
        public string? Notice { get; set; }
    }

    // Step 8a2: an admin picks the MVPs of a finished match by hand (POST /LeagueMatch/SetMvp), or hands them back to the
    // automatic rule with Auto = true. The four picks are LeagueProfile ids; null clears that pick. A manual save sets
    // LeagueMatch.MvpIsManual, so a later stats save does not overwrite it.
    public class LeagueMatchMvpRequest
    {
        public int Id { get; set; }
        public bool Auto { get; set; }
        public int? Mvp { get; set; }
        public int? TeamMvp { get; set; }
        public int? StatsMvp { get; set; }
        public int? StatsTeamMvp { get; set; }
    }

    public class LeagueMvpBackfillResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public int Checked { get; set; }
        public int Updated { get; set; }
    }

    // Step 13a: POST /LeagueMatch/ImportStats (permission LeagueMatch > ImportStats). Saves the game name of players who have none.
    // A name is stored as the League profile's Riot ID, and only when that profile has no game name yet.
    public class LeagueImportNamesRequest
    {
        public int Id { get; set; }
        public List<LeagueImportNameDto?> Names { get; set; } = new();
    }

    public class LeagueImportNameDto
    {
        // LeagueMatchPlayer.Id.
        public int PlayerId { get; set; }
        // What was typed ("Suguru" or "Suguru#EG1"); in the answer, the game name now on that player's profile (null = none).
        public string? GameName { get; set; }
    }

    public class LeagueImportNamesResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public int Saved { get; set; }
        // Players of the match with the game name they have now (after the save).
        public List<LeagueImportNameDto> Players { get; set; } = new();
    }
}
