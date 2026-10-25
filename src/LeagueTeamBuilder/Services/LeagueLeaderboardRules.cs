using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Step 8d pure rules for the Leaderboard page (no EF, no clock), so they are unit-testable without a database.
    // The ORDER of the full ranking is LeagueRanking (8a); this file only parses the query, counts games, and
    // filters / sorts the rows.
    public record LbRecord(int Games, int Wins, int Losses);

    // Leaderboard step L1: the average match score of one person and how many games it is made of.
    public record LbScore(int ScoredGames, double Average);

    public static class LeagueLeaderboardRules
    {
        public const int MaxSearchLength = 60;

        // Scored games a player needs before the average counts (owner: "maybe 5", final number left to Claude).
        public const int MinScoredGames = 5;

        // The sort used when the address names none: Score; but with a rank picked, Rank, so the list looks like the old one.
        public static LeagueLeaderboardSort DefaultSort(LeagueRank? rank) =>
            rank == null ? LeagueLeaderboardSort.Score : LeagueLeaderboardSort.Rank;

        // Never throws: anything unknown falls back to the default (all players, rank order).
        public static LeagueLeaderboardQuery ParseQuery(string? q, string? kind, string? rank, string? lane, string? sort)
        {
            var search = (q ?? "").Trim();
            if (search.Length > MaxSearchLength) search = search[..MaxSearchLength].Trim();

            var k = (kind ?? "").Trim().ToLowerInvariant() switch
            {
                "members" or "member" or "users" or "user" => LeagueLeaderboardKind.Members,
                "guests" or "guest" => LeagueLeaderboardKind.Guests,
                _ => LeagueLeaderboardKind.All,
            };

            LeagueRank? r = null;
            if (int.TryParse(rank, out var ri) && Enum.IsDefined(typeof(LeagueRank), ri)) r = (LeagueRank)ri;

            LeagueLane? l = null;
            var laneText = (lane ?? "").Trim();
            if (laneText.Length > 0 && !int.TryParse(laneText, out _)
                && Enum.TryParse<LeagueLane>(laneText, true, out var parsedLane) && Enum.IsDefined(typeof(LeagueLane), parsedLane))
                l = parsedLane;

            // A named sort always wins. Blank or unknown = not chosen = the default (Score, or Rank when a rank is picked).
            LeagueLeaderboardSort? chosen = (sort ?? "").Trim().ToLowerInvariant() switch
            {
                "rank" => LeagueLeaderboardSort.Rank,
                "score" => LeagueLeaderboardSort.Score,
                "mvps" or "mvp" => LeagueLeaderboardSort.Mvps,
                "clashmvps" or "clashmvp" or "clash" => LeagueLeaderboardSort.ClashMvps,
                "games" => LeagueLeaderboardSort.Games,
                "winrate" or "wr" => LeagueLeaderboardSort.WinRate,
                _ => null,
            };
            var s = chosen ?? DefaultSort(r);

            return new LeagueLeaderboardQuery { Search = search, Kind = k, Rank = r, Lane = l, Sort = s };
        }

        // Values for the query string (they round-trip through ParseQuery).
        public static string LaneKey(LeagueLane lane) => lane.ToString().ToLowerInvariant();

        public static string SortKey(LeagueLeaderboardSort sort) => sort switch
        {
            LeagueLeaderboardSort.Mvps => "mvps",
            LeagueLeaderboardSort.ClashMvps => "clashmvps",
            LeagueLeaderboardSort.Games => "games",
            LeagueLeaderboardSort.WinRate => "winrate",
            LeagueLeaderboardSort.Score => "score",
            _ => "rank",
        };

        public static string KindKey(LeagueLeaderboardKind kind) => kind switch
        {
            LeagueLeaderboardKind.Members => "members",
            LeagueLeaderboardKind.Guests => "guests",
            _ => "all",
        };

        // Games, wins and losses per profile over FINISHED matches (the caller passes only status Done), custom and
        // Clash together, the same way "My league stats" counts (Step 5d): a match with no recorded winner is a game
        // but neither a win nor a loss. One row per person per match; rows with no profile count for nobody.
        public static Dictionary<int, LbRecord> Records(IEnumerable<OvMatch> done, IEnumerable<OvRow> rows)
        {
            var winners = done.ToDictionary(m => m.Id, m => m.WinnerTeamIndex);
            var result = new Dictionary<int, LbRecord>();
            foreach (var g in rows.Where(r => r.ProfileId != null && winners.ContainsKey(r.MatchId))
                         .GroupBy(r => r.ProfileId!.Value))
            {
                int games = 0, wins = 0, losses = 0;
                foreach (var m in g.GroupBy(r => r.MatchId))
                {
                    games++;
                    var winner = winners[m.Key];
                    if (winner == null) continue;
                    if (m.First().TeamIndex == winner) wins++; else losses++;
                }
                result[g.Key] = new LbRecord(games, wins, losses);
            }
            return result;
        }

        // Average match score per profile. `draftMatches` = the players of each finished custom Normal Draft match (one list per
        // match, both teams). Each team is scored on its own with LeagueMvpScoring.Score (Draft weights, lane = the MvpPlayer lane);
        // a team with a missing stat or fewer than 2 players gives no score, so that game adds nothing for its players (it is still a
        // game and a win or loss elsewhere). A person counts at most once per match. Rows with no profile count for nobody.
        // Profiles with no scored game are not in the result.
        public static Dictionary<int, LbScore> AverageScores(IEnumerable<IReadOnlyList<MvpPlayer>> draftMatches)
        {
            var sums = new Dictionary<int, (int Games, double Sum)>();
            foreach (var match in draftMatches)
            {
                var counted = new HashSet<int>();
                foreach (var team in match.GroupBy(p => p.TeamIndex))
                {
                    var players = team.ToList();
                    var scores = LeagueMvpScoring.Score(players, MvpScoringProfile.Draft);
                    if (scores == null) continue;
                    for (var i = 0; i < players.Count; i++)
                    {
                        if (players[i].ProfileId is not int id || !counted.Add(id)) continue;
                        var cur = sums.GetValueOrDefault(id);
                        sums[id] = (cur.Games + 1, cur.Sum + scores[i].Score);
                    }
                }
            }
            return sums.ToDictionary(kv => kv.Key, kv => new LbScore(kv.Value.Games, kv.Value.Sum / kv.Value.Games));
        }

        // Whole percent of decided games won; null when there is nothing decided.
        public static int? WinRatePercent(int wins, int losses)
        {
            var total = wins + losses;
            return total <= 0 ? null : (int)Math.Round(wins * 100.0 / total, MidpointRounding.AwayFromZero);
        }

        // "Main lanes" = the pyramid's Mains (level 4-5), best first. With none (for example only a level-3 lane),
        // the profile's own main lane is shown so the column is never empty.
        public static List<LeagueLane> MainLanes(IEnumerable<LaneLevel> rows, LeagueLane fallback)
        {
            var mains = LeagueLaneLevels.Pyramid(rows)[LeagueLaneTier.Mains].Select(r => r.Lane).ToList();
            return mains.Count > 0 ? mains : new List<LeagueLane> { fallback };
        }

        // Filter, then sort. Rows must already carry their Position from the full ranking. Every sort falls back to
        // the ranking order (Position, then name), so equal numbers keep the order people expect.
        public static List<LeagueLeaderboardRow> Apply(IEnumerable<LeagueLeaderboardRow> all, LeagueLeaderboardQuery query)
        {
            var rows = all.Where(r =>
                (query.Kind == LeagueLeaderboardKind.All
                    || (query.Kind == LeagueLeaderboardKind.Guests) == r.IsGuest)
                && (query.Rank == null || r.CurTier == query.Rank)
                && (query.Lane == null || r.Mains.Contains(query.Lane.Value))
                && (query.Search.Length == 0 || r.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase)));

            IOrderedEnumerable<LeagueLeaderboardRow> ordered = query.Sort switch
            {
                // Players with enough scored games first, best average first. The rest follow, most games played first.
                LeagueLeaderboardSort.Score => rows.OrderByDescending(r => r.HasScore)
                    .ThenByDescending(r => r.HasScore ? r.AvgScore!.Value : 0)
                    .ThenByDescending(r => r.HasScore ? 0 : r.Games),
                LeagueLeaderboardSort.Mvps => rows.OrderByDescending(r => r.Mvps),
                LeagueLeaderboardSort.ClashMvps => rows.OrderByDescending(r => r.ClashMvps),
                LeagueLeaderboardSort.Games => rows.OrderByDescending(r => r.Games),
                // People with a decided game first, then the higher rate, then more decided games.
                LeagueLeaderboardSort.WinRate => rows.OrderByDescending(r => r.Decided > 0)
                    .ThenByDescending(r => r.Decided > 0 ? (double)r.Wins / r.Decided : 0).ThenByDescending(r => r.Decided),
                _ => rows.OrderBy(r => 0),
            };
            return ordered.ThenBy(r => r.Position)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Id)
                .ToList();
        }
    }
}
