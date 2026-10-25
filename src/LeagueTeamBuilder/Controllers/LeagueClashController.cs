using System.Security.Claims;
using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeagueTeamBuilder.Controllers
{
    // Clash (docs/LEAGUE_BUILD_SPEC.md section 4). 6c: the member response page (the link in the notification).
    // 6d: the Clash page (Index) and its JSON endpoint (Build). 6e4a: EndClash. 6e4d: NextGame (server only; the button is 6e4e). 8i: SetGuestBusy.
    // 11c: CreateTeam, RenameTeam, DeleteTeam (several teams per clash; the screen for them is 11d). 12d: ProgressImage (moved to LeagueExport/Render in 12g). Denied for every group, admin included,
    // until the owner ticks it (LeaguePermissions); the notification only goes to groups that have Respond ticked.
    [Authorize]
    public class LeagueClashController : Controller
    {
        private readonly LeagueClashScheduleService _clash;
        private readonly LeagueClashBoardService _board;

        public LeagueClashController(LeagueClashScheduleService clash, LeagueClashBoardService board)
        {
            _clash = clash;
            _board = board;
        }

        // GET /LeagueClash?id=5 - the Clash page. Without an id it opens on the running or next upcoming clash.
        [HttpGet]
        public async Task<IActionResult> Index(int? id) => View(await _board.GetPageAsync(id));

        // POST /LeagueClash/Build - JSON in, JSON out. One action for building the team and for rechecking it after a
        // hand edit (one permission to tick). Read-only: nothing is saved here. The anti-forgery token comes in the
        // RequestVerificationToken header, like POST /League/Generate.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Build([FromBody] LeagueClashBuildRequest? request)
        {
            if (request is null)
                return BadRequest(new LeagueClashBuildResponse { Ok = false, Error = "The request was empty." });
            return Json(await _board.RunAsync(request));
        }

        // POST /LeagueClash/EndClash - JSON in, JSON out (6e4a). Sets the clash Done. A separate action so it is a separate
        // permission to tick. The anti-forgery token comes in the RequestVerificationToken header, like Build.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EndClash([FromBody] LeagueClashEndRequest? request)
        {
            if (request is null)
                return BadRequest(new LeagueClashEndResponse { Ok = false, Error = "The request was empty." });
            var (ok, message) = await _clash.EndAsync(request.EventId);
            return Json(new LeagueClashEndResponse { Ok = ok, Error = ok ? null : message, Message = ok ? message : null });
        }

        // POST /LeagueClash/CreateTeam, RenameTeam, DeleteTeam - JSON in, JSON out (11c). Several teams can play the same clash. Three
        // separate actions so they are three permissions to tick (Rescan controllers first). The anti-forgery token comes in the
        // RequestVerificationToken header, like Build.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTeam([FromBody] LeagueClashTeamCreateRequest? request)
        {
            if (request is null)
                return BadRequest(new LeagueClashTeamResponse { Ok = false, Error = "The request was empty." });
            return Json(await _clash.CreateTeamAsync(request.EventId, request.Name));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameTeam([FromBody] LeagueClashTeamRenameRequest? request)
        {
            if (request is null)
                return BadRequest(new LeagueClashTeamResponse { Ok = false, Error = "The request was empty." });
            return Json(await _clash.RenameTeamAsync(request.TeamId, request.Name));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTeam([FromBody] LeagueClashTeamDeleteRequest? request)
        {
            if (request is null)
                return BadRequest(new LeagueClashTeamResponse { Ok = false, Error = "The request was empty." });
            return Json(await _clash.DeleteTeamAsync(request.TeamId));
        }

        // POST /LeagueClash/SetGuestBusy - JSON in, JSON out (8i). Saves a guest's free / busy answer for a clash (the Busy button
        // on a guest row of the Clash page). A separate action so it is a separate permission to tick. The anti-forgery token
        // comes in the RequestVerificationToken header, like Build.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetGuestBusy([FromBody] LeagueClashGuestBusyRequest? request)
        {
            if (request is null)
                return BadRequest(new LeagueClashGuestBusyResponse { Ok = false, Error = "The request was empty." });
            var (ok, message) = await _clash.SetGuestBusyAsync(request.EventId, request.ProfileId, request.Busy);
            return Json(new LeagueClashGuestBusyResponse { Ok = ok, Error = ok ? null : message, Message = ok ? message : null, Busy = request.Busy });
        }

        // POST /LeagueClash/NextGame - JSON in, JSON out (6e4d). Returns the last finished game's team as slots, rechecked, so
        // the same team survives a page reload. Read-only: nothing is saved (Save as game N does that). A separate action so it
        // is a separate permission to tick. The anti-forgery token comes in the RequestVerificationToken header, like Build.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NextGame([FromBody] LeagueClashNextGameRequest? request)
        {
            if (request is null)
                return BadRequest(new LeagueClashNextGameResponse { Ok = false, Error = "The request was empty." });
            return Json(await _board.NextGameAsync(request));
        }

        // GET /LeagueClash/Respond/5
        [HttpGet]
        public async Task<IActionResult> Respond(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Forbid();
            var model = await _clash.GetRespondPageAsync(id, userId);
            if (model == null)
            {
                TempData["Error"] = "That clash no longer exists.";
                return RedirectToAction("Index", "League");
            }
            return View(model);
        }

        // POST /LeagueClash/SaveResponse: the profile is always the signed-in user's own; the form only says free or busy.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveResponse(int id, string? answer)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Forbid();
            var (ok, message) = await _clash.RespondAsync(id, userId, answer);
            TempData[ok ? "Success" : "Error"] = message;
            return RedirectToAction(nameof(Respond), new { id });
        }
    }
}
