using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Step 8e: the pure rules of the Player page (no EF, no clock), so they are unit-testable without a database.
    // Decisions: PLAN-league.md, "Step 8e - decisions".

    // The checked and cleaned result of an admin rank edit. Error is null when Ok.
    public record RankEditResult(bool Ok, string? Error, LeagueRank CurTier, int? CurDiv, LeagueRank PeakTier, int? PeakDiv);

    public static class LeaguePlayerRules
    {
        // Text of the line in the Admin activity log.
        public const string AuditAction = "Changed league player rank";

        // Same rules as the own profile form: the tier must exist, a division 1-4 is required below Master and is
        // dropped from Master up, and the peak cannot be lower than the current rank.
        public static RankEditResult ValidateRank(LeagueRank curTier, int? curDiv, LeagueRank peakTier, int? peakDiv)
        {
            static RankEditResult Fail(string error) => new(false, error, default, null, default, null);

            if (!Enum.IsDefined(curTier)) return Fail("Pick a valid current rank.");
            if (!Enum.IsDefined(peakTier)) return Fail("Pick a valid peak rank.");

            var cd = LeagueRankRules.HasDivision(curTier) ? curDiv : null;
            var pd = LeagueRankRules.HasDivision(peakTier) ? peakDiv : null;
            if (LeagueRankRules.HasDivision(curTier) && cd is not (>= 1 and <= 4)) return Fail("Pick the current division (1-4).");
            if (LeagueRankRules.HasDivision(peakTier) && pd is not (>= 1 and <= 4)) return Fail("Pick the peak division (1-4).");
            if (LeagueRankRules.Value(peakTier, pd) < LeagueRankRules.Value(curTier, cd))
                return Fail("Peak rank can't be lower than the current rank.");

            return new RankEditResult(true, null, curTier, cd, peakTier, pd);
        }

        // "Gold II (peak Platinum IV)".
        public static string RankPairText(LeagueRank cur, int? curDiv, LeagueRank peak, int? peakDiv) =>
            $"{LeagueViewText.RankText(cur, curDiv)} (peak {LeagueViewText.RankText(peak, peakDiv)})";

        // Target text of the audit line: "Name: Gold II (peak Platinum IV) -> Diamond IV (peak Diamond IV)".
        public static string RankChangeText(string name,
            LeagueRank oldCur, int? oldCurDiv, LeagueRank oldPeak, int? oldPeakDiv,
            LeagueRank newCur, int? newCurDiv, LeagueRank newPeak, int? newPeakDiv) =>
            $"{name}: {RankPairText(oldCur, oldCurDiv, oldPeak, oldPeakDiv)} -> {RankPairText(newCur, newCurDiv, newPeak, newPeakDiv)}";

        // The place of one player in the FULL ranking (LeagueRanking: ties share a number) and how many players there are.
        // Null when the id is not in the list.
        public static (int Position, int Total)? PlaceOf(IEnumerable<RankInput> players, int id)
        {
            var ranked = LeagueRanking.Rank(players);
            var entry = ranked.FirstOrDefault(e => e.Player.Id == id);
            return entry == null ? null : (entry.Position, ranked.Count);
        }

        // "#3 of 12 in the server".
        public static string PlaceText(int position, int total) => $"#{position} of {total} in the server";
    }
}
