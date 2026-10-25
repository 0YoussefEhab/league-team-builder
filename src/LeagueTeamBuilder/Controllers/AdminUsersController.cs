using LeagueTeamBuilder.Data;
using LeagueTeamBuilder.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Controllers
{
    // /Admin/Users: who has an account, what role they have, and switching an account off.
    // Protected by the Admin ROLE directly (not by the League permission table), so it cannot be locked out by a table edit.
    // Rule: there must always be at least one active Admin; changes that would remove the last one are refused.
    [Authorize(Roles = AppRoles.Admin)]
    [Route("Admin/Users")]
    public class AdminUsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _users;

        public AdminUsersController(UserManager<ApplicationUser> users) => _users = users;

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var all = await _users.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();

            var roleOf = new Dictionary<string, string>();
            foreach (var role in Enumerable.Reverse(AppRoles.All)) // lowest first, so the highest role wins if someone has two (Enumerable.Reverse on purpose: C# 14 binds array.Reverse() to an in-place span method)
                foreach (var u in await _users.GetUsersInRoleAsync(role))
                    roleOf[u.Id] = role;

            var me = _users.GetUserId(User);
            return View(new AdminUsersPageModel
            {
                Roles = AppRoles.All,
                Message = TempData["Message"] as string,
                Error = TempData["Error"] as string,
                Users = all.Select(u => new AdminUserRow
                {
                    Id = u.Id,
                    Email = u.Email ?? "",
                    UserName = u.UserName ?? "",
                    DisplayName = u.DisplayName,
                    Role = roleOf.TryGetValue(u.Id, out var r) ? r : "",
                    IsDeactivated = u.IsDeactivated,
                    IsYou = u.Id == me,
                }).ToList(),
            });
        }

        [HttpPost("Role")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetRole(string id, string role)
        {
            if (!AppRoles.All.Contains(role)) return Fail("That role does not exist.");
            var user = await _users.FindByIdAsync(id);
            if (user is null) return Fail("That user was not found.");

            var current = await _users.GetRolesAsync(user);
            if (current.Count == 1 && current[0] == role) return Done($"{Label(user)} already has the role {role}.");

            if (current.Contains(AppRoles.Admin) && role != AppRoles.Admin && await IsLastActiveAdminAsync(user))
                return Fail("There must be at least one active Admin. Make someone else an Admin first.");

            if (current.Count > 0)
            {
                var removed = await _users.RemoveFromRolesAsync(user, current);
                if (!removed.Succeeded) return Fail(Errors(removed));
            }
            var added = await _users.AddToRoleAsync(user, role);
            if (!added.Succeeded) return Fail(Errors(added));

            // The role is part of the sign-in cookie; a new stamp makes the open session reload it within about a minute.
            await _users.UpdateSecurityStampAsync(user);
            return Done($"{Label(user)} is now {role}.");
        }

        [HttpPost("Active")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetActive(string id, bool active)
        {
            var user = await _users.FindByIdAsync(id);
            if (user is null) return Fail("That user was not found.");

            if (!active)
            {
                if (user.Id == _users.GetUserId(User)) return Fail("You cannot deactivate your own account.");
                if (await IsLastActiveAdminAsync(user)) return Fail("There must be at least one active Admin.");
            }

            if (user.IsDeactivated == !active) return Done($"{Label(user)} is already {(active ? "active" : "deactivated")}.");

            user.IsDeactivated = !active;
            var saved = await _users.UpdateAsync(user);
            if (!saved.Succeeded) return Fail(Errors(saved));

            // Ends a session that is already open (see AppSignInManager and Program.cs).
            await _users.UpdateSecurityStampAsync(user);
            return Done($"{Label(user)} was {(active ? "reactivated" : "deactivated")}.");
        }

        // True when this account is an active Admin and no other active Admin exists.
        private async Task<bool> IsLastActiveAdminAsync(ApplicationUser target)
        {
            if (target.IsDeactivated || !await _users.IsInRoleAsync(target, AppRoles.Admin)) return false;
            var admins = await _users.GetUsersInRoleAsync(AppRoles.Admin);
            return !admins.Any(a => a.Id != target.Id && !a.IsDeactivated);
        }

        private static string Label(ApplicationUser u) =>
            !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : (u.Email ?? u.UserName ?? "The user");

        private static string Errors(IdentityResult r) => string.Join(" ", r.Errors.Select(e => e.Description));

        private IActionResult Done(string message) { TempData["Message"] = message; return RedirectToAction(nameof(Index)); }
        private IActionResult Fail(string message) { TempData["Error"] = message; return RedirectToAction(nameof(Index)); }
    }
}
