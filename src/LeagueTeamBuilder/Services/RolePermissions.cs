using System.Reflection;
using System.Security.Claims;
using LeagueTeamBuilder.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Asked by the League views ("show this button?") and by the controllers' filter ("may this person open this action?").
    // The same two-string question HFECloud asked of its group matrix; here the answer comes from the role table below.
    public interface IPermissionChecker
    {
        Task<bool> CanAccessAsync(ClaimsPrincipal? user, string controller, string action);
    }

    // Who may do what. ONE table, one line per action: the lowest role that may use it. A higher role may do everything
    // a lower one may (Admin > Officer > Member). Change who can do something by moving its name to another line.
    //
    // Fail closed: an action of a League controller that is NOT in this table is denied for everybody, Admin included.
    // That is why the start-up check (FindUnlistedActions, called from Program.cs) logs every League action missing here.
    public static class RolePermissions
    {
        private static readonly Dictionary<string, string> MinRoleByAction = Build();

        private static Dictionary<string, string> Build()
        {
            var t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Add(string controller, string minRole, params string[] actions)
            {
                foreach (var a in actions) t[Key(controller, a)] = minRole;
            }

            // ---- Member: look at everything, keep their own profile, answer clashes.
            Add("League", AppRoles.Member, "Index", "Leaderboard", "Player", "History", "MatchDetail", "Profile");
            Add("LeagueClash", AppRoles.Member, "Index", "Respond", "SaveResponse");

            // ---- Officer: run games and the roster.
            Add("League", AppRoles.Officer, "Builder", "Generate");
            Add("LeagueMatch", AppRoles.Officer, "Submit", "Start", "Finish", "EditResult", "EditDate", "Stats", "SetMvp", "ImportStats", "Delete");
            Add("LeagueClash", AppRoles.Officer, "Build", "EndClash", "CreateTeam", "RenameTeam", "DeleteTeam", "SetGuestBusy", "NextGame");
            Add("LeagueExport", AppRoles.Officer, "Render", "Send");
            Add("LeagueSettings", AppRoles.Officer, "Index", "SetPlayerTier",
                "Guests", "AddGuest", "EditGuest", "DeleteGuest", "EditGuestNames", "LinkGuest",
                "EditPlayerRank", "EditPlayerNames",
                "ClashSchedule", "CreateClash", "EditClash", "CancelClash");

            // ---- Admin: the catalog, the modes and maps, the tier list, Discord, and anything that rewrites or undoes history.
            Add("LeagueSettings", AppRoles.Admin,
                "Champions", "AddChampion", "BulkAddChampions", "RenameChampion", "SetChampionActive", "RemoveChampion",
                "Modes", "SaveModes", "UploadMap", "RemoveMap", "SaveSpots",
                "Tiers", "AddTier", "RenameTier", "MoveTier", "RemoveTier",
                "LinkedGuests", "UnlinkGuest", "UndoGuestLink",
                "AutoSend");
            Add("LeagueMatch", AppRoles.Admin, "BackfillMvps");

            return t;
        }

        private static string Key(string controller, string action) => controller + "/" + action;

        private static int Rank(string role) => Array.IndexOf(AppRoles.All, role) switch
        {
            // AppRoles.All is highest first (Admin, Officer, Member), so a LOWER index is a HIGHER role.
            -1 => 0,
            var i => AppRoles.All.Length - i,
        };

        // The roles that may use controller/action, highest first. Empty when the action is not in the table.
        public static string[] RolesAllowing(string controller, string action)
        {
            if (!MinRoleByAction.TryGetValue(Key(controller, action), out var min)) return Array.Empty<string>();
            var need = Rank(min);
            return AppRoles.All.Where(r => Rank(r) >= need).ToArray();
        }

        public static bool IsAllowed(ClaimsPrincipal? user, string controller, string action)
        {
            if (user?.Identity?.IsAuthenticated != true) return false;
            return RolesAllowing(controller, action).Any(user.IsInRole);
        }

        // Public actions of the League controllers that the table does not list (they would be denied for everyone).
        public static List<string> FindUnlistedActions(Assembly assembly)
        {
            var missing = new List<string>();
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract || !typeof(Controller).IsAssignableFrom(type)) continue;
                if (!type.Name.EndsWith("Controller", StringComparison.Ordinal)) continue;
                var controller = type.Name[..^"Controller".Length];
                if (!LeaguePermissions.IsLeagueController(controller)) continue;

                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (m.IsSpecialName || m.GetCustomAttribute<NonActionAttribute>() != null) continue;
                    var key = Key(controller, m.Name);
                    if (!MinRoleByAction.ContainsKey(key) && !missing.Contains(key)) missing.Add(key);
                }
            }
            return missing;
        }
    }

    public class RolePermissionChecker : IPermissionChecker
    {
        // The roles are already in the sign-in cookie, so nothing here touches the database.
        public Task<bool> CanAccessAsync(ClaimsPrincipal? user, string controller, string action) =>
            Task.FromResult(RolePermissions.IsAllowed(user, controller, action));
    }

    public static class RolePermissionQueries
    {
        // Ids of the users whose role may open controller/action. Replaces HFECloud's "groups with this box ticked" query.
        // Does not look at IsDeactivated; callers that need active users filter on it themselves.
        public static IQueryable<string> UserIdsAllowedTo(this ApplicationDbContext db, string controller, string action)
        {
            var roles = RolePermissions.RolesAllowing(controller, action);
            return from ur in db.UserRoles
                   join r in db.Roles on ur.RoleId equals r.Id
                   where r.Name != null && roles.Contains(r.Name)
                   select ur.UserId;
        }
    }
}
