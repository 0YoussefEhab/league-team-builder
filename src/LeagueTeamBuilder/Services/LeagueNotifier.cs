using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Stands where HFECloud's NotificationService stood. In-app only: a row in UserNotifications, read on /Notifications.
    // Nothing leaves the machine, so it works the same in the offline desktop edition.
    public interface ILeagueNotifier
    {
        Task CreateForUserAsync(string userId, string message, string? link = null);
        Task<int> UnreadCountAsync(string userId);
    }

    public class LeagueNotifier : ILeagueNotifier
    {
        private readonly ApplicationDbContext _db;

        public LeagueNotifier(ApplicationDbContext db) => _db = db;

        // Saves at once. The callers (clash scheduling, guest linking) only notify after their own change is already saved.
        public async Task CreateForUserAsync(string userId, string message, string? link = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(message)) return;
            _db.UserNotifications.Add(new UserNotification
            {
                UserId = userId,
                Message = Cut(message.Trim(), UserNotification.MaxMessage),
                Link = string.IsNullOrWhiteSpace(link) ? null : Cut(link.Trim(), UserNotification.MaxLink),
                CreatedUtc = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync();
        }

        public Task<int> UnreadCountAsync(string userId) =>
            _db.UserNotifications.CountAsync(n => n.UserId == userId && n.ReadUtc == null);

        private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
    }
}
