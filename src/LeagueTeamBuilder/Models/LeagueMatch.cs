namespace LeagueTeamBuilder.Models
{
    // M3: a played (or about to be played) game. Teams and players are SNAPSHOTS: names, guest flag and
    // ranks are copied at submit time so history survives profile edits, guest linking and deletion
    // (docs/LEAGUE_BUILD_SPEC.md R4, section 6).
    public class LeagueMatch
    {
        public int Id { get; set; }
        public LeagueMatchKind Kind { get; set; }
        // Game mode snapshot: "normal" / "aram" / "arena" and its display name at the time.
        public string ModeKey { get; set; } = "";
        public string ModeName { get; set; } = "";
        public LeagueMatchStatus Status { get; set; }

        // Shown in History. Set to SubmittedUtc on submit and moved to FinishedUtc when the game finishes.
        public DateTime PlayedUtc { get; set; }
        public DateTime SubmittedUtc { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }

        // Index of the winning LeagueMatchTeam (Arena can have more than 2 teams). Null until finished.
        public int? WinnerTeamIndex { get; set; }

        // Clash series (Step 6, M4): the event this game belongs to and its number in the series (1..4).
        public int? ClashEventId { get; set; }
        public LeagueClashEvent? ClashEvent { get; set; }
        public int? GameNumber { get; set; }
        // Step 11b: which team of the clash played this game. Null for Custom games, and (until Step 11c) for new Clash games.
        public int? ClashTeamId { get; set; }
        public LeagueClashTeam? ClashTeam { get; set; }

        // The builder's log text (who was busy / replaced), shown in the match popup.
        public string? BuilderLog { get; set; }

        public string? CreatedByUserId { get; set; }

        // Optional match-level stats (spec section 14). Shown only when present.
        public int? TeamKills0 { get; set; }
        public int? TeamKills1 { get; set; }
        public int? DurationSeconds { get; set; }
        public string? RiotMatchId { get; set; }   // unused in v1

        // Step 8 MVPs. Never set unless the stats they are based on are filled in (see PLAN-league.md, "MVP design").
        // Live links to a profile; SetNull when the profile is deleted. Written by step 8a2, only the columns exist in 8a.
        // MvpProfileId = best K/D/A score on the winning team (counts for the MVP badge and the leaderboard).
        // TeamMvpProfileId = best K/D/A score on the losing team (2-team matches only).
        // StatsMvpProfileId / StatsTeamMvpProfileId = the same two picks from the all-stats score.
        public int? MvpProfileId { get; set; }
        public int? TeamMvpProfileId { get; set; }
        public int? StatsMvpProfileId { get; set; }
        public int? StatsTeamMvpProfileId { get; set; }
        // True when an admin picked the MVP(s) by hand; a later stats save must not overwrite a manual pick.
        public bool MvpIsManual { get; set; }

        // "Log who edited and when" (spec section 14).
        public string? StatsEditedByUserId { get; set; }
        public DateTime? StatsEditedUtc { get; set; }

        public List<LeagueMatchTeam> Teams { get; set; } = new();
    }

    public class LeagueMatchTeam
    {
        public int Id { get; set; }
        public int MatchId { get; set; }
        public LeagueMatch? Match { get; set; }
        // 0-based position inside the match; LeagueMatch.WinnerTeamIndex refers to this.
        public int Index { get; set; }
        public string Name { get; set; } = "";

        public List<LeagueMatchPlayer> Players { get; set; } = new();
    }

    public class LeagueMatchPlayer
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public LeagueMatchTeam? Team { get; set; }

        // Lane the builder assigned (null for ARAM / Arena, which have no lanes).
        public LeagueLane? Lane { get; set; }

        // Live link to the profile; SetNull when the profile is deleted or merged away. The snapshots below stay.
        public int? ProfileId { get; set; }
        public LeagueProfile? Profile { get; set; }
        public string NameSnapshot { get; set; } = "";
        public bool WasGuest { get; set; }

        // Ranks the teams were balanced on (proposed in PLAN-league.md, beyond the spec text).
        public LeagueRank? CurRankSnapshot { get; set; }
        public int? CurDivSnapshot { get; set; }
        public LeagueRank? PeakRankSnapshot { get; set; }
        public int? PeakDivSnapshot { get; set; }

        // Stats, all optional and entered in stages (spec section 14).
        public int? Kills { get; set; }
        public int? Deaths { get; set; }
        public int? Assists { get; set; }
        public int? ChampionId { get; set; }
        public LeagueChampion? Champion { get; set; }
        public LeagueLane? PlayedLane { get; set; }
        public int? Cs { get; set; }
        public int? DamageDealt { get; set; }
        public int? DamageTaken { get; set; }
        public int? Gold { get; set; }
        public int? VisionScore { get; set; }
        public int? ObjectiveDamage { get; set; }
        public string? Notes { get; set; }
        // Anything added later without a migration.
        public string? ExtraJson { get; set; }
    }
}
