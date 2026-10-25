using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeagueTeamBuilder.Controllers
{
    // Results/export step 12g: ONE permission (LeagueExport > Render) for every Discord picture of results. Like every League
    // controller it is denied for every group until the owner ticks it on Admin > Groups > Permissions (Rescan controllers first).
    // It replaces the three separate permissions of 12d-12f (League > SessionImage, League > ExportData, LeagueClash > ProgressImage).
    // 12h1 adds LeagueExport > Send: posting a drawn picture to the League Discord channel (the only action that writes anything).
    // Render is read-only: it only builds the data of a picture; the browser draws it (league-result-image.js, league-export-day.js).
    [Authorize]
    public class LeagueExportController : Controller
    {
        private readonly LeagueHistoryService _history;
        private readonly LeagueClashBoardService _board;
        private readonly LeagueDiscordSender _discord;

        public LeagueExportController(LeagueHistoryService history, LeagueClashBoardService board, LeagueDiscordSender discord)
        {
            _history = history;
            _board = board;
            _discord = discord;
        }

        // GET /LeagueExport/Render?kind=...  - JSON out.
        //   kind=session&matchId=5                    the Session picture of the block of games that holds this match (12e)
        //   kind=result&matchId=5                     the Game result picture of one finished custom game (12h2, used by the auto-send)
        //   kind=clash&eventId=5[&teamId=2]           the Clash progress picture of one team (12d)
        //   kind=day&day=2026-10-17[&ids=1,2,3&mode=session|games|none]
        //                                             the export dialog: the day's checklist, or the overlap and the pictures' data (12f)
        [HttpGet]
        public async Task<IActionResult> Render(string? kind, int matchId, int eventId, int? teamId, string? day, string? ids, string? mode)
        {
            switch ((kind ?? "").Trim().ToLowerInvariant())
            {
                case "session":
                    return Json(await _history.SessionImageAsync(matchId));
                case "result":
                    return Json(await _history.GameImageAsync(matchId));
                case "clash":
                    return Json(await _board.ProgressImageAsync(eventId, teamId));
                case "day":
                    return Json(await DayAsync(day, ids, mode));
                default:
                    return Json(new LeagueExportResponse { Ok = false, Error = "Unknown picture kind." });
            }
        }

        // POST /LeagueExport/Send (multipart, field "file" = a 1280 x 720 PNG the browser drew) - JSON out { ok, error } (12h1).
        // Posts the picture to the League Discord channel. Its own permission (LeagueExport > Send, denied until ticked) because it
        // writes to a shared channel; Render only reads. The server checks it really is a 1280 x 720 PNG under 8 MB.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(LeaguePictureRules.MaxBytes + 65536)]
        public async Task<IActionResult> Send(IFormFile? file)
        {
            if (!_discord.IsConfigured)
                return Json(new { ok = false, error = "Sending to Discord is not set up (Discord__LeagueWebhookUrl is missing)." });
            if (file == null || file.Length == 0 || file.Length > LeaguePictureRules.MaxBytes)
                return Json(new { ok = false, error = "The picture is missing or too big." });
            byte[] data;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                data = ms.ToArray();
            }
            if (!LeaguePictureRules.IsLeaguePng(data))
                return Json(new { ok = false, error = "That is not a 1280 x 720 PNG picture." });
            var (ok, error) = await _discord.SendImageAsync(data, LeaguePictureRules.SafeFileName(file.FileName));
            return Json(new { ok, error });
        }

        private async Task<LeagueExportResponse> DayAsync(string? day, string? ids, string? mode)
        {
            if (!LeagueExportDayRules.TryParseDay(day, out var d))
                return new LeagueExportResponse { Ok = false, Error = "Pick a valid day." };
            var list = new List<int>();
            foreach (var part in (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var id) && id > 0 && !list.Contains(id)) list.Add(id);
                if (list.Count > 100) break;
            }
            var m = mode is "session" or "games" ? mode : "none";
            return await _history.ExportDataAsync(d, list, m);
        }
    }
}
