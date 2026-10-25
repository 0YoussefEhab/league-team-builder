namespace LeagueTeamBuilder.Data
{
    // The three roles. Admin manages accounts and every setting; Officer runs games and the roster; Member plays.
    // What each role may open is decided by the action table added in step A4 (RolePermissionChecker).
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string Officer = "Officer";
        public const string Member = "Member";

        // Highest first. Used for the role dropdown and for seeding.
        public static readonly string[] All = { Admin, Officer, Member };
    }
}
