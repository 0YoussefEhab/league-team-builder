using System.Globalization;
using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Results/export step 3: the data behind the "Game result" picture (spec section 18.2, image 2). Pure (no EF, no clock).
    // Decisions: PLAN-league.md, "Step 12 - decisions".
    //   Only a finished CUSTOM match with exactly two teams that both have players gets a result picture
    //   (a Clash game has no opponent players and uses the Clash progress picture; Arena has more than two teams).
    //   Game MVP = StatsMvpProfileId (winning team), Team MVP = StatsTeamMvpProfileId (losing team): the all-stats picks, else the
    //   K/D/A picks (MvpProfileId / TeamMvpProfileId) when no all-stats pick exists (see ShownPick), automatic or hand-picked. A pick that is not on that team in this match is ignored. Nothing is guessed:
    //   no pick = no badge, and a player without K/D/A is drawn as "no KDA".
    public static class LeagueResultImageRules
    {
        public static ResultImageSpec? Build(LeagueMatchDetailModel d)
        {
            if (d.IsClash || d.Status != "done" || d.Teams.Count != 2 || d.Teams.Any(t => t.IsEmpty)) return null;
            if (d.WinnerIndex is not int winner || d.Teams.All(t => t.Index != winner)) return null;

            var teams = d.Teams.OrderBy(t => t.Index).Select(t =>
            {
                var won = t.Index == winner;
                var pick = won ? ShownPick(d.StatsMvpId, d.MvpId) : ShownPick(d.StatsTeamMvpId, d.TeamMvpId);
                var badged = false;
                var players = t.Players.Select(p =>
                {
                    // One badge per team, even if the same profile somehow appears twice.
                    var badge = !badged && pick != null && p.ProfileId == pick ? (won ? "mvp" : "team") : null;
                    if (badge != null) badged = true;
                    return new ResultImagePlayer(p.Name, d.HasLanes ? p.LaneLabel : null, p.IsGuest, p.Kills, p.Deaths, p.Assists, badge);
                }).ToList();
                return new ResultImageTeam(t.Index, t.Name, won, TeamKills(t, players), players);
            }).ToList();

            return new ResultImageSpec(
                d.Id, d.ModeName, DateText(d.PlayedUtc), d.HasLanes ? "normal" : "aram", d.HasLanes, winner, teams);
        }

        // Owner, 2026-10-19: the pictures show the all-stats MVP. Only when there is no all-stats pick (a stat is missing)
        // the K/D/A pick is used instead, so the picture never has an empty MVP when one exists. Shared by the session and Clash pictures.
        public static int? ShownPick(int? statsPick, int? kdaPick) => statsPick ?? kdaPick;

        // The kills the owner typed for the team, else the sum of the players' kills when anybody has kills, else unknown.
        public static int? TeamKills(LeagueMatchDetailTeam t, IReadOnlyList<ResultImagePlayer> players)
        {
            if (t.Kills != null) return t.Kills;
            var known = players.Where(p => p.Kills != null).ToList();
            return known.Count == 0 ? null : known.Sum(p => p.Kills!.Value);
        }

        // "17 Oct 2026", the Played-on day in Cairo.
        public static string DateText(DateTime playedUtc) =>
            LeagueDayRules.ToCairo(playedUtc).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
