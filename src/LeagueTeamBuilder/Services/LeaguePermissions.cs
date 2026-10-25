namespace LeagueTeamBuilder.Services
{
    // League Team Builder controllers (League, LeagueSettings, LeagueMatch, LeagueClash) are DENIED
    // for every group - the admin group included - until the owner ticks them for a group on
    // Admin > Groups > Permissions (owner decision, docs/LEAGUE_BUILD_SPEC.md section 2).
    // Two places enforce it so there is no fail-open window:
    //   PermissionSyncService creates the rows as Allowed = false for every group.
    //   PermissionChecker treats a MISSING row for these controllers as not allowed.
    public static class LeaguePermissions
    {
        public static bool IsLeagueController(string controllerName) =>
            controllerName.StartsWith("League", StringComparison.Ordinal);
    }
}
