namespace LeagueTeamBuilder.Services
{
    // Results/export step 2: the Series MVP shown on the Clash progress image. Pure rules (no EF, no clock), covered by tests.
    // Decision: PLAN-league.md, "Step 12 - decisions", "Series MVP".
    //   The games shown are treated as ONE big match: add each player's kills, deaths and assists over those games and apply
    //   the same K/D/A formula as the match MVP (LeagueMvpRules.KdaScore) to the totals. Best wins; a tie gives nobody.
    //   A player is only counted when
    //     - they have kills, deaths and assists in EVERY game they played, and
    //     - they played at least half of the games shown (MinPlayedShare; a one-game sub cannot win the series).
    //   Not stored anywhere: it is computed again for each image from the finished games.
    //
    // Input: one list per game SHOWN (the caller passes only finished games of ONE clash team, in order). Each list holds the
    // players of our team in that game. A Clash game's "Opponents" team has no players, so it never reaches this class;
    // ClashTeamOf() below picks team 0 out of a match's players for callers that have all of them.

    // One player's totals over the games shown.
    public record SeriesMvpRow(int? ProfileId, int GamesPlayed, int Kills, int Deaths, int Assists, double Score);

    public static class LeagueSeriesMvpRules
    {
        // A player must have played at least this share of the games shown. One place to change it.
        public const double MinPlayedShare = 0.5;

        // The Clash team is team 0 of a Clash game (LeagueClashGameRules); team 1 is "Opponents" with nobody in it.
        public const int ClashTeamIndex = 0;

        // Two series scores this close are the same score (floating point noise only; scores are ratios of whole numbers).
        private const double Epsilon = 1e-9;

        // The players of our team out of every player of a match.
        public static List<MvpPlayer> ClashTeamOf(IEnumerable<MvpPlayer> players) =>
            players.Where(p => p.TeamIndex == ClashTeamIndex).ToList();

        // Did a player who played playedGames of the shownGames games play enough of them?
        public static bool PlayedEnough(int playedGames, int shownGames) =>
            shownGames > 0 && playedGames > 0 && playedGames >= shownGames * MinPlayedShare;

        // Every counted player with their totals and score, best first (ties keep the order the players first appear in).
        // Players who are not counted (too few games, or a game they played has no K/D/A) are left out.
        public static List<SeriesMvpRow> Standings(IReadOnlyList<IReadOnlyList<MvpPlayer>> games)
        {
            var shown = games.Count;
            if (shown == 0) return new List<SeriesMvpRow>();

            // A player without a profile (deleted) cannot be followed from game to game: each such row is its own
            // entry with a negative key. It can still be the best score, and then nobody is named (see Best).
            var order = new List<int>();
            var played = new Dictionary<int, int>();
            var kills = new Dictionary<int, int>();
            var deaths = new Dictionary<int, int>();
            var assists = new Dictionary<int, int>();
            var complete = new Dictionary<int, bool>();
            var profileOf = new Dictionary<int, int?>();
            var anonymous = 0;

            foreach (var game in games)
            {
                foreach (var p in game)
                {
                    var key = p.ProfileId ?? -(++anonymous);
                    if (!played.ContainsKey(key))
                    {
                        order.Add(key);
                        played[key] = 0; kills[key] = 0; deaths[key] = 0; assists[key] = 0;
                        complete[key] = true;
                        profileOf[key] = p.ProfileId;
                    }
                    played[key]++;
                    if (LeagueMvpRules.HasKda(p))
                    {
                        kills[key] += p.Kills!.Value;
                        deaths[key] += p.Deaths!.Value;
                        assists[key] += p.Assists!.Value;
                    }
                    else complete[key] = false;
                }
            }

            var rows = new List<SeriesMvpRow>();
            foreach (var key in order)
            {
                if (!complete[key] || !PlayedEnough(played[key], shown)) continue;
                var score = (kills[key] + assists[key]) / (double)Math.Max(deaths[key], 1);
                rows.Add(new SeriesMvpRow(profileOf[key], played[key], kills[key], deaths[key], assists[key], score));
            }
            return rows.OrderByDescending(r => r.Score).ToList();   // OrderBy is stable
        }

        // The Series MVP, or null: nobody is counted, the best score is shared (a tie), or the best player has no profile any more.
        public static SeriesMvpRow? Best(IReadOnlyList<IReadOnlyList<MvpPlayer>> games)
        {
            var rows = Standings(games);
            if (rows.Count == 0) return null;
            if (rows.Count > 1 && Math.Abs(rows[0].Score - rows[1].Score) < Epsilon) return null;
            return rows[0].ProfileId == null ? null : rows[0];
        }
    }
}
