namespace LeagueTeamBuilder.Models
{
    // One in-app notification for one user (a clash was scheduled, a guest was linked to them). Shown on /Notifications.
    public class UserNotification
    {
        public const int MaxMessage = 500;
        public const int MaxLink = 300;

        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        // Site-relative link the notification opens (for example /LeagueClash/Respond/5). Null = no link.
        public string? Link { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ReadUtc { get; set; }
    }

    // One line of the audit trail: who did what to what. There is no page that shows it yet.
    public class ActivityLogEntry
    {
        public const int MaxAction = 100;
        public const int MaxTarget = 1000;

        public int Id { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string? ActorUserId { get; set; }
        public string ActorLabel { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }

    public class NotificationsPageModel
    {
        public List<UserNotification> Items { get; set; } = new();
        public int Unread { get; set; }
    }
}
