using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Results/export step 12e: the data behind the "Session collection" picture (spec section 18.2, image 4). Pure (no EF, no clock).
    // Decisions: PLAN-league.md, "Step 12 - decisions" and "Step 12e - decisions".
    //   A session picture shows up to GamesPerImage finished CUSTOM games of one Cairo day, in PlayedUtc order. With more games
    //   on the day the picture is split into blocks of four (games 1-4, 5-8, ...). Which games belong together (the 70% rule)
    //   is Step 12f; here the caller passes the games.
    //   Sides: "Team 1" / "Team 2" are the two teams of the FIRST game of the block. When the teams were reshuffled, a later game
    //   is lined up by shared players, so the record counts wins for the same group of people (see SideOf).
    //   Game MVP = the all-stats pick (StatsMvpProfileId, else MvpProfileId) on the winning team, Team MVP = StatsTeamMvpProfileId (else TeamMvpProfileId) on the losing team (stored picks,
    //   a pick who is not on that team is ignored, nothing is guessed). Best performance of the session = the game MVP with the
    //   best K/D/A score (LeagueMvpRules.KdaScore); a shared best score, or no game MVP with full K/D/A, = no card.
    public static class LeagueSessionImageRules
    {
        public const int GamesPerImage = 4;

        // A game can be in a session picture: finished, Custom, a winner, exactly two teams that both have players.
        public static bool IsEligible(LeagueMatch m)
        {
            if (m.Kind != LeagueMatchKind.Custom || m.Status != LeagueMatchStatus.Done) return false;
            if (m.WinnerTeamIndex is not int winner) return false;
            var teams = m.Teams.ToList();
            return teams.Count == 2 && teams.All(t => t.Players.Count > 0) && teams.Any(t => t.Index == winner);
        }

        // Eligible games in play order (PlayedUtc, then id).
        public static List<LeagueMatch> Order(IEnumerable<LeagueMatch> matches) =>
            matches.Where(IsEligible).OrderBy(m => m.PlayedUtc).ThenBy(m => m.Id).ToList();

        // The block of up to GamesPerImage games that holds matchId (games 1-4, 5-8, ...). Null when that match is not eligible.
        // Offset = how many games come before the block; Total = all eligible games passed in.
        public static (List<LeagueMatch> Block, int Offset, int Total)? BlockOf(int matchId, IEnumerable<LeagueMatch> dayMatches)
        {
            var all = Order(dayMatches);
            var i = all.FindIndex(m => m.Id == matchId);
            if (i < 0) return null;
            var offset = i / GamesPerImage * GamesPerImage;
            return (all.Skip(offset).Take(GamesPerImage).ToList(), offset, all.Count);
        }

        // Who a player is across games: the profile when there is one, else the name (a guest).
        public static string PlayerKey(LeagueMatchPlayer p) =>
            p.ProfileId is int id ? "p" + id : "n" + (p.NameSnapshot ?? "").Trim().ToLowerInvariant();

        // True when the SECOND team shares more players with side A (the first game's first team) than the FIRST team does.
        // An equal count keeps the slot order.
        public static bool IsFlipped(ISet<string> sideA, LeagueMatchTeam first, LeagueMatchTeam second) =>
            Shared(sideA, second) > Shared(sideA, first);

        private static int Shared(ISet<string> sideA, LeagueMatchTeam t) =>
            t.Players.Select(PlayerKey).Distinct().Count(sideA.Contains);

        // The kills typed for the team, else the sum of the players' kills when anybody has kills, else unknown.
        public static int? KillsOf(LeagueMatch m, LeagueMatchTeam t)
        {
            var typed = t.Index == 0 ? m.TeamKills0 : t.Index == 1 ? m.TeamKills1 : null;
            if (typed != null) return typed;
            var known = t.Players.Where(p => p.Kills != null).ToList();
            return known.Count == 0 ? null : known.Sum(p => p.Kills!.Value);
        }

        private static SessionImagePlayer? Pick(LeagueMatchTeam team, int? profileId)
        {
            if (profileId == null) return null;
            var p = team.Players.FirstOrDefault(x => x.ProfileId == profileId);
            if (p == null) return null;
            return new SessionImagePlayer(
                p.NameSnapshot, p.Lane is LeagueLane l ? LeagueClashProgressRules.LaneChip(l) : null, p.Kills, p.Deaths, p.Assists);
        }

        // block = the games of one picture in play order (from BlockOf). offset = games before the block, total = games that day.
        // Null when the block is empty or holds a game that is not eligible.
        public static SessionImageSpec? Build(int matchId, IReadOnlyList<LeagueMatch> block, int offset, int total) =>
            Build(matchId, block, Enumerable.Range(0, block.Count).Select(i => offset + i + 1).ToList(), total);

        // Step 12f: numbers = each game's number in the DAY, so a ticked selection keeps the gaps (Game 1, Game 3, Game 4).
        // Null when numbers and games do not line up, the block is empty, or it holds a game that is not eligible.
        public static SessionImageSpec? Build(int matchId, IReadOnlyList<LeagueMatch> block, IReadOnlyList<int> numbers, int total)
        {
            if (numbers.Count != block.Count) return null;
            if (block.Count == 0 || block.Any(m => !IsEligible(m))) return null;

            var firstTeams = block[0].Teams.OrderBy(t => t.Index).ToList();
            var sideA = firstTeams[0].Players.Select(PlayerKey).ToHashSet();
            var games = new List<SessionImageGame>();
            var candidates = new List<(string Name, int K, int D, int A, int Number, double Score)>();
            int winsA = 0, winsB = 0;

            for (var i = 0; i < block.Count; i++)
            {
                var m = block[i];
                var teams = m.Teams.OrderBy(t => t.Index).ToList();
                var flip = i > 0 && IsFlipped(sideA, teams[0], teams[1]);
                var a = flip ? teams[1] : teams[0];
                var b = flip ? teams[0] : teams[1];
                var winnerSide = m.WinnerTeamIndex == a.Index ? 0 : 1;
                if (winnerSide == 0) winsA++; else winsB++;

                var winning = winnerSide == 0 ? a : b;
                var losing = winnerSide == 0 ? b : a;
                var mvp = Pick(winning, LeagueResultImageRules.ShownPick(m.StatsMvpProfileId, m.MvpProfileId));
                var teamMvp = Pick(losing, LeagueResultImageRules.ShownPick(m.StatsTeamMvpProfileId, m.TeamMvpProfileId));

                var kA = KillsOf(m, a);
                var kB = KillsOf(m, b);
                var number = numbers[i];
                games.Add(new SessionImageGame(number, m.ModeName, winnerSide, kA != null && kB != null ? kA : null, kA != null && kB != null ? kB : null, mvp, teamMvp));

                if (mvp is { Kills: int k, Deaths: int d, Assists: int ast })
                {
                    var score = LeagueMvpRules.KdaScore(new MvpPlayer(null, 0, null, k, d, ast, null, null, null, null, null, null));
                    if (score != null) candidates.Add((mvp.Name, k, d, ast, number, score.Value));
                }
            }

            SessionImageBest? best = null;
            if (candidates.Count > 0)
            {
                var top = candidates.Max(c => c.Score);
                var tied = candidates.Where(c => Math.Abs(c.Score - top) < 1e-9).ToList();
                if (tied.Count == 1) best = new SessionImageBest(tied[0].Name, tied[0].K, tied[0].D, tied[0].A, tied[0].Number);
            }

            var first = block[0];
            var hasLanes = first.Teams.SelectMany(t => t.Players).Any(p => p.Lane != null);
            return new SessionImageSpec(
                matchId, LeagueResultImageRules.DateText(first.PlayedUtc), hasLanes ? "normal" : "aram", hasLanes,
                winsA, winsB, numbers[0], total, games, best);
        }
    }
}
