using LeagueTeamBuilder.Services;

namespace LeagueTeamBuilder.Models
{
    // Step 8e: the Player page (GET /League/Player/{id}). Data: LeagueOverviewService.GetPlayerAsync (plus
    // LeagueHistoryService.GetPlayerStatsAsync for the Step 5d numbers). Decisions: PLAN-league.md, "Step 8e - decisions".
    public class LeaguePlayerModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsGuest { get; set; }
        public string? AvatarUrl { get; set; }
        public LeagueRank CurTier { get; set; }
        public int? CurDiv { get; set; }
        public LeagueRank PeakTier { get; set; }
        public int? PeakDiv { get; set; }

        // Place in the FULL ranking (LeagueRanking: current rank, peak rank, MVPs, name; full ties share a number)
        // and how many players there are.
        public int Position { get; set; }
        public int TotalPlayers { get; set; }

        // MVP redesign Step 5d: MVPs in CUSTOM DRAFT matches (one credit per match). The Clash and ARAM/Arena numbers are in Stats.Buckets.
        public int Mvps { get; set; }

        // Finished CUSTOM DRAFT matches only, the same numbers as the Leaderboard.
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int? WinRate => LeagueLeaderboardRules.WinRatePercent(Wins, Losses);

        // Step 8g: holds the #1 rank badge / the MVP badge (LeagueBadges).
        public bool RankKing { get; set; }
        public bool MvpKing { get; set; }
        // Step 13: holds the Clash winner badge (the players of the best Clash team on the Overview).
        public bool ClashKing { get; set; }

        // The five lanes in the three pyramid tiers (a lane with no row is level 1).
        public Dictionary<LeagueLaneTier, List<LaneLevel>> Pyramid { get; set; } = new();

        // The Step 5d numbers (K/D/A, champions, lanes played, last results). Filled by the controller.
        public LeaguePlayerStats Stats { get; set; } = new();

        // True for a group that has LeagueSettings > EditPlayerRank. Filled by the controller.
        public bool CanEditRank { get; set; }

        // True for a group that has LeagueSettings > EditPlayerNames (names form on the Player page). Filled by the controller.
        public bool CanEditNames { get; set; }

        // The Riot ID (Name#TAG), only read to prefill the names form. Filled by the controller for a group that can edit names.
        public string? RiotId { get; set; }
    }

    // POST /LeagueSettings/EditPlayerRank. Field names match the _RankSelect partial (CurTier/CurDiv, PeakTier/PeakDiv).
    public class LeagueRankEditRequest
    {
        public int ProfileId { get; set; }
        public LeagueRank CurTier { get; set; }
        public int? CurDiv { get; set; }
        public LeagueRank PeakTier { get; set; }
        public int? PeakDiv { get; set; }
    }
}
