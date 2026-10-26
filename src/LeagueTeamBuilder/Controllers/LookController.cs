using System.Security.Claims;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Controllers
{
    // The look picker's server side (step C1): POST /Look/Set from the top-bar dropdown (wwwroot/js/lh-shell.js).
    // Not a League controller (the name does not start with "League"), so the role table does not apply; anyone may pick a look,
    // signed in or not. The antiforgery token in the form is still required.
    //   - the value must be in LeagueLooks.All, otherwise 400 and nothing is saved;
    //   - the cookie lh-look is set for one year (SameSite Lax, not HttpOnly, Secure only on https);
    //   - a signed-in user also gets ApplicationUser.Look saved (one UPDATE);
    //   - the script sends the header X-LH-Ajax and gets 204 (no page change); a plain form post gets 302 to a local returnUrl, else "/".
    [AllowAnonymous]
    public class LookController : Controller
    {
        private readonly ApplicationDbContext _db;

        public LookController(ApplicationDbContext db) => _db = db;

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Set(string? look, string? returnUrl)
        {
            var chosen = LeagueLooks.Find(look);
            if (chosen == null) return BadRequest("Unknown look.");

            Response.Cookies.Append(LeagueLooks.CookieName, chosen.Key, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                Path = "/",
                SameSite = SameSiteMode.Lax,
                HttpOnly = false,
                Secure = Request.IsHttps,
                IsEssential = true
            });

            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(userId))
                    await _db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Look, chosen.Key));
            }

            if (Request.Headers.ContainsKey("X-LH-Ajax")) return NoContent();

            return Redirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
        }
    }
}
