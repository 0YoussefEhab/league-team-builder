using System.Globalization;
using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // MVP redesign, Step 7: the short "why this MVP" sentence shown under an all-stats MVP in the match popup.
    // Pure rules (no EF, no clock). Design: STEP-0-MVP-REDESIGN.md, Step 7.
    //
    // It is worked out live from the saved stats with the same engine as the pick itself (LeagueMvpScoring), so nothing new
    // is stored. A part counts as "strongest" by the points it adds over an average teammate:
    //     weight / 100 x (part - 1)
    // The top three parts that add at least MinPoints are named. Returns null (nothing is shown) when:
    //   - the game has no scoring profile (old formula), the team lacks a stat, or the player is not on a team of the match;
    //   - the stored pick is not what the current rules give (a match picked before the new scoring and not recomputed yet,
    //     so the sentence would explain somebody else).
    public static class LeagueMvpBreakdown
    {
        private const double MinPoints = 0.02;
        private const int MaxParts = 3;
        private const double Epsilon = 1e-9;   // floating point noise only

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string? Why(IReadOnlyList<MvpPlayer> players, int profileId, MvpScoringProfile? profile,
            IReadOnlyDictionary<int, string> names)
        {
            if (profile is not MvpScoringProfile sp) return null;
            var pick = players.FirstOrDefault(p => p.ProfileId == profileId);
            if (pick == null) return null;

            var team = players.Where(p => p.TeamIndex == pick.TeamIndex).ToList();
            var parts = LeagueMvpScoring.Score(team, sp);
            if (parts == null) return null;
            if (LeagueMvpRules.BestByAllStats(team, sp) != profileId) return null;

            var n = team.Count;
            var i = team.FindIndex(p => ReferenceEquals(p, pick));
            var me = parts[i];

            // Parts in the same order as LeagueMvpScoring.WeightsOf.
            double[] values = { me.Combat, me.Damage, me.Economy, me.Objectives, me.Vision, me.Survival, me.DamageTaken };
            var weights = LeagueMvpScoring.WeightsOf(sp, pick.Lane);
            var strongest = Enumerable.Range(0, values.Length)
                .Select(k => (Part: k, Points: weights[k] / 100.0 * (values[k] - 1.0)))
                .Where(x => x.Points >= MinPoints)
                .OrderByDescending(x => x.Points)
                .Take(MaxParts)
                .Select(x => Describe(x.Part, values[x.Part], n, team, pick))
                .ToList();

            var text = $"Score {me.Score.ToString("0.00", Inv)} (an average teammate is 1.00).";
            if (strongest.Count > 0) text += " Strongest: " + string.Join("; ", strongest) + ".";

            // Close call: somebody else was within the tie margin of the best score, so the tie-break decided.
            var best = parts.Max(x => x.Score);
            var rivals = Enumerable.Range(0, n)
                .Where(j => j != i && best - parts[j].Score <= LeagueMvpRules.TieMargin + Epsilon)
                .OrderByDescending(j => team[j].Kills!.Value + team[j].Assists!.Value)
                .ThenBy(j => team[j].Deaths!.Value)
                .ToList();
            if (rivals.Count > 0)
            {
                var rival = team[rivals[0]];
                var rivalName = rival.ProfileId is int rid && names.TryGetValue(rid, out var rn) ? rn : "a teammate";
                var rivalScore = parts[rivals[0]].Score.ToString("0.00", Inv);
                var myKa = pick.Kills!.Value + pick.Assists!.Value;
                var rivalKa = rival.Kills!.Value + rival.Assists!.Value;
                text += $" Close call with {rivalName} ({rivalScore}): ";
                if (myKa > rivalKa)
                {
                    var a = LeagueMvpRules.KillParticipation(team, pick);
                    var b = LeagueMvpRules.KillParticipation(team, rival);
                    text += a != null && b != null
                        ? $"won on kill participation ({Pct(a.Value)} vs {Pct(b.Value)})."
                        : "won on kill participation.";
                }
                else
                {
                    text += $"level on kill participation, won on fewer deaths ({pick.Deaths!.Value} vs {rival.Deaths!.Value}).";
                }
            }
            return text;
        }

        private static string Pct(double ratio) => Math.Round(ratio * 100).ToString("0", Inv) + "%";

        // One part in plain words. Share-based parts: part / n is the player's share of the team.
        private static string Describe(int part, double value, int n, IReadOnlyList<MvpPlayer> team, MvpPlayer p)
        {
            var share = Pct(value / n);
            switch (part)
            {
                case 0:
                {
                    var kp = LeagueMvpRules.KillParticipation(team, p);
                    var kda = $"{p.Kills!.Value}/{p.Deaths!.Value}/{p.Assists!.Value}";
                    return kp == null ? $"Fighting ({kda})" : $"Fighting ({kda}, {Pct(kp.Value)} kill participation)";
                }
                case 1: return $"Damage ({share} of the team's)";
                case 2: return $"Gold and farm ({share} of the team's)";
                case 3: return $"Objectives ({share} of the team's objective damage)";
                case 4: return $"Vision ({share} of the team's)";
                case 5:
                {
                    var d = p.Deaths!.Value;
                    var td = team.Sum(x => x.Deaths!.Value);
                    return d == 0 ? $"Staying alive (no deaths, team total {td})"
                        : $"Staying alive ({d} {(d == 1 ? "death" : "deaths")}, team total {td})";
                }
                default: return $"Tanking ({share} of the damage the team took)";
            }
        }
    }
}
