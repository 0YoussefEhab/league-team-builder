using System.Globalization;
using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Results/export step 12d: the data behind the "Clash progress" picture (spec section 18.2, image 3). Pure (no EF, no clock).
    // Decisions: PLAN-league.md, "Step 12 - decisions" and "Step 12d - decisions".
    //   One picture is one clash TEAM: its finished games, by the team's own game number (1..MaxGames). A game that is
    //   saved but not finished, or a number with no game, is a dashed "Not played yet" column.
    //   Each column lists the five players of OUR team (team index 0; the Opponents team has nobody).
    //   Game MVP highlight = the stored all-stats pick (StatsMvpProfileId when the game was won, StatsTeamMvpProfileId when it was lost),
    //   else the K/D/A pick (MvpProfileId / TeamMvpProfileId) when no all-stats pick exists (the pick the History popup shows). No pick, or a pick who is not on the team, = nothing highlighted. Nothing is guessed here.
    //   Series MVP = LeagueSeriesMvpRules.Best over the games shown (kills, deaths and assists added up, same K/D/A formula).
    public static class LeagueClashProgressRules
    {
        // Lane chips on the picture (Normal Draft only).
        public static string LaneChip(LeagueLane lane) => lane switch
        {
            LeagueLane.Top => "TOP",
            LeagueLane.Jungle => "JGL",
            LeagueLane.Mid => "MID",
            LeagueLane.Adc => "ADC",
            _ => "SUP",
        };

        // "Sat 24 Oct": the clash day in Cairo.
        public static string WhenText(DateTime startsAtUtc) =>
            LeagueDayRules.ToCairo(startsAtUtc).ToString("ddd d MMM", CultureInfo.InvariantCulture);

        // A game counts when it is Done, has a winner, a number 1..MaxGames, and a Clash team (index 0).
        public static bool IsShown(LeagueMatch m) =>
            m.Status == LeagueMatchStatus.Done
            && m.WinnerTeamIndex != null
            && m.GameNumber is int n && n >= 1 && n <= LeagueClashGameRules.MaxGames
            && m.Teams.Any(t => t.Index == LeagueSeriesMvpRules.ClashTeamIndex);

        // games = the saved games of ONE clash team (any status, any order). Null when no game is finished yet:
        // there is nothing to show (the button is for "after game 1").
        public static ClashProgressSpec? Build(LeagueClashEvent ev, int teamId, string teamName, IEnumerable<LeagueMatch> games)
        {
            // One finished game per number (the newest row if two ever share a number).
            var shown = games.Where(IsShown)
                .GroupBy(m => m.GameNumber!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Id).Last());
            if (shown.Count == 0) return null;

            var hasLanes = ev.Mode == LeagueMode.Normal;
            var columns = new List<ClashProgressGame?>();
            var forMvp = new List<IReadOnlyList<MvpPlayer>>();
            var lastNameOf = new Dictionary<int, string>();
            int wins = 0, losses = 0;

            for (var number = 1; number <= LeagueClashGameRules.MaxGames; number++)
            {
                if (!shown.TryGetValue(number, out var m)) { columns.Add(null); continue; }

                var won = m.WinnerTeamIndex == LeagueSeriesMvpRules.ClashTeamIndex;
                if (won) wins++; else losses++;

                var team = m.Teams.First(t => t.Index == LeagueSeriesMvpRules.ClashTeamIndex);
                var ordered = hasLanes
                    ? team.Players.OrderBy(p => p.Lane == null ? 99 : (int)p.Lane.Value).ThenBy(p => p.Id).ToList()
                    : team.Players.OrderBy(p => p.Id).ToList();

                var pick = won ? LeagueResultImageRules.ShownPick(m.StatsMvpProfileId, m.MvpProfileId) : LeagueResultImageRules.ShownPick(m.StatsTeamMvpProfileId, m.TeamMvpProfileId);
                var badged = false;
                var players = ordered.Select(p =>
                {
                    // One highlight per column, even if the same profile somehow appears twice.
                    var mvp = !badged && pick != null && p.ProfileId == pick;
                    if (mvp) badged = true;
                    if (p.ProfileId is int pid) lastNameOf[pid] = p.NameSnapshot;
                    return new ClashProgressPlayer(
                        p.NameSnapshot, hasLanes && p.Lane is LeagueLane l ? LaneChip(l) : null,
                        p.WasGuest, p.Kills, p.Deaths, p.Assists, mvp);
                }).ToList();
                columns.Add(new ClashProgressGame(number, won, players));

                forMvp.Add(team.Players.Select(p => new MvpPlayer(
                    p.ProfileId, LeagueSeriesMvpRules.ClashTeamIndex, p.Lane, p.Kills, p.Deaths, p.Assists,
                    p.Cs, p.DamageDealt, p.DamageTaken, p.Gold, p.VisionScore, p.ObjectiveDamage)).ToList());
            }

            var best = LeagueSeriesMvpRules.Best(forMvp);
            string? mvpName = best?.ProfileId is int bid && lastNameOf.TryGetValue(bid, out var nm) ? nm : null;

            return new ClashProgressSpec(
                ev.Id, teamId, ev.Title, teamName, WhenText(ev.StartsAtUtc), hasLanes ? "normal" : "aram", hasLanes,
                wins, losses, wins + losses, LeagueClashGameRules.MaxGames, columns,
                mvpName, mvpName == null ? 0 : best!.GamesPlayed);
        }
    }
}
