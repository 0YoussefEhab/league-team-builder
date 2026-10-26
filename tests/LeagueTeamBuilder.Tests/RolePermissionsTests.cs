using System.Security.Claims;
using LeagueTeamBuilder.Data;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // A7: the role table (RolePermissions), the checker, the filter and the "who may open this" query.
    public class RolePermissionsTests
    {
        private static ClaimsPrincipal User(params string[] roles) =>
            new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), authenticationType: "test"));

        private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

        // ---- the table ----

        [Theory]
        // Member: look at everything, answer clashes.
        [InlineData("Member", "League", "Index", true)]
        [InlineData("Member", "League", "Leaderboard", true)]
        [InlineData("Member", "LeagueClash", "Respond", true)]
        [InlineData("Member", "League", "Builder", false)]
        [InlineData("Member", "LeagueMatch", "Submit", false)]
        [InlineData("Member", "LeagueSettings", "Guests", false)]
        [InlineData("Member", "LeagueSettings", "Badges", false)]
        // Officer: run games and the roster, but not the Admin settings.
        [InlineData("Officer", "League", "Index", true)]
        [InlineData("Officer", "League", "Builder", true)]
        [InlineData("Officer", "LeagueMatch", "Finish", true)]
        [InlineData("Officer", "LeagueSettings", "Guests", true)]
        [InlineData("Officer", "LeagueSettings", "Champions", false)]
        [InlineData("Officer", "LeagueSettings", "Badges", false)]
        [InlineData("Officer", "LeagueSettings", "UploadBadge", false)]
        [InlineData("Officer", "LeagueMatch", "BackfillMvps", false)]
        // Admin: everything listed.
        [InlineData("Admin", "League", "Index", true)]
        [InlineData("Admin", "LeagueSettings", "Champions", true)]
        [InlineData("Admin", "LeagueMatch", "BackfillMvps", true)]
        public void The_role_table_answers_as_designed(string role, string controller, string action, bool expected)
        {
            Assert.Equal(expected, RolePermissions.IsAllowed(User(role), controller, action));
        }

        [Theory]
        [InlineData("Badges")]
        [InlineData("UploadBadge")]
        [InlineData("RemoveBadge")]
        [InlineData("ResetBadges")]
        public void The_four_badge_actions_are_Admin_only(string action)
        {
            Assert.Equal(new[] { AppRoles.Admin }, RolePermissions.RolesAllowing("LeagueSettings", action));
            Assert.True(RolePermissions.IsAllowed(User(AppRoles.Admin), "LeagueSettings", action));
            Assert.False(RolePermissions.IsAllowed(User(AppRoles.Officer), "LeagueSettings", action));
            Assert.False(RolePermissions.IsAllowed(User(AppRoles.Member), "LeagueSettings", action));
        }

        [Fact]
        public void Names_are_not_case_sensitive()
        {
            Assert.True(RolePermissions.IsAllowed(User("Admin"), "leaguesettings", "badges"));
        }

        [Fact]
        public void An_action_that_is_not_in_the_table_is_denied_for_everybody_Admin_included()
        {
            Assert.Empty(RolePermissions.RolesAllowing("LeagueSettings", "DoesNotExist"));
            Assert.False(RolePermissions.IsAllowed(User("Admin", "Officer", "Member"), "LeagueSettings", "DoesNotExist"));
        }

        [Fact]
        public void Nobody_who_is_not_signed_in_or_has_no_known_role_gets_in()
        {
            Assert.False(RolePermissions.IsAllowed(null, "League", "Index"));
            Assert.False(RolePermissions.IsAllowed(Anonymous(), "League", "Index"));
            Assert.False(RolePermissions.IsAllowed(User("Guest"), "League", "Index"));
            Assert.False(RolePermissions.IsAllowed(User(), "League", "Index"));
            // A role claim on an identity that is not authenticated does not count either.
            var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }));
            Assert.False(RolePermissions.IsAllowed(unauthenticated, "League", "Index"));
        }

        [Fact]
        public void Every_action_of_a_League_controller_is_in_the_table()
        {
            // The same check Program.cs logs at start-up: an unlisted action would be denied for everybody.
            Assert.Empty(RolePermissions.FindUnlistedActions(typeof(RolePermissions).Assembly));
        }

        [Fact]
        public void A_higher_role_may_always_do_what_a_lower_one_may()
        {
            var controllers = typeof(RolePermissions).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(Controller).IsAssignableFrom(t) && t.Name.EndsWith("Controller"))
                .Where(t => LeaguePermissions.IsLeagueController(t.Name[..^"Controller".Length]))
                .ToList();
            Assert.NotEmpty(controllers);

            foreach (var type in controllers)
            {
                var controller = type.Name[..^"Controller".Length];
                var actions = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName && m.GetCustomAttributes(typeof(NonActionAttribute), false).Length == 0)
                    .Select(m => m.Name).Distinct();
                foreach (var action in actions)
                {
                    var roles = RolePermissions.RolesAllowing(controller, action);
                    // Admin is always in; the list is Admin, or Admin + Officer, or all three (never a gap).
                    Assert.True(roles.Length is >= 1 and <= 3, $"{controller}/{action}");
                    Assert.Equal(AppRoles.All.Take(roles.Length).ToArray(), roles);
                }
            }
        }

        [Fact]
        public async Task The_checker_gives_the_same_answer_as_the_table()
        {
            var checker = new RolePermissionChecker();
            Assert.True(await checker.CanAccessAsync(User("Admin"), "LeagueSettings", "Badges"));
            Assert.False(await checker.CanAccessAsync(User("Officer"), "LeagueSettings", "Badges"));
            Assert.False(await checker.CanAccessAsync(null, "League", "Index"));
        }

        // ---- the filter ----

        private static AuthorizationFilterContext Context(ClaimsPrincipal user, string controller, string action, string? accept = null)
        {
            var http = new DefaultHttpContext { User = user };
            if (accept != null) http.Request.Headers["Accept"] = accept;
            var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action };
            return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), new List<IFilterMetadata>());
        }

        [Fact]
        public async Task The_filter_lets_an_allowed_request_through()
        {
            var ctx = Context(User("Admin"), "LeagueSettings", "Badges");
            await new RolePermissionFilter().OnAuthorizationAsync(ctx);
            Assert.Null(ctx.Result);
        }

        [Fact]
        public async Task The_filter_sends_a_visitor_who_is_not_signed_in_to_the_login_page()
        {
            var ctx = Context(Anonymous(), "League", "Index");
            await new RolePermissionFilter().OnAuthorizationAsync(ctx);
            Assert.IsType<ChallengeResult>(ctx.Result);
        }

        [Fact]
        public async Task The_filter_shows_access_denied_to_a_signed_in_user_who_may_not()
        {
            var ctx = Context(User("Officer"), "LeagueSettings", "Badges");
            await new RolePermissionFilter().OnAuthorizationAsync(ctx);
            Assert.IsType<ForbidResult>(ctx.Result);
        }

        [Fact]
        public async Task The_filter_answers_a_script_request_with_403_not_a_redirect()
        {
            var ctx = Context(User("Member"), "LeagueSettings", "UploadBadge", accept: "application/json");
            await new RolePermissionFilter().OnAuthorizationAsync(ctx);
            var result = Assert.IsType<StatusCodeResult>(ctx.Result);
            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        }

        [Fact]
        public async Task The_filter_denies_an_unlisted_League_action_even_for_Admin()
        {
            var ctx = Context(User("Admin"), "LeagueSettings", "DoesNotExist");
            await new RolePermissionFilter().OnAuthorizationAsync(ctx);
            Assert.IsType<ForbidResult>(ctx.Result);
        }

        [Fact]
        public async Task The_filter_ignores_controllers_that_are_not_League_controllers()
        {
            var ctx = Context(Anonymous(), "AdminUsers", "Index");
            await new RolePermissionFilter().OnAuthorizationAsync(ctx);
            Assert.Null(ctx.Result);
        }

        // ---- "which users may open this action" ----

        [Fact]
        public async Task UserIdsAllowedTo_returns_only_the_users_whose_role_may_open_the_action()
        {
            using var testDb = new SqliteTestDb();
            await using (var db = testDb.NewContext())
            {
                foreach (var role in AppRoles.All)
                    db.Roles.Add(new IdentityRole { Id = "role-" + role, Name = role, NormalizedName = role.ToUpperInvariant() });
                foreach (var role in AppRoles.All)
                {
                    db.Users.Add(new Models.ApplicationUser { Id = "user-" + role, UserName = role, NormalizedUserName = role.ToUpperInvariant() });
                    db.UserRoles.Add(new IdentityUserRole<string> { UserId = "user-" + role, RoleId = "role-" + role });
                }
                await db.SaveChangesAsync();
            }

            await using var query = testDb.NewContext();
            var everyone = query.UserIdsAllowedTo("League", "Index").ToList();
            var officers = query.UserIdsAllowedTo("League", "Builder").ToList();
            var admins = query.UserIdsAllowedTo("LeagueSettings", "Badges").ToList();
            var nobody = query.UserIdsAllowedTo("LeagueSettings", "DoesNotExist").ToList();

            Assert.Equal(new[] { "user-Admin", "user-Member", "user-Officer" }, everyone.OrderBy(x => x).ToArray());
            Assert.Equal(new[] { "user-Admin", "user-Officer" }, officers.OrderBy(x => x).ToArray());
            Assert.Equal(new[] { "user-Admin" }, admins);
            Assert.Empty(nobody);
        }
    }
}
