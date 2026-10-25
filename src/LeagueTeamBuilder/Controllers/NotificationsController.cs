using System.Security.Claims;
using LeagueTeamBuilder.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Controllers
{
    // The page the in-app notifications are read on (HFECloud had it in its sidebar). Any signed-in user sees their own list only.
    // Not a League controller, so the role table does not apply; [Authorize] is enough.
    [Authorize]
    public class NotificationsController : Controller
    {
        private const int PageSize = 50;
        private readonly ApplicationDbContext _db;

        public NotificationsController(ApplicationDbContext db) => _db = db;

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = UserId;
            var model = new NotificationsPageModel
            {
                Items = await _db.UserNotifications.AsNoTracking()
                    .Where(n => n.UserId == userId)
                    .OrderByDescending(n => n.CreatedUtc).ThenByDescending(n => n.Id)
                    .Take(PageSize).ToListAsync(),
                Unread = await _db.UserNotifications.CountAsync(n => n.UserId == userId && n.ReadUtc == null),
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            var userId = UserId;
            var now = DateTime.UtcNow;
            await _db.UserNotifications.Where(n => n.UserId == userId && n.ReadUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadUtc, (DateTime?)now));
            return RedirectToAction(nameof(Index));
        }
    }
}
