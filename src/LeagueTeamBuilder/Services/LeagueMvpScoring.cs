using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // MVP redesign, Step 1: the new all-stats score. Pure rules (no EF, no clock).
    // Design: STEP-0-MVP-REDESIGN.md ("The new scoring spec").
    //
    // NOT WIRED IN YET. LeagueMvpRules still decides every MVP on the site; nothing calls this class until Step 2.
    //
    // Every player gets seven parts. Each part is the player's SHARE of their own team multiplied by the team size n,
    // so 1.00 = an average teammate (no fixed 5: a 4-player or odd-sized team stays fair).
    // The score is the weighted sum of the parts. The weights depend on the profile (Draft by lane, or ARAM).

    public enum MvpScoringProfile { Draft, Aram }

    // One player's parts and final score. Every part is 1.00-based.
    public record MvpScoreParts(
        double Combat, double Damage, double Economy, double Objectives,
        double Vision, double Survival, double DamageTaken, double Score);

    public static class LeagueMvpScoring
    {
        // Weights of one row, each row sums to 100. Starting values, tuned later by looking at real matches.
        private readonly record struct Weights(
            double Combat, double Damage, double Economy, double Objectives,
            double Vision, double Survival, double DamageTaken)
        {
            public double Sum => Combat + Damage + Economy + Objectives + Vision + Survival + DamageTaken;
        }

        private static readonly Weights Top     = new(25, 20, 15, 15, 10, 10, 5);
        private static readonly Weights Jungle  = new(25, 10, 10, 25, 15, 10, 5);
        private static readonly Weights Mid     = new(25, 25, 15, 10, 5, 20, 0);
        private static readonly Weights Adc     = new(25, 30, 15, 10, 5, 15, 0);
        private static readonly Weights Support = new(30, 10, 5, 10, 25, 20, 0);
        // No lane saved on a draft player: the average of the five rows above (the old code used its base weights here).
        private static readonly Weights NoLane  = new(26, 19, 12, 14, 12, 15, 2);
        // ARAM (and Arena): the same row for everybody, no CS, vision or objectives.
        private static readonly Weights AramRow = new(40, 30, 0, 0, 0, 15, 15);

        private static Weights For(MvpScoringProfile profile, LeagueLane? lane)
        {
            if (profile == MvpScoringProfile.Aram) return AramRow;
            return lane switch
            {
                LeagueLane.Top => Top,
                LeagueLane.Jungle => Jungle,
                LeagueLane.Mid => Mid,
                LeagueLane.Adc => Adc,
                LeagueLane.Support => Support,
                _ => NoLane,
            };
        }

        // MVP redesign Step 7: the seven weights of a row in part order (Combat, Damage, Economy, Objectives, Vision,
        // Survival, Damage taken). The breakdown uses them to see which parts made the pick.
        public static double[] WeightsOf(MvpScoringProfile profile, LeagueLane? lane)
        {
            var w = For(profile, lane);
            return new[] { w.Combat, w.Damage, w.Economy, w.Objectives, w.Vision, w.Survival, w.DamageTaken };
        }

        // The weights of a row added up; always 100. Exposed so a check can confirm it.
        public static double WeightSum(MvpScoringProfile profile, LeagueLane? lane) => For(profile, lane).Sum;

        // The parts and score of every player of ONE team (same order as the input), or null when the team has fewer
        // than 2 players or anybody is missing a stat (the all-stats score needs every stat of every player).
        public static IReadOnlyList<MvpScoreParts>? Score(IReadOnlyList<MvpPlayer> team, MvpScoringProfile profile)
        {
            if (team.Count < 2 || !team.All(LeagueMvpRules.HasAllStats)) return null;
            var n = team.Count;

            // Share of the team times n. A part nobody has any of (team total 0) is 1.00 for everyone.
            double[] Share(Func<MvpPlayer, double> value)
            {
                var v = team.Select(value).ToArray();
                var total = v.Sum();
                return total <= 0 ? v.Select(_ => 1.0).ToArray() : v.Select(x => x / total * n).ToArray();
            }

            // Kill-life-assist ratio (K+A)/(D+1): one death in a clean game cannot wreck it.
            var kla = Share(p => (p.Kills!.Value + p.Assists!.Value) / (double)(p.Deaths!.Value + 1));
            var ka = Share(p => p.Kills!.Value + p.Assists!.Value);
            var damage = Share(p => p.DamageDealt!.Value);
            var gold = Share(p => p.Gold!.Value);
            var cs = Share(p => p.Cs!.Value);
            var objectives = Share(p => p.ObjectiveDamage!.Value);
            var vision = Share(p => p.Vision!.Value);
            var taken = Share(p => p.DamageTaken!.Value);

            // Survival = 1 minus the player's share of team deaths, scaled so an average player (1/n of the deaths) = 1.00.
            // A team with 0 deaths: everybody 1.00.
            var teamDeaths = team.Sum(p => p.Deaths!.Value);

            var result = new MvpScoreParts[n];
            for (var i = 0; i < n; i++)
            {
                var combat = 0.5 * kla[i] + 0.5 * ka[i];
                var economy = 0.5 * gold[i] + 0.5 * cs[i];
                var survival = teamDeaths == 0 ? 1.0 : (1.0 - team[i].Deaths!.Value / (double)teamDeaths) / (1.0 - 1.0 / n);

                var w = For(profile, team[i].Lane);
                var score = (w.Combat * combat + w.Damage * damage[i] + w.Economy * economy + w.Objectives * objectives[i]
                    + w.Vision * vision[i] + w.Survival * survival + w.DamageTaken * taken[i]) / 100.0;

                result[i] = new MvpScoreParts(combat, damage[i], economy, objectives[i], vision[i], survival, taken[i], score);
            }
            return result;
        }
    }
}
