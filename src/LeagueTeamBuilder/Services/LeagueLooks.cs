using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // One look = one entry here + one [data-look] block in wwwroot/css/leaguehub-looks.css (Steel is the :root default in leaguehub-theme.css).
    // FontsHref is the stylesheet that declares only that look's fonts; _Layout links just the active one.
    public sealed record LookInfo(string Key, string Name, string FontsHref);

    public static class LeagueLooks
    {
        public const string CookieName = "lh-look";

        // THE allowed list. To hide a look from the picker, remove its line (a saved or cookie value that is no longer listed falls back to the default).
        public static readonly IReadOnlyList<LookInfo> All = new[]
        {
            new LookInfo("steel",  "Steel",  "~/lib/fonts/steel.css"),
            new LookInfo("brutal", "Brutal", "~/lib/fonts/brutal.css"),
            new LookInfo("forest", "Forest", "~/lib/fonts/forest.css"),
        };

        public static LookInfo? Find(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            key = key.Trim();
            return All.FirstOrDefault(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }

    // Decides which look a request gets: the lh-look cookie, else the signed-in user's saved look (ApplicationUser.Look),
    // else Theme:DefaultLook, else the first listed look. Anything not in LeagueLooks.All counts as missing.
    public class LookResolver
    {
        private const string ItemKey = "lh.look";
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;

        public LookResolver(ApplicationDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public LookInfo Default => LeagueLooks.Find(_config["Theme:DefaultLook"]) ?? LeagueLooks.Find("steel") ?? LeagueLooks.All[0];

        public async Task<LookInfo> ResolveAsync(HttpContext http)
        {
            if (http.Items[ItemKey] is LookInfo cached) return cached;

            var look = LeagueLooks.Find(http.Request.Cookies[LeagueLooks.CookieName]);
            if (look == null && http.User.Identity?.IsAuthenticated == true)
            {
                var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(id))
                {
                    var saved = await _db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Look).FirstOrDefaultAsync();
                    look = LeagueLooks.Find(saved);
                }
            }
            look ??= Default;
            http.Items[ItemKey] = look;
            return look;
        }
    }
}
