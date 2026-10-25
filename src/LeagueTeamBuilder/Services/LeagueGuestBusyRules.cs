using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Step 8i: a moderator saves a guest's free / busy answer for a clash (decisions: PLAN-league.md, "Step 8i - decisions").
    // Pure rules, no database. The write is LeagueClashScheduleService.SetGuestBusyAsync.
    public static class LeagueGuestBusyRules
    {
        // Null = allowed. The answer may be set while the clash is Open or InProgress; a Done or Cancelled clash is closed.
        public static string? CheckStatus(LeagueClashStatus status) => status switch
        {
            LeagueClashStatus.Open => null,
            LeagueClashStatus.InProgress => null,
            LeagueClashStatus.Cancelled => "This clash was cancelled.",
            _ => "This clash is over, so answers are closed.",
        };

        public static LeagueClashAnswer AnswerFor(bool busy) => busy ? LeagueClashAnswer.Busy : LeagueClashAnswer.Free;

        public static string SavedMessage(string name, bool busy) =>
            busy ? $"Saved: {name} is busy." : $"Saved: {name} is free.";
    }
}
