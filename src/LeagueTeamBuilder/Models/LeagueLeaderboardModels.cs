using LeagueTeamBuilder.Services;

namespace LeagueTeamBuilder.Models
{
    // Step 8d: the Leaderboard page (GET /League/Leaderboard). Data: LeagueOverviewService.GetLeaderboardAsync,
    // filter and sort rules: LeagueLeaderboardRules. Decisions: PLAN-league.md, "Step 8d - decisions".

    public enum LeagueLeaderboardKind { All = 0, Members = 1, Guests = 2 }
    // MVP redesign Step 5c: ClashMvps added (sort by Clash MVPs). Leaderboard step L1: Score added (average match score of custom
    // draft games); it is the default sort, see LeagueLeaderboardRules.DefaultSort.
    public enum LeagueLeaderboardSort { Rank = 0, Mvps = 1, Games = 2, WinRate = 3, ClashMvps = 4, Score = 5 }

    // What the visitor asked for (query string). Built by LeagueLeaderboardRules.ParseQuery, which never throws.
    public class LeagueLeaderboardQuery
    {
        public string Search { get; set; } = "";
        public LeagueLeaderboardKind Kind { get; set; } = LeagueLeaderboardKind.All;
        // Filter on the CURRENT rank tier (null = all ranks).
        public LeagueRank? Rank { get; set; }
        // Filter on a main lane (null = any).
        public LeagueLane? Lane { get; set; }
        public LeagueLeaderboardSort Sort { get; set; } = LeagueLeaderboardSort.Score;

        public bool HasFilter => Search.Length > 0 || Kind != LeagueLeaderboardKind.All || Rank != null || Lane != null
            || Sort != LeagueLeaderboardSort.Score;
    }

    // One player (member or guest) on the leaderboard.
    public class LeagueLeaderboardRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsGuest { get; set; }
        public string? AvatarUrl { get; set; }
        public LeagueRank CurTier { get; set; }
        public int? CurDiv { get; set; }
        public LeagueRank PeakTier { get; set; }
        public int? PeakDiv { get; set; }

        // Place in the FULL ranking (LeagueRanking: current rank, peak rank, MVPs, name). Full ties share a number.
        // It does not change when the list is filtered or sorted another way.
        public int Position { get; set; }

        // MVP redesign Step 5b/5c: MVPs in CUSTOM DRAFT matches (the counted match MVP, one credit per match). The main number.
        public int Mvps { get; set; }
        // MVPs in Clash games (Match MVP of a won game, Team MVP of a lost one). Its own number, never added to Mvps.
        public int ClashMvps { get; set; }

        // MVP redesign Step 5c: finished CUSTOM DRAFT matches only (Clash and custom ARAM/Arena are not in these three).
        // Wins + Losses are the ones with a recorded winner.
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }

        // Step 8g: holds the #1 rank badge / the MVP badge (LeagueBadges).
        public bool RankKing { get; set; }
        public bool MvpKing { get; set; }
        // Step 13: holds the Clash winner badge (the players of the best Clash team on the Overview).
        public bool ClashKing { get; set; }

        // Lanes at level 4-5 (the pyramid's "Mains"), best first; the profile's main lane when there are none.
        public List<LeagueLane> Mains { get; set; } = new();

        // Leaderboard step L1: the average all-stats match score (LeagueMvpScoring.Score, 1.00 = an average teammate) over the
        // custom draft games where the player's whole team has full stats. Worked out live, nothing is stored.
        // ScoredGames = how many games that is. AvgScore is null when there is none. It is only SHOWN and ranked first when
        // HasScore (at least LeagueLeaderboardRules.MinScoredGames scored games).
        public double? AvgScore { get; set; }
        public int ScoredGames { get; set; }
        public bool HasScore => AvgScore != null && ScoredGames >= LeagueLeaderboardRules.MinScoredGames;
        // Scored games still missing before the score counts; 0 once HasScore.
        public int GamesToScore => Math.Max(0, LeagueLeaderboardRules.MinScoredGames - ScoredGames);

        public int Decided => Wins + Losses;
        // Whole percent, null when no finished match has a recorded winner.
        public int? WinRate => LeagueLeaderboardRules.WinRatePercent(Wins, Losses);
    }

    public class LeagueLeaderboardModel
    {
        public LeagueLeaderboardQuery Query { get; set; } = new();
        // Filtered and sorted.
        public List<LeagueLeaderboardRow> Rows { get; set; } = new();
        // Everybody, before the filters (so the page can say "3 of 12 players").
        public int TotalPlayers { get; set; }
    }
}
