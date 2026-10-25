using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LeagueTeamBuilder.Controllers
{
    // League Team Builder, member-facing side (docs/LEAGUE_BUILD_SPEC.md). Ordinary controller so it
    // is gated by the role table (RolePermissions) through RolePermissionFilter like every other
    // feature. League controllers are denied for ALL groups until the owner grants them (see
    // LeaguePermissions). Nothing here links to or edits the main profile; the only contact with it
    // is READING DisplayName/AvatarUrl once when the member presses "Copy name and picture".
    [Authorize]
    public class LeagueController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly LeagueProfileService _profiles;
        private readonly ChampionCatalogService _catalog;
        private readonly LeagueBuilderService _builder;
        private readonly LeagueMatchService _matches;
        private readonly LeagueHistoryService _history;
        private readonly LeagueOverviewService _overview;
        private readonly IPermissionChecker _permissions;

        public LeagueController(
            UserManager<ApplicationUser> userManager,
            LeagueProfileService profiles,
            ChampionCatalogService catalog,
            LeagueBuilderService builder,
            LeagueMatchService matches,
            LeagueHistoryService history,
            LeagueOverviewService overview,
            IPermissionChecker permissions)
        {
            _userManager = userManager;
            _profiles = profiles;
            _catalog = catalog;
            _builder = builder;
            _matches = matches;
            _history = history;
            _overview = overview;
            _permissions = permissions;
        }

        // GET /League - the Overview (Step 8c): top ranked, top MVPs, best Clash team, server stats. Read only.
        [HttpGet]
        public async Task<IActionResult> Index() => View(await _overview.GetAsync());

        // GET /League/Leaderboard?q=&kind=all|members|guests&rank=0..9&lane=top|jungle|mid|adc|support&sort=rank|mvps|games|winrate
        // (Step 8d). Every player, members and guests; search, filters and sort run on the server so the page also works
        // without JavaScript. Read only. New permission action League > Leaderboard (denied for every group until ticked).
        // The rows link to the player page (Step 8e) for a group that may open League > Player.
        [HttpGet]
        public async Task<IActionResult> Leaderboard(string? q, string? kind, string? rank, string? lane, string? sort)
        {
            var query = LeagueLeaderboardRules.ParseQuery(q, kind, rank, lane, sort);
            return View(await _overview.GetLeaderboardAsync(query));
        }

        // GET /League/Player/{id} - one player's page (Step 8e), id = LeagueProfile.Id (members and guests). Read only.
        // New permission action League > Player (denied for every group until ticked). The rank editor in the hero shows
        // only for a group that has LeagueSettings > EditPlayerRank (the POST itself is LeagueSettingsController.EditPlayerRank).
        // The "Match history" button (Step 8h) links to History?player=id; the view shows it only to a group with League > History.
        [HttpGet]
        public async Task<IActionResult> Player(int id)
        {
            var model = await _overview.GetPlayerAsync(id);
            if (model is null) return NotFound();
            model.Stats = await _history.GetPlayerStatsAsync(id);
            model.CanEditRank = await _permissions.CanAccessAsync(User, "LeagueSettings", "EditPlayerRank");
            model.CanEditNames = await _permissions.CanAccessAsync(User, "LeagueSettings", "EditPlayerNames");
            if (model.CanEditNames) model.RiotId = await _profiles.GetRiotIdAsync(id);
            return View(model);
        }

        // GET /League/Builder - the team builder page (Step 4c). Everything it needs is embedded in the page;
        // the only other call is POST /League/Generate.
        [HttpGet]
        public async Task<IActionResult> Builder()
        {
            var model = await _builder.GetPageAsync();
            // Open matches show who is playing, so they follow the same rule as the actions that change them.
            if (await _permissions.CanAccessAsync(User, "LeagueMatch", "Start")
                || await _permissions.CanAccessAsync(User, "LeagueMatch", "Finish")
                || await _permissions.CanAccessAsync(User, "LeagueMatch", "Delete"))
                model.OpenMatches = await _matches.GetOpenAsync();
            return View(model);
        }

        // POST /League/Generate - JSON in, JSON out. One action for everything the builder asks the server
        // (generate teams, recheck after a manual swap, apply a lane-clash option) so the owner ticks one
        // permission for it. Read-only: nothing is saved here (saving a match is LeagueMatchController, Step 4d). The anti-forgery token
        // comes in the RequestVerificationToken header.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate([FromBody] LeagueBuilderRequest? request)
        {
            if (request is null)
                return BadRequest(new LeagueBuilderResponse { Ok = false, Error = "The request was empty." });
            return Json(await _builder.RunAsync(request));
        }

        // GET /League/History?filter=all|done|open|missing&kind=custom|clash&mode=draft|aram|arena&q=text&player=id&page=1
        // - the match list (Step 5a, read-only). Step 8h added kind, mode, q (a player or a champion) and player.
        [HttpGet]
        public async Task<IActionResult> History(string? filter, string? kind, string? mode, string? q, int? player, int page = 1)
        {
            var model = await _history.GetPageAsync(LeagueHistoryService.ParseQuery(filter, kind, mode, q, player, page));
            model.CanDelete = await _permissions.CanAccessAsync(User, "LeagueMatch", "Delete");
            // MVP redesign Step 5e: the "Recompute MVPs" button uses the existing LeagueMatch > BackfillMvps action (no new action, no rescan).
            model.CanRecomputeMvps = await _permissions.CanAccessAsync(User, "LeagueMatch", "BackfillMvps");
            // Step 12f/12g: the export buttons need LeagueExport > Render (one permission for every picture). Today and yesterday are Cairo days; a day with no game greys its button out.
            model.CanExport = await _permissions.CanAccessAsync(User, "LeagueExport", "Render");
            if (model.CanExport)
            {
                var today = LeagueDayRules.Today(DateTime.UtcNow);
                var yesterday = today.AddDays(-1);
                model.ExportTodayKey = LeagueExportDayRules.DayKey(today);
                model.ExportYesterdayKey = LeagueExportDayRules.DayKey(yesterday);
                model.ExportTodayCount = await _history.ExportableCountAsync(today);
                model.ExportYesterdayCount = await _history.ExportableCountAsync(yesterday);
            }
            return View(model);
        }

        // GET /League/MatchDetail/{id} - one match. The History page loads it with fetch (header
        // X-Requested-With) and shows the partial in a dialog; opened directly, or with JavaScript off,
        // it is a normal page around the same partial (spec section 8).
        [HttpGet]
        public async Task<IActionResult> MatchDetail(int id)
        {
            // The champion list is only needed (and only loaded) for someone who may use the stats form
            // or the "Fill from screenshots" panel (Step 13b: its champion boxes and prompt use the same list).
            var canStats = await _permissions.CanAccessAsync(User, "LeagueMatch", "Stats");
            var canImport = await _permissions.CanAccessAsync(User, "LeagueMatch", "ImportStats");
            var model = await _history.GetDetailAsync(id, canStats || canImport);
            if (model is null) return NotFound();
            model.CanDelete = await _permissions.CanAccessAsync(User, "LeagueMatch", "Delete");
            // Step 12e/12g: the Session image button (finished custom game) and the Clash progress button (Clash game) need LeagueExport > Render.
            var canRender = await _permissions.CanAccessAsync(User, "LeagueExport", "Render");
            model.CanSessionImage = canRender && model.ResultImage != null;
            model.CanClashProgress = canRender && model.IsClash && model.ClashEventId != null;
            model.CanEditResult = model.Status == "done" && await _permissions.CanAccessAsync(User, "LeagueMatch", "EditResult");
            // Step 8a3: the hand-pick form for the MVPs (LeagueMatch > SetMvp) needs a finished match with players on a team.
            // Results/export step 1: the "Played on" form (LeagueMatch > EditDate) on a finished match.
            model.CanEditDate = model.Status == "done" && await _permissions.CanAccessAsync(User, "LeagueMatch", "EditDate");
            model.CanSetMvp = model.Status == "done" && model.Teams.Any(t => !t.IsEmpty) && await _permissions.CanAccessAsync(User, "LeagueMatch", "SetMvp");
            // A match where no team has a player has nothing to add stats to (Step 6e5).
            model.CanEditStats = model.Status == "done" && canStats && model.Teams.Any(t => !t.IsEmpty);
            // Step 13a: "Fill from screenshots" (LeagueMatch > ImportStats), a finished match with players on a team.
            model.CanImportStats = model.Status == "done" && model.Teams.Any(t => !t.IsEmpty) && canImport;
            if (!model.CanEditStats && !model.CanImportStats) model.Champions = new();
            var ajax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
            return ajax ? PartialView("_MatchDetail", model) : View(model);
        }

        // GET /League/Profile - the member's own league profile (view + edit in one form).
        // ?copyMain=true pre-fills the form with the main profile's name and picture; nothing is
        // saved until the member presses Save, so no half-finished profile is ever stored.
        [HttpGet]
        public async Task<IActionResult> Profile(bool copyMain = false)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
                return Challenge();

            var profile = await _profiles.GetByUserIdAsync(user.Id);
            var form = profile is null ? new LeagueProfileForm() : LeagueProfileService.ToForm(profile);

            if (copyMain)
            {
                var shown = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.UserName ?? string.Empty;
                form.InGameName = shown.Trim().Length > LeagueProfileService.MaxNameLength
                    ? shown.Trim()[..LeagueProfileService.MaxNameLength]
                    : shown.Trim();
                form.UseMainAvatar = !string.IsNullOrEmpty(user.AvatarUrl);
            }

            return View(await BuildPageAsync(user, form, profile, new Dictionary<string, string>(), copyMain));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(LeagueProfileForm form)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
                return Challenge();

            var errors = await _profiles.SaveOwnAsync(user, form);
            if (errors.Count == 0)
            {
                TempData["Success"] = "League profile saved.";
                return RedirectToAction(nameof(Profile));
            }

            var profile = await _profiles.GetByUserIdAsync(user.Id);
            return View(await BuildPageAsync(user, form, profile, errors, false));
        }

        private async Task<LeagueProfilePageModel> BuildPageAsync(
            ApplicationUser user, LeagueProfileForm form, LeagueProfile? profile,
            Dictionary<string, string> errors, bool copiedFromMain)
        {
            var catalog = await _catalog.GetActiveAsync();
            var model = new LeagueProfilePageModel
            {
                Form = form,
                IsNew = profile is null,
                CopiedFromMain = copiedFromMain,
                Errors = errors,
                HasMainAvatar = !string.IsNullOrEmpty(user.AvatarUrl),
                Catalog = catalog.Select(c => new LeagueChampOption(c.Id, c.Name, false)).ToList(),
                OtherProfiles = await _profiles.GetOtherProfilesAsync(profile?.Id),
                Stats = profile is null ? null : await _history.GetPlayerStatsAsync(profile.Id),
            };

            model.AvatarPreviewUrl = form.RemoveAvatar ? null
                : form.UseMainAvatar && !string.IsNullOrEmpty(user.AvatarUrl) ? user.AvatarUrl
                : profile?.AvatarUrl;

            // Names for the chips that are already selected, retired champions included.
            var allIds = Enum.GetValues<LeagueLane>().SelectMany(l => form.ChampsFor(l)).Distinct().ToList();
            var byId = (await _catalog.GetByIdsAsync(allIds)).ToDictionary(c => c.Id);
            foreach (var lane in Enum.GetValues<LeagueLane>())
            {
                model.Selected[lane] = form.ChampsFor(lane)
                    .Where(byId.ContainsKey)
                    .Select(id => new LeagueChampOption(id, byId[id].Name, !byId[id].IsActive))
                    .ToList();
            }
            return model;
        }
    }
}
