namespace LeagueTeamBuilder.Services
{
    // The League controllers (League, LeagueSettings, LeagueMatch, LeagueClash, LeagueExport) are gated by the role table in
    // RolePermissions.cs (Admin, Officer, Member). An action that is not in that table is denied for everybody, Admin
    // included, so there is no fail-open window. RolePermissionFilter enforces it; RolePermissionChecker answers the views.
    public static class LeaguePermissions
    {
        public static bool IsLeagueController(string controllerName) =>
            controllerName.StartsWith("League", StringComparison.Ordinal);
    }
}
