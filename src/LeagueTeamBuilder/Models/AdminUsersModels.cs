namespace LeagueTeamBuilder.Models
{
    public class AdminUserRow
    {
        public string Id { get; set; } = "";
        public string Email { get; set; } = "";
        public string UserName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        // One role per user (Admin, Officer or Member). Empty only for an account created outside the app.
        public string Role { get; set; } = "";
        public bool IsDeactivated { get; set; }
        public bool IsYou { get; set; }
    }

    public class AdminUsersPageModel
    {
        public List<AdminUserRow> Users { get; set; } = new();
        public string[] Roles { get; set; } = Array.Empty<string>();
        public string? Message { get; set; }
        public string? Error { get; set; }
    }
}
