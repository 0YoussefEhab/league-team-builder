namespace LeagueTeamBuilder.Models
{
    // Step 8b: everything the League Overview page (8c) shows, built by LeagueOverviewService.
    // Picture rule (owner): show AvatarUrl when it exists, else the coloured initial (the page decides).

    // A person in the top-3 ranked cards or the top-3 MVP cards (users and guests together).
    public class LeagueOverviewPlayer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsGuest { get; set; }
        public string? AvatarUrl { get; set; }
        public LeagueRank CurTier { get; set; }
        public int? CurDiv { get; set; }
        public LeagueRank PeakTier { get; set; }
        public int? PeakDiv { get; set; }
        // Custom matches where this person holds a match MVP (either score, one credit per match).
        public int MvpCount { get; set; }
        // Ranked cards: place by LeagueRanking (full ties share a number). MVP cards: 1, 2, 3 by MVP count.
        public int Position { get; set; }
        // Step 8g: holds the #1 rank badge / the MVP badge (LeagueBadges).
        public bool RankKing { get; set; }
        public bool MvpKing { get; set; }
        // Step 13: holds the Clash winner badge (the players of the best Clash team on the Overview).
        public bool ClashKing { get; set; }
    }

    // One of the five tiles of the best Clash team. Snapshot data when the profile was deleted (ProfileId null).
    public class LeagueOverviewClashPlayer
    {
        public int? ProfileId { get; set; }
        public string Name { get; set; } = "";
        public bool IsGuest { get; set; }
        public string? AvatarUrl { get; set; }
        // Null in an ARAM Clash (no lanes).
        public LeagueLane? Lane { get; set; }
        public LeagueRank? Tier { get; set; }
        public int? Div { get; set; }
    }

    // The most recent finished Clash we won.
    public class LeagueOverviewClash
    {
        public int EventId { get; set; }
        public string Title { get; set; } = "";
        public DateTime StartsAtUtc { get; set; }
        // Games the Clash team won / games finished in that Clash ("Won N games").
        public int Wins { get; set; }
        public int Games { get; set; }
        // Lane order (Top, Jungle, Mid, ADC, Support); saved order when there are no lanes.
        public List<LeagueOverviewClashPlayer> Players { get; set; } = new();
    }

    // A fun fact that names a person ("Longest win streak", "Most active player"). ProfileId opens the profile (8e).
    public class LeagueOverviewPersonFact
    {
        public int ProfileId { get; set; }
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    public class LeagueOverviewServerStats
    {
        // Games played (finished only). Customs = draft + ARAM + arena.
        public int Customs { get; set; }
        public int Draft { get; set; }
        public int Aram { get; set; }
        public int Arena { get; set; }
        public int Clashes { get; set; }
        public int ClashGames { get; set; }
        public int ClashesWon { get; set; }

        // Balance scoreboard (C3): finished custom Draft and ARAM games (two sides) won by Side A (Team 1) and Side B (Team 2).
        // Arena (many teams) and Clash (our team vs an opponent) are not part of it. Both 0 = the page hides the scoreboard.
        public int SideAWins { get; set; }
        public int SideBWins { get; set; }

        // Fun facts. Null = nothing to show yet (the page hides that tile).
        public int Players { get; set; }
        public int Members { get; set; }
        public int Guests { get; set; }
        public string? TopChampion { get; set; }
        public int TopChampionGames { get; set; }
        public LeagueLane? TopLane { get; set; }
        public int TopLanePercent { get; set; }
        public LeagueOverviewPersonFact? Streak { get; set; }
        public LeagueOverviewPersonFact? MostActive { get; set; }
        public int? AverageLengthSeconds { get; set; }
        // "27:40", ready to show.
        public string? AverageLengthText { get; set; }
    }

    public class LeagueOverviewModel
    {
        // At most 3, best first.
        public List<LeagueOverviewPlayer> TopRanked { get; set; } = new();
        // At most 3, only people with at least one MVP (so empty until the first MVP exists).
        public List<LeagueOverviewPlayer> TopMvps { get; set; } = new();
        // Null = no Clash won yet; the card says so.
        public LeagueOverviewClash? BestClash { get; set; }
        public LeagueOverviewServerStats Stats { get; set; } = new();
    }
}
