using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Step 8a2: who is the MVP of a finished match. Pure rules (no EF, no clock), covered by tests.
    // Decisions: PLAN-league.md, "Step 8 - MVP design" and "Step 8 - all-stats score: research and weights".
    //   Match MVP     = best player on the WINNING team.   Team MVP = best player on the LOSING team (2-team matches only).
    //   Two scores: the K/D/A score and the all-stats score, so up to four picks per match.
    //   Nothing is ever set unless the stats the score needs are filled for EVERY player of that team.

    // One player of a match as the MVP rules see them.
    public record MvpPlayer(
        int? ProfileId, int TeamIndex, LeagueLane? Lane,
        int? Kills, int? Deaths, int? Assists,
        int? Cs, int? DamageDealt, int? DamageTaken, int? Gold, int? Vision, int? ObjectiveDamage);

    // The four picks (profile ids). Null = nobody (not enough stats, a tie, or a deleted profile).
    public record MvpPicks(int? Mvp, int? TeamMvp, int? StatsMvp, int? StatsTeamMvp)
    {
        public static readonly MvpPicks None = new(null, null, null, null);

        // Owner, 2026-10-19: the match MVP that COUNTS is the all-stats pick; the K/D/A pick counts only when the game has no
        // all-stats pick. So when stats are in and the two picks are different people, the K/D/A person gets no credit.
        public int? CountedMvp => StatsMvp ?? Mvp;

        // MVP redesign Step 5a: the same rule for the losing side's pick. Only a Clash game uses it (our team lost, so our best
        // player holds the Team MVP); a custom game's Team MVP never counts anywhere.
        public int? CountedTeamMvp => StatsTeamMvp ?? TeamMvp;
    }

    public static class LeagueMvpRules
    {
        // OLD tie rule, only for games without a scoring profile (an unknown ModeKey; ARAM and Arena have one since Step 4): two all-stats scores closer
        // than this are a tie, no automatic MVP, an admin picks.
        public const double StatsTieMargin = 0.01;

        // MVP redesign Step 3, for games WITH a scoring profile: players within this of the best score are tied. The tie is
        // settled by kill participation, then fewer deaths; only if that still ties is nobody named and an admin picks.
        public const double TieMargin = 0.02;
        private const double TieEpsilon = 1e-9;   // floating point noise only

        // OLD all-stats formula (AllStatsScores, with the weights and Role() below). Since MVP redesign Step 2 it is only used for
        // games without a scoring profile (an unknown ModeKey); every real game now uses LeagueMvpScoring (Draft or ARAM profile).
        // It can be deleted later. Base weights (sum 100). Tunable in one place.
        public const double WKda = 25, WParticipation = 15, WDamage = 20, WGold = 10, WCs = 10, WVision = 8, WObjective = 8, WTaken = 4;

        public static bool HasKda(MvpPlayer p) => p.Kills != null && p.Deaths != null && p.Assists != null;

        public static bool HasAllStats(MvpPlayer p) =>
            HasKda(p) && p.Cs != null && p.DamageDealt != null && p.DamageTaken != null
            && p.Gold != null && p.Vision != null && p.ObjectiveDamage != null;

        // (kills + assists) / max(deaths, 1). Null until kills, deaths and assists are all filled.
        public static double? KdaScore(MvpPlayer p) =>
            HasKda(p) ? (p.Kills!.Value + p.Assists!.Value) / (double)Math.Max(p.Deaths!.Value, 1) : null;

        // Role multipliers (PlayedLane). No lane (ARAM / Arena) = all 1.
        private static (double Cs, double Dmg, double Gold, double Vision, double Obj, double Taken) Role(LeagueLane? lane) => lane switch
        {
            LeagueLane.Support => (0, 0.6, 0.6, 2, 1, 1),
            LeagueLane.Jungle => (0.5, 1, 1, 1, 1.5, 1),
            LeagueLane.Top => (1, 1, 1, 1, 1, 1.5),
            _ => (1, 1, 1, 1, 1, 1),
        };

        // All-stats score of every player of ONE team (same order as the input), or null when anyone is missing a stat.
        // Each metric = the player's share of the team divided by the fair share, so 1.00 = an average teammate.
        public static double[]? AllStatsScores(IReadOnlyList<MvpPlayer> team)
        {
            if (team.Count < 2 || !team.All(HasAllStats)) return null;
            var n = team.Count;

            double[] Share(Func<MvpPlayer, double> value)
            {
                var v = team.Select(value).ToArray();
                var total = v.Sum();
                return total <= 0 ? Array.Empty<double>() : v.Select(x => x / total * n).ToArray();
            }

            var metrics = new (double W, double[] Ratio, Func<(double Cs, double Dmg, double Gold, double Vision, double Obj, double Taken), double> Mult)[]
            {
                (WKda,           Share(p => KdaScore(p)!.Value),               _ => 1),
                (WParticipation, Share(p => p.Kills!.Value + p.Assists!.Value), _ => 1),
                (WDamage,        Share(p => p.DamageDealt!.Value),              r => r.Dmg),
                (WGold,          Share(p => p.Gold!.Value),                     r => r.Gold),
                (WCs,            Share(p => p.Cs!.Value),                       r => r.Cs),
                (WVision,        Share(p => p.Vision!.Value),                   r => r.Vision),
                (WObjective,     Share(p => p.ObjectiveDamage!.Value),          r => r.Obj),
                (WTaken,         Share(p => p.DamageTaken!.Value),              r => r.Taken),
            };

            var scores = new double[n];
            for (var i = 0; i < n; i++)
            {
                var role = Role(team[i].Lane);
                double wSum = 0, sum = 0;
                foreach (var m in metrics)
                {
                    if (m.Ratio.Length == 0) continue;          // nobody has any of this stat: skip it
                    var w = m.W * m.Mult(role);
                    wSum += w;
                    sum += w * m.Ratio[i];
                }
                scores[i] = wSum <= 0 ? 0 : sum / wSum;
            }
            return scores;
        }

        // Best K/D/A score of a team; null when anyone is missing K/D/A, the team has fewer than 2 players,
        // the best score is shared (a tie) or the best player has no profile any more.
        public static int? BestByKda(IReadOnlyList<MvpPlayer> team)
        {
            if (team.Count < 2 || !team.All(HasKda)) return null;
            var scores = team.Select(p => KdaScore(p)!.Value).ToArray();
            var best = scores.Max();
            var top = Enumerable.Range(0, team.Count).Where(i => Math.Abs(scores[i] - best) < 1e-9).ToList();
            return top.Count == 1 ? team[top[0]].ProfileId : null;
        }

        // Best all-stats score of a team; null when stats are missing, the tie-break cannot separate the contenders
        // (old formula: the top two are within StatsTieMargin), or the best player has no profile.
        // profile = the new scoring (LeagueMvpScoring, MVP redesign Step 2); null = the old formula (AllStatsScores).
        public static int? BestByAllStats(IReadOnlyList<MvpPlayer> team, MvpScoringProfile? profile = null)
        {
            var scores = profile is MvpScoringProfile sp
                ? LeagueMvpScoring.Score(team, sp)?.Select(x => x.Score).ToArray()
                : AllStatsScores(team);
            if (scores == null) return null;
            var order = Enumerable.Range(0, team.Count).OrderByDescending(i => scores[i]).ToList();

            // Old formula (no profile): the old rule, unchanged.
            if (profile == null)
            {
                if (scores[order[0]] - scores[order[1]] < StatsTieMargin) return null;
                return team[order[0]].ProfileId;
            }

            // New scoring: everybody within TieMargin of the best score is a contender.
            var best = scores[order[0]];
            var contenders = order.Where(i => best - scores[i] <= TieMargin + TieEpsilon).ToList();
            return contenders.Count == 1 ? team[contenders[0]].ProfileId : BreakTie(team, contenders);
        }

        // Kill participation = (kills + assists) / the team's kills (the sum of its players' kills). Null when the team has no
        // kills or the player has no kills / assists filled in. Also what the Step 7 breakdown will show.
        public static double? KillParticipation(IReadOnlyList<MvpPlayer> team, MvpPlayer p)
        {
            if (p.Kills == null || p.Assists == null) return null;
            var teamKills = team.Sum(x => x.Kills ?? 0);
            return teamKills <= 0 ? null : (p.Kills.Value + p.Assists.Value) / (double)teamKills;
        }

        // The tie-break: higher kill participation, then fewer deaths. Contenders are on the same team, so the team's kills are
        // the same for all of them and the higher (kills + assists) is the higher kill participation (compared as whole
        // numbers, no rounding). Still level after both: null, an admin picks.
        private static int? BreakTie(IReadOnlyList<MvpPlayer> team, List<int> contenders)
        {
            var ranked = contenders
                .OrderByDescending(i => team[i].Kills!.Value + team[i].Assists!.Value)
                .ThenBy(i => team[i].Deaths!.Value)
                .ToList();
            MvpPlayer first = team[ranked[0]], second = team[ranked[1]];
            var level = first.Kills!.Value + first.Assists!.Value == second.Kills!.Value + second.Assists!.Value
                && first.Deaths!.Value == second.Deaths!.Value;
            return level ? null : first.ProfileId;
        }

        // The four picks of a finished match. winnerIndex null (or not a team of the match) = no picks.
        // Team picks only exist for two-team matches (Arena has no team MVP).
        // clash = true (Step 12c.1): a Clash game only has OUR team (team index ClashTeamIndex; the Opponents team has nobody).
        // Our best player gets the pick whatever the result: Match MVP when our team won, Team MVP when it lost. There is never
        // an opponent pick, and the winner index may be the empty Opponents team (a lost game).
        // profile = which all-stats scoring to use (null = the old formula); see BestByAllStats.
        public static MvpPicks Compute(int? winnerIndex, IReadOnlyList<MvpPlayer> players, bool clash = false, MvpScoringProfile? profile = null)
        {
            if (winnerIndex is not int w) return MvpPicks.None;
            var teams = players.GroupBy(p => p.TeamIndex).ToDictionary(g => g.Key, g => (IReadOnlyList<MvpPlayer>)g.ToList());

            if (clash)
            {
                if (!teams.TryGetValue(LeagueSeriesMvpRules.ClashTeamIndex, out var ours)) return MvpPicks.None;
                var kda = BestByKda(ours);
                var all = BestByAllStats(ours, profile);
                return w == LeagueSeriesMvpRules.ClashTeamIndex
                    ? new MvpPicks(kda, null, all, null)
                    : new MvpPicks(null, kda, null, all);
            }

            if (!teams.TryGetValue(w, out var winners)) return MvpPicks.None;

            int? teamMvp = null, statsTeamMvp = null;
            if (teams.Count == 2)
            {
                var losers = teams.First(t => t.Key != w).Value;
                teamMvp = BestByKda(losers);
                statsTeamMvp = BestByAllStats(losers, profile);
            }
            return new MvpPicks(BestByKda(winners), teamMvp, BestByAllStats(winners, profile), statsTeamMvp);
        }

        // Step 12c.1: why the K/D/A rule names nobody for this team, in words for the person who pressed "Back to automatic".
        // Null when BestByKda would name someone. Same checks, same order as BestByKda.
        public static string? WhyNoKdaPick(string teamName, IReadOnlyList<MvpPlayer> team)
        {
            if (team.Count < 2) return $"{teamName} has fewer than 2 players.";
            var missing = team.Count(p => !HasKda(p));
            if (missing > 0) return $"{teamName} is missing kills, deaths or assists for {missing} player{(missing == 1 ? "" : "s")}.";
            var scores = team.Select(p => KdaScore(p)!.Value).ToArray();
            var best = scores.Max();
            var top = Enumerable.Range(0, team.Count).Where(i => Math.Abs(scores[i] - best) < 1e-9).ToList();
            if (top.Count > 1) return $"{teamName}: two or more players share the best K/D/A score, so nobody is named. Pick one by hand.";
            if (team[top[0]].ProfileId == null) return $"{teamName}: the best player has no profile any more, so nobody is named.";
            return null;
        }

        // MVP count of a player = matches where they are the COUNTED match MVP (MvpPicks.CountedMvp: the all-stats pick, else the
        // K/D/A pick when the game has no all-stats pick). One credit per match. Owner decision 2026-10-19; it replaced "either
        // score counts" of 2026-10-14. Losing-team picks do not count. Used by 8b; here so it is tested with the rest.
        public static int CountMvpMatches(int profileId, IEnumerable<MvpPicks> matches) =>
            matches.Count(m => m.CountedMvp == profileId);
    }
}
