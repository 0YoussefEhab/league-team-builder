using System.Security.Claims;
using LeagueTeamBuilder.Models;

namespace LeagueTeamBuilder.Services
{
    // Stands where HFECloud's AdminActivityLogger stood.
    public interface IActivityLog
    {
        // Adds one line to the audit trail. It does NOT save: every caller changes something else on the same context and
        // calls SaveChangesAsync right after, so the line is written in the same save (and in the same transaction) as the change.
        void Log(ClaimsPrincipal? actor, string action, string? target);
    }

    public class ActivityLog : IActivityLog
    {
        private readonly ApplicationDbContext _db;

        public ActivityLog(ApplicationDbContext db) => _db = db;

        public void Log(ClaimsPrincipal? actor, string action, string? target)
        {
            var label = actor?.Identity?.Name;
            _db.ActivityLogEntries.Add(new ActivityLogEntry
            {
                CreatedUtc = DateTime.UtcNow,
                ActorUserId = actor?.FindFirstValue(ClaimTypes.NameIdentifier),
                ActorLabel = string.IsNullOrWhiteSpace(label) ? "(unknown)" : label!,
                Action = Cut(action ?? string.Empty, ActivityLogEntry.MaxAction),
                Target = Cut(target ?? string.Empty, ActivityLogEntry.MaxTarget),
            });
        }

        private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
    }
}
