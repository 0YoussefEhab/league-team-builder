using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LeagueTeamBuilder.Controllers
{
    // League match lifecycle (Step 4d, docs/LEAGUE_BUILD_SPEC.md section 3): submit, start, finish, delete,
    // plus EditResult (Step 5b), Stats (Step 5c) and ImportStats (Step 13a) for a finished match.
    // One action per capability so the owner can grant, say, only Start and Finish to an officer group. Like the
    // rest of League every action starts denied for ALL groups, admin included (LeaguePermissions). Every
    // action takes and returns JSON; the anti-forgery token comes in the RequestVerificationToken header.
    [Authorize]
    public class LeagueMatchController : Controller
    {
        private readonly LeagueMatchService _matches;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPermissionChecker _permissions;
        private readonly LeagueProfileService _profiles;

        public LeagueMatchController(LeagueMatchService matches, UserManager<ApplicationUser> userManager, IPermissionChecker permissions, LeagueProfileService profiles)
        {
            _profiles = profiles;
            _matches = matches;
            _userManager = userManager;
            _permissions = permissions;
        }

        // Saves the teams on the builder page as a match with status "submitted".
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit([FromBody] LeagueMatchSubmitRequest? request)
        {
            if (request is null) return BadRequest(Empty());
            return Json(await _matches.SubmitAsync(request, _userManager.GetUserId(User)));
        }

        // submitted -> live.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start([FromBody] LeagueMatchIdRequest? request)
        {
            if (request is null) return BadRequest(Empty());
            return Json(await _matches.StartAsync(request.Id));
        }

        // live -> done. The winner is required; the team kills are optional.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Finish([FromBody] LeagueMatchFinishRequest? request)
        {
            if (request is null) return BadRequest(Empty());
            // A "Played on" in the request needs LeagueMatch > EditDate as well (checked again in the service).
            var canEditDate = await _permissions.CanAccessAsync(User, "LeagueMatch", "EditDate");
            return Json(await _matches.FinishAsync(request, canEditDate));
        }

        // done -> done: change the winner or the team kills of a finished match (Step 5b).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditResult([FromBody] LeagueMatchFinishRequest? request)
        {
            if (request is null) return BadRequest(Empty());
            return Json(await _matches.EditResultAsync(request, _userManager.GetUserId(User)));
        }

        // Results/export step 1: change the "Played on" date of a finished match. Its own permission (LeagueMatch > EditDate),
        // like the "Played on" box on the Finish form. Denied for every group until the owner ticks it.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDate([FromBody] LeagueMatchDateRequest? request)
        {
            if (request is null) return BadRequest(Empty());
            return Json(await _matches.EditDateAsync(request, _userManager.GetUserId(User)));
        }

        // Step 5c: champion, lane played and K/D/A of a finished match, one save per match.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Stats([FromBody] LeagueMatchStatsRequest? request)
        {
            if (request is null) return BadRequest(Empty());
            return Json(await _matches.SaveStatsAsync(request, _userManager.GetUserId(User)));
        }

        // Step 8a2: an admin picks the MVPs of a finished match by hand, or sets Auto = true to go back to the automatic rule.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetMvp([FromBody] LeagueMatchMvpRequest? request)
        {
            if (request is null) return BadRequest(Empty());
            return Json(await _matches.SetMvpAsync(request, _userManager.GetUserId(User)));
        }

        // Step 8a2: one-time fill of the MVPs of matches finished before this step. Safe to run again.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BackfillMvps()
        {
            return Json(await _matches.BackfillMvpsAsync());
        }

        // Step 13a ("Fill from screenshots", step 1): saves the game name of match players who have none, as the Riot ID of their
        // League profile. New action: "Rescan controllers" + grant LeagueMatch > ImportStats to the groups that may use the panel.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportStats([FromBody] LeagueImportNamesRequest? request)
        {
            if (request is null) return BadRequest(new LeagueImportNamesResponse { Ok = false, Error = "The request was empty." });
            return Json(await _profiles.SaveGameNamesAsync(request, User));
        }

        // Any status.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete([FromBody] LeagueMatchIdRequest? request)
        {
            if (request is null) return BadRequest(Empty());
            return Json(await _matches.DeleteAsync(request.Id));
        }

        private static LeagueMatchResponse Empty() => new() { Ok = false, Error = "The request was empty." };
    }
}
