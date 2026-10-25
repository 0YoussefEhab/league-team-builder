using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LeagueTeamBuilder.Services
{
    // Global filter (Program.cs): stands where HFECloud's DynamicPermissionFilter stood. Every action of a League controller
    // must be allowed by the role table (RolePermissions). Other controllers (AdminUsers, Notifications) keep their own [Authorize].
    public class RolePermissionFilter : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.ActionDescriptor is not ControllerActionDescriptor action) return Task.CompletedTask;
            if (!LeaguePermissions.IsLeagueController(action.ControllerName)) return Task.CompletedTask;

            var user = context.HttpContext.User;
            if (RolePermissions.IsAllowed(user, action.ControllerName, action.ActionName)) return Task.CompletedTask;

            if (user.Identity?.IsAuthenticated != true)
            {
                context.Result = new ChallengeResult();           // not signed in: go to the login page
            }
            else if (IsScriptRequest(context.HttpContext.Request))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);   // the page's script shows its own message
            }
            else
            {
                context.Result = new ForbidResult();              // a normal page: the "access denied" page
            }
            return Task.CompletedTask;
        }

        // The League scripts send fetch() calls with Accept: application/json (or a JSON body). A redirect to an HTML page would break them.
        private static bool IsScriptRequest(HttpRequest request) =>
            request.Headers.Accept.Any(a => a != null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            || (request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
