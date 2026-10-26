using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace LeagueTeamBuilder.Controllers
{
    // League settings (docs/LEAGUE_BUILD_SPEC.md sections 3 and 7). A separate controller with a
    // separate action per capability so the owner can grant, for example, only champion management
    // to an officer group. Denied for every group until granted (LeaguePermissions).
    // Step 2 added the champion catalog; Step 3 modes + map, tiers and guests; Step 6c the Clash schedule.
    // Step 7b added guest linking (LinkGuest: search, confirm and link share one action name, so one permission).
    // Admin fixes Step 4d: Guests has an Unlinked / Linked / All filter, and UnlinkGuest (GET confirm + POST) is the
    // manual Unlink for older links with no log entry.
    // Step 8e added EditPlayerRank (admin rank change from the Player page).
    // Step 12h2 added AutoSend (the Discord auto-send switches; page and save share the one action name).
    // Phase B3 added the rank badge pictures (Badges, UploadBadge, RemoveBadge, ResetBadges; Admin only).
    [Authorize]
    public class LeagueSettingsController : Controller
    {
        private readonly ChampionCatalogService _catalog;
        private readonly LeagueSettingsService _settings;
        private readonly LeagueTierService _tiers;
        private readonly LeagueGuestService _guests;
        private readonly LeagueClashScheduleService _clash;
        private readonly LeagueGuestLinkService _links;
        private readonly LeagueProfileService _profiles;
        private readonly LeagueAutoSendService _autoSend;
        private readonly LeagueRankBadgeService _badges;

        public LeagueSettingsController(
            ChampionCatalogService catalog,
            LeagueSettingsService settings,
            LeagueTierService tiers,
            LeagueGuestService guests,
            LeagueClashScheduleService clash,
            LeagueGuestLinkService links,
            LeagueProfileService profiles,
            LeagueAutoSendService autoSend,
            LeagueRankBadgeService badges)
        {
            _badges = badges;
            _autoSend = autoSend;
            _catalog = catalog;
            _settings = settings;
            _tiers = tiers;
            _guests = guests;
            _clash = clash;
            _links = links;
            _profiles = profiles;
        }

        public IActionResult Index() => View();

        public async Task<IActionResult> Champions(string? q)
        {
            ViewData["Query"] = q;
            return View(await _catalog.GetAllWithUsageAsync(q));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddChampion(string? name) =>
            Done(await _catalog.AddAsync(name));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAddChampions(string? names)
        {
            var (added, skipped) = await _catalog.BulkAddAsync(names);
            TempData["Success"] = $"Added {added} champion(s). Skipped {skipped} (blank, too long or already in the catalog).";
            return RedirectToAction(nameof(Champions));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameChampion(int id, string? name) =>
            Done(await _catalog.RenameAsync(id, name));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SetChampionActive(int id, bool active) =>
            Done(await _catalog.SetActiveAsync(id, active));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveChampion(int id) =>
            Done(await _catalog.RemoveAsync(id));

        // ---- Modes, map image and map spots (Step 3) -------------------------------------------

        public async Task<IActionResult> Modes() => View(await _settings.GetAllAsync());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveModes(int normal, int aram, int arena, int arenaTeams) =>
            DoneTo(await _settings.SaveCountsAsync(normal, aram, arena, arenaTeams), nameof(Modes));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadMap(LeagueMode mode, IFormFile? image) =>
            Enum.IsDefined(mode)
                ? DoneTo(await _settings.SetMapImageAsync(mode, image), nameof(Modes))
                : DoneTo((false, "Unknown mode."), nameof(Modes));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMap(LeagueMode mode) =>
            Enum.IsDefined(mode)
                ? DoneTo(await _settings.RemoveMapImageAsync(mode), nameof(Modes))
                : DoneTo((false, "Unknown mode."), nameof(Modes));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSpots(LeagueMode mode, List<LeagueSpotInput>? spots) =>
            Enum.IsDefined(mode)
                ? DoneTo(await _settings.SaveSpotsAsync(mode, spots), nameof(Modes), "spots")
                : DoneTo((false, "Unknown mode."), nameof(Modes));

        // ---- Custom tiers (Step 3) -------------------------------------------------------------

        public async Task<IActionResult> Tiers() =>
            View(new LeagueTiersPageModel
            {
                Tiers = await _tiers.GetTiersAsync(),
                Players = await _tiers.GetPlayersAsync(),
            });

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTier(string? name) =>
            DoneTo(await _tiers.AddAsync(name), nameof(Tiers));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameTier(int id, string? name) =>
            DoneTo(await _tiers.RenameAsync(id, name), nameof(Tiers));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveTier(int id, bool up) =>
            DoneTo(await _tiers.MoveAsync(id, up), nameof(Tiers));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveTier(int id) =>
            DoneTo(await _tiers.RemoveAsync(id), nameof(Tiers));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPlayerTier(int profileId, int? tierId) =>
            DoneTo(await _tiers.SetPlayerTierAsync(profileId, tierId), nameof(Tiers));

        // ---- Guests (Step 3; linking a guest to a user is Step 7b) -------------------------------

        public async Task<IActionResult> Guests(string? filter)
        {
            var f = LeagueGuestFilter.Parse(filter);
            var (rows, unlinked, linked) = await _guests.GetRowsAsync(f);
            return View(new LeagueGuestsPageModel { Guests = rows, Filter = f, UnlinkedCount = unlinked, LinkedCount = linked });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddGuest(LeagueGuestForm form)
        {
            form.Id = 0;
            var (errors, _) = await _guests.SaveAsync(form);
            if (errors.Count == 0)
            {
                TempData["Success"] = $"Added guest {form.InGameName?.Trim()}.";
                return RedirectToAction(nameof(Guests));
            }
            var (rows, unlinked, linked) = await _guests.GetRowsAsync(LeagueGuestFilter.Unlinked);
            return View(nameof(Guests), new LeagueGuestsPageModel
            {
                Guests = rows, Filter = LeagueGuestFilter.Unlinked, UnlinkedCount = unlinked, LinkedCount = linked,
                Form = form, Errors = errors,
            });
        }

        [HttpGet]
        public async Task<IActionResult> EditGuest(int id)
        {
            var form = await _guests.GetFormAsync(id);
            if (form is null)
            {
                TempData["Error"] = "That guest no longer exists.";
                return RedirectToAction(nameof(Guests));
            }
            return View(new LeagueGuestEditPageModel { Form = form });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditGuest(LeagueGuestForm form)
        {
            var (errors, _) = await _guests.SaveAsync(form);
            if (errors.Count == 0)
            {
                TempData["Success"] = $"Saved guest {form.InGameName?.Trim()}.";
                return RedirectToAction(nameof(Guests));
            }
            if (errors.ContainsKey("Guest"))
            {
                TempData["Error"] = errors["Guest"];
                return RedirectToAction(nameof(Guests));
            }
            return View(new LeagueGuestEditPageModel { Form = form, Errors = errors });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteGuest(int id) =>
            DoneTo(await _guests.DeleteAsync(id), nameof(Guests));

        // ---- Edit a guest's names (Step 3 of PLAN-admin-fixes.md) ---------------------------------
        // In-game name and Riot ID (Name#TAG, optional). Past matches are rewritten to the new in-game name.
        // One action name for GET and POST, so one permission (LeagueSettings > EditGuestNames).

        [HttpGet]
        public async Task<IActionResult> EditGuestNames(int id)
        {
            var form = await _guests.GetFormAsync(id);
            if (form is null)
            {
                TempData["Error"] = "That guest no longer exists.";
                return RedirectToAction(nameof(Guests));
            }
            var riotId = await _profiles.GetRiotIdAsync(id);
            return View(new LeagueGuestNamesPageModel
            {
                Form = new AdminNamesEdit { ProfileId = id, IncludeLeague = true, InGameName = form.InGameName, RiotId = riotId },
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditGuestNames(AdminNamesEdit form)
        {
            form.UserId = null; // this screen is for guests only
            var (ok, message, errors) = await _profiles.EditNamesAsync(form, User);
            if (ok)
            {
                TempData["Success"] = message;
                return RedirectToAction(nameof(Guests));
            }
            if (errors.ContainsKey("Guest"))
            {
                TempData["Error"] = message;
                return RedirectToAction(nameof(Guests));
            }
            return View(new LeagueGuestNamesPageModel { Form = form, Errors = errors });
        }

        // ---- Link a guest to a user (Step 7b) ---------------------------------------------------
        // One action name for the whole flow (search, confirm, link) so a single permission covers it.

        [HttpGet]
        public async Task<IActionResult> LinkGuest(int id, string? q, string? userId)
        {
            var guest = await _guests.GetFormAsync(id);
            if (guest is null)
            {
                TempData["Error"] = "That guest no longer exists. It may already have been linked.";
                return RedirectToAction(nameof(Guests));
            }

            var model = new LeagueLinkPageModel { GuestId = id, GuestName = guest.InGameName ?? "", Query = q };
            if (!string.IsNullOrWhiteSpace(userId))
            {
                model.Preview = await _links.GetPreviewAsync(id, userId);
                if (model.Preview is null)
                {
                    TempData["Error"] = "That user can no longer be linked (not found, deactivated, or their group lost League access).";
                    return RedirectToAction(nameof(LinkGuest), new { id, q });
                }
            }
            else
            {
                model.Users = await _links.GetLinkableUsersAsync(q);
            }
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> LinkGuest(LeagueLinkRequest request)
        {
            var result = await _links.LinkAsync(request, User);
            TempData[result.Ok ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(Guests));
        }

        // ---- Manual Unlink (Step 4d of PLAN-admin-fixes.md) ----------------------------------------
        // For a member profile that came from a guest but has no link-log entry. One action name for the confirm
        // screen (GET) and the change (POST), so one permission (LeagueSettings > UnlinkGuest).

        [HttpGet]
        public async Task<IActionResult> UnlinkGuest(int id)
        {
            var preview = await _links.GetUnlinkPreviewAsync(id);
            if (preview is null)
            {
                TempData["Error"] = "That profile is not a linked member any more.";
                return RedirectToAction(nameof(Guests), new { filter = LeagueGuestFilter.Linked });
            }
            return View(preview);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlinkGuest(int id, bool confirmed)
        {
            if (!confirmed)
            {
                TempData["Error"] = "Tick the box to confirm the unlink. Nothing was changed.";
                return RedirectToAction(nameof(UnlinkGuest), new { id });
            }
            var result = await _links.UnlinkAsync(id, User);
            TempData[result.Ok ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(Guests), new { filter = result.Ok ? LeagueGuestFilter.Unlinked : LeagueGuestFilter.Linked });
        }

        // ---- Link log, Undo link, Change user (Step 4e of PLAN-admin-fixes.md) ----------------------
        // LinkedGuests = the list (LeagueSettings > LinkedGuests). UndoGuestLink = the change (LeagueSettings > UndoGuestLink).
        // "Change user" is Undo followed by the existing Link screen for the guest that came back.

        [HttpGet]
        public async Task<IActionResult> LinkedGuests() =>
            View(await _links.GetLinkLogAsync());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UndoGuestLink(int id, bool changeUser)
        {
            var result = await _links.UndoLinkAsync(id, User);
            if (result.Ok && changeUser)
            {
                TempData["Success"] = result.Message + " Choose the right user below.";
                return RedirectToAction(nameof(LinkGuest), new { id = result.GuestId });
            }
            TempData[result.Ok ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(LinkedGuests));
        }

        // ---- Player rank (Step 8e) ---------------------------------------------------------------
        // Changes the current and peak rank of any league profile, member or guest. Its own permission action
        // (LeagueSettings > EditPlayerRank). The form lives on the Player page; the result comes back as a message there.

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPlayerRank(LeagueRankEditRequest request)
        {
            var (ok, message) = await _profiles.SetRankAsync(request, User);
            TempData[ok ? "Success" : "Error"] = message;
            return RedirectToAction("Player", "League", new { id = request.ProfileId });
        }

        // ---- Player names (2026-10-18) ------------------------------------------------------------
        // Changes the in-game name and Riot ID of any league profile, member or guest, from the Player page. Its own
        // permission action (LeagueSettings > EditPlayerNames, see RolePermissions). Never changes the user account.

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPlayerNames(AdminNamesEdit form)
        {
            var profileId = form.ProfileId;
            var request = new AdminNamesEdit
            {
                ProfileId = profileId, IncludeLeague = true, InGameName = form.InGameName, RiotId = form.RiotId,
            };
            var (ok, message, errors) = await _profiles.EditNamesAsync(request, User, anyProfile: true);
            if (ok) TempData["Success"] = message;
            else TempData["Error"] = errors.Count > 0 && !errors.ContainsKey("Guest") ? string.Join(" ", errors.Values) : message;
            return RedirectToAction("Player", "League", new { id = profileId });
        }

        // ---- Clash schedule (Step 6c) -----------------------------------------------------------
        // Separate actions per capability: ClashSchedule (the list), CreateClash, EditClash, CancelClash.

        public async Task<IActionResult> ClashSchedule() =>
            View(new LeagueClashSchedulePageModel { Events = await _clash.GetScheduleAsync() });

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateClash(LeagueClashForm form)
        {
            form.Id = 0;
            var (errors, id, notified) = await _clash.CreateAsync(form, User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (errors.Count == 0 && id != null)
            {
                TempData["Success"] = $"Scheduled {form.Title?.Trim()}. {notified} member(s) were notified.";
                return RedirectToAction(nameof(ClashSchedule));
            }
            return View(nameof(ClashSchedule), new LeagueClashSchedulePageModel
            {
                Events = await _clash.GetScheduleAsync(), Form = form, Errors = errors,
            });
        }

        [HttpGet]
        public async Task<IActionResult> EditClash(int id)
        {
            var form = await _clash.GetFormAsync(id);
            if (form is null)
            {
                TempData["Error"] = "That clash no longer exists.";
                return RedirectToAction(nameof(ClashSchedule));
            }
            return View(new LeagueClashEditPageModel { Form = form });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditClash(LeagueClashForm form)
        {
            var (errors, notified, dateChanged) = await _clash.EditAsync(form);
            if (errors.Count == 0)
            {
                TempData["Success"] = dateChanged
                    ? $"Saved {form.Title?.Trim()}. The new time was sent to {notified} member(s)."
                    : $"Saved {form.Title?.Trim()}.";
                return RedirectToAction(nameof(ClashSchedule));
            }
            if (errors.ContainsKey("Event"))
            {
                TempData["Error"] = errors["Event"];
                return RedirectToAction(nameof(ClashSchedule));
            }
            return View(new LeagueClashEditPageModel { Form = form, Errors = errors });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelClash(int id) =>
            DoneTo(await _clash.CancelAsync(id), nameof(ClashSchedule));

        // ---- Discord auto-send (Step 12h2) --------------------------------------------------------
        // One action name for the page and its save (GET + POST) so a single permission covers it.

        [HttpGet]
        public async Task<IActionResult> AutoSend() => View(await _autoSend.GetAsync());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AutoSend(bool gameResult, bool clashProgress)
        {
            await _autoSend.SaveAsync(gameResult, clashProgress, User.FindFirstValue(ClaimTypes.NameIdentifier));
            TempData["Success"] = "Discord auto-send saved.";
            return RedirectToAction(nameof(AutoSend));
        }

        // ---- Rank badge pictures (Phase B3) ----------------------------------------------------

        public async Task<IActionResult> Badges() => View(await _badges.GetAllAsync());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadBadge(LeagueRank tier, IFormFile? image) =>
            Enum.IsDefined(tier)
                ? DoneTo(await _badges.SetAsync(tier, image), nameof(Badges), BadgeAnchor(tier))
                : DoneTo((false, "Unknown rank."), nameof(Badges));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveBadge(LeagueRank tier) =>
            Enum.IsDefined(tier)
                ? DoneTo(await _badges.RemoveAsync(tier), nameof(Badges), BadgeAnchor(tier))
                : DoneTo((false, "Unknown rank."), nameof(Badges));

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetBadges() =>
            DoneTo(await _badges.ResetAllAsync(), nameof(Badges));

        private static string BadgeAnchor(LeagueRank tier) => "badge-" + tier.ToString().ToLowerInvariant();

        private IActionResult DoneTo((bool Ok, string Message) result, string action, string? fragment = null)
        {
            TempData[result.Ok ? "Success" : "Error"] = result.Message;
            return RedirectToAction(action, null, null, fragment);
        }

        private IActionResult Done((bool Ok, string Message) result)
        {
            TempData[result.Ok ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(Champions));
        }
    }
}
