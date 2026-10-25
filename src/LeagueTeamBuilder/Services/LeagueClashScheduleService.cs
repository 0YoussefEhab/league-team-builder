using System.Globalization;
using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Clash scheduling (docs/LEAGUE_BUILD_SPEC.md section 4): create / edit / cancel an event, the in-site
    // notifications, and the members' free / busy answers. The Clash page and the series (6d, 6e) come later.
    public class LeagueClashScheduleService
    {
        public const int MaxTitle = 100;
        public const string RespondController = "LeagueClash";
        public const string RespondAction = "Respond";

        private readonly ApplicationDbContext _db;
        private readonly NotificationService _notifications;
        private readonly ILogger<LeagueClashScheduleService> _logger;

        public LeagueClashScheduleService(ApplicationDbContext db, NotificationService notifications, ILogger<LeagueClashScheduleService> logger)
        {
            _db = db;
            _notifications = notifications;
            _logger = logger;
        }

        // ------------------------------------------------------------------ pure helpers (unit-tested)

        // The script sends an ISO "...Z" time; without JavaScript only the datetime-local text arrives and it is read as UTC.
        public static bool TryParseStart(string? utcText, string? localText, out DateTime utc)
        {
            const DateTimeStyles styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;
            foreach (var text in new[] { utcText, localText })
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, styles, out var d))
                {
                    utc = DateTime.SpecifyKind(new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0), DateTimeKind.Utc);
                    return true;
                }
            }
            utc = default;
            return false;
        }

        public static bool IsClashMode(LeagueMode mode) => mode == LeagueMode.Normal || mode == LeagueMode.Aram;

        public static string ModeName(LeagueMode mode) => mode == LeagueMode.Aram ? "ARAM" : "Normal Draft";

        public static string StatusName(LeagueClashStatus s) => s switch
        {
            LeagueClashStatus.Open => "Open",
            LeagueClashStatus.InProgress => "In progress",
            LeagueClashStatus.Done => "Done",
            _ => "Cancelled",
        };

        public static string ScheduledMessage(string title, DateTime startsUtc) =>
            $"Clash scheduled: {title}, {LeagueHistoryFormat.Plain(startsUtc)}. Tell the team if you can join.";

        public static string MovedMessage(string title, DateTime startsUtc) =>
            $"Clash moved: {title} is now {LeagueHistoryFormat.Plain(startsUtc)}. Check that you can still join.";

        public static string CancelledMessage(string title, DateTime startsUtc) =>
            $"Clash cancelled: {title}, {LeagueHistoryFormat.Plain(startsUtc)}.";

        public static string RespondLink(int eventId) => $"/LeagueClash/Respond/{eventId}";

        // Title 1-100 characters, a Normal or ARAM mode, a start time (in the future when requireFuture).
        public static Dictionary<string, string> Validate(LeagueClashForm form, bool requireFuture, DateTime nowUtc, out DateTime startsUtc)
        {
            var errors = new Dictionary<string, string>();
            var title = (form.Title ?? "").Trim();
            if (title.Length == 0) errors["Title"] = "Give the clash a title.";
            else if (title.Length > MaxTitle) errors["Title"] = $"The title can have at most {MaxTitle} characters.";
            if (!IsClashMode(form.Mode)) errors["Mode"] = "A clash is Normal Draft or ARAM.";
            if (!TryParseStart(form.StartsAtUtc, form.StartsAtLocal, out startsUtc)) errors["StartsAt"] = "Pick the date and time.";
            else if (requireFuture && startsUtc <= nowUtc) errors["StartsAt"] = "Pick a time in the future.";
            return errors;
        }

        // ------------------------------------------------------------------ settings side

        public async Task<List<LeagueClashRow>> GetScheduleAsync()
        {
            var events = await _db.LeagueClashEvents.AsNoTracking().ToListAsync();
            var answers = await _db.LeagueClashResponses.AsNoTracking()
                .GroupBy(r => new { r.EventId, r.Status })
                .Select(g => new { g.Key.EventId, g.Key.Status, N = g.Count() })
                .ToListAsync();
            var games = await _db.LeagueMatches.AsNoTracking()
                .Where(m => m.ClashEventId != null)
                .GroupBy(m => m.ClashEventId)
                .Select(g => new { Id = g.Key, N = g.Count() })
                .ToListAsync();

            var rows = events.Select(e => new LeagueClashRow
            {
                Id = e.Id,
                Title = e.Title,
                StartsAtUtc = e.StartsAtUtc,
                Mode = e.Mode,
                Status = e.Status,
                CanJoin = answers.Where(a => a.EventId == e.Id && a.Status == LeagueClashAnswer.Free).Sum(a => a.N),
                Busy = answers.Where(a => a.EventId == e.Id && a.Status == LeagueClashAnswer.Busy).Sum(a => a.N),
                Games = games.Where(g => g.Id == e.Id).Sum(g => g.N),
            }).ToList();

            // Open and in-progress clashes first (soonest first), then the rest, newest first.
            return rows.Where(r => r.Status is LeagueClashStatus.Open or LeagueClashStatus.InProgress).OrderBy(r => r.StartsAtUtc)
                .Concat(rows.Where(r => r.Status is LeagueClashStatus.Done or LeagueClashStatus.Cancelled).OrderByDescending(r => r.StartsAtUtc))
                .ToList();
        }

        public async Task<LeagueClashForm?> GetFormAsync(int id)
        {
            var e = await _db.LeagueClashEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (e == null) return null;
            return new LeagueClashForm
            {
                Id = e.Id,
                Title = e.Title,
                StartsAtUtc = LeagueHistoryFormat.Iso(e.StartsAtUtc),
                // The fallback for no JavaScript (read as UTC); the script replaces it with the local time.
                StartsAtLocal = e.StartsAtUtc.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
                Mode = e.Mode,
            };
        }

        public async Task<(Dictionary<string, string> Errors, int? Id, int Notified)> CreateAsync(LeagueClashForm form, string? userId)
        {
            var now = DateTime.UtcNow;
            var errors = Validate(form, requireFuture: true, now, out var starts);
            if (errors.Count > 0) return (errors, null, 0);

            var ev = new LeagueClashEvent
            {
                Title = form.Title!.Trim(),
                StartsAtUtc = starts,
                Mode = form.Mode,
                Status = LeagueClashStatus.Open,
                CreatedUtc = now,
                CreatedByUserId = userId,
            };
            _db.LeagueClashEvents.Add(ev);
            await _db.SaveChangesAsync();

            var notified = await NotifyAsync(ev.Id, ScheduledMessage(ev.Title, ev.StartsAtUtc));
            return (errors, ev.Id, notified);
        }

        // Only an open clash can be edited. A new date re-notifies; title and mode edits do not (spec section 4).
        // The mode cannot change once a game exists (it would not match the saved game).
        public async Task<(Dictionary<string, string> Errors, int Notified, bool DateChanged)> EditAsync(LeagueClashForm form)
        {
            var ev = await _db.LeagueClashEvents.FirstOrDefaultAsync(e => e.Id == form.Id);
            if (ev == null) return (new() { ["Event"] = "That clash no longer exists." }, 0, false);
            if (ev.Status != LeagueClashStatus.Open)
                return (new() { ["Event"] = "Only a clash that has not started can be edited." }, 0, false);

            // The date must be in the future only when it changes (a title fix after the start time is fine).
            var errors = Validate(form, requireFuture: false, DateTime.UtcNow, out var starts);
            var dateChanged = errors.Count == 0 && starts != ev.StartsAtUtc;
            if (dateChanged && starts <= DateTime.UtcNow) errors["StartsAt"] = "Pick a time in the future.";
            if (errors.Count == 0 && form.Mode != ev.Mode && await _db.LeagueMatches.AnyAsync(m => m.ClashEventId == ev.Id))
                errors["Mode"] = "The mode cannot change once a game has been created for this clash.";
            if (errors.Count > 0) return (errors, 0, false);

            ev.Title = form.Title!.Trim();
            ev.Mode = form.Mode;
            ev.StartsAtUtc = starts;
            await _db.SaveChangesAsync();

            var notified = dateChanged ? await NotifyAsync(ev.Id, MovedMessage(ev.Title, ev.StartsAtUtc)) : 0;
            return (errors, notified, dateChanged);
        }

        // Cancel = status Cancelled (owner decision), members are told. Only while the clash is Open: one conditional
        // UPDATE, so a double click or a game start at the same moment cannot cancel twice or cancel a running clash.
        public async Task<(bool Ok, string Message)> CancelAsync(int id)
        {
            var ev = await _db.LeagueClashEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
            if (ev == null) return (false, "That clash no longer exists.");
            var now = DateTime.UtcNow;
            var rows = await _db.LeagueClashEvents
                .Where(e => e.Id == id && e.Status == LeagueClashStatus.Open)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, LeagueClashStatus.Cancelled).SetProperty(e => e.CancelledUtc, (DateTime?)now));
            if (rows == 0) return (false, "Only a clash that has not started can be cancelled. Reload the page.");

            var notified = await NotifyAsync(ev.Id, CancelledMessage(ev.Title, ev.StartsAtUtc));
            return (true, $"Cancelled {ev.Title}. {notified} member(s) were notified.");
        }

        // Step 6e4a: End clash = status Done, EndedUtc set. One conditional UPDATE: the clash must still be InProgress and
        // have no game that is not Done, so a double click, a cancel, or a game saved at the same moment cannot end it wrongly.
        // Nobody is notified. Nothing is deleted: the games stay in History.
        public async Task<(bool Ok, string Message)> EndAsync(int id)
        {
            var status = await _db.LeagueClashEvents.AsNoTracking().Where(e => e.Id == id)
                .Select(e => (LeagueClashStatus?)e.Status).FirstOrDefaultAsync();
            if (status == null) return (false, "That clash no longer exists.");
            var games = await _db.LeagueMatches.AsNoTracking().Where(m => m.ClashEventId == id)
                .Select(m => m.Status).ToListAsync();
            var error = LeagueClashGameRules.CheckEnd(status.Value, games);
            if (error != null) return (false, error);

            var now = DateTime.UtcNow;
            var rows = await _db.LeagueClashEvents
                .Where(e => e.Id == id && e.Status == LeagueClashStatus.InProgress
                    && !_db.LeagueMatches.Any(m => m.ClashEventId == id && m.Status != LeagueMatchStatus.Done))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, LeagueClashStatus.Done).SetProperty(e => e.EndedUtc, (DateTime?)now));
            if (rows == 0) return (false, "The clash changed just now, so it was not ended. Reload the page.");
            return (true, "The clash has ended.");
        }

        // ------------------------------------------------------------------ teams (Step 11c)

        private static LeagueClashTeamResponse TeamFail(string error) => new() { Ok = false, Error = error };

        private static LeagueClashTeamResponse TeamOk(string message, LeagueClashTeam? team, int games) => new()
        {
            Ok = true,
            Message = message,
            Team = team == null ? null : new LeagueClashTeamDto { Id = team.Id, Name = team.Name, SortOrder = team.SortOrder, Games = games },
        };

        // Adds a team to an Open or InProgress clash. A clash that has no team yet gets "Team one" first, so the new one is never
        // the first by accident. At most 4 teams, name 1-30 characters, no two with the same name (ignoring case).
        public async Task<LeagueClashTeamResponse> CreateTeamAsync(int eventId, string? name)
        {
            var status = await _db.LeagueClashEvents.AsNoTracking().Where(e => e.Id == eventId)
                .Select(e => (LeagueClashStatus?)e.Status).FirstOrDefaultAsync();
            if (status == null) return TeamFail("That clash no longer exists.");
            var openError = LeagueClashTeamRules.CheckClashOpen(status.Value);
            if (openError != null) return TeamFail(openError);
            var (clean, nameError) = LeagueClashTeamRules.CleanName(name);
            if (nameError != null) return TeamFail(nameError);

            await LeagueClashTeamData.EnsureFirstTeamIdAsync(_db, eventId);
            var existing = await _db.LeagueClashTeams.AsNoTracking().Where(t => t.EventId == eventId)
                .Select(t => new { t.Name, t.SortOrder }).ToListAsync();
            var error = LeagueClashTeamRules.CheckCreate(existing.Select(t => t.Name).ToList(), clean);
            if (error != null) return TeamFail(error);

            var team = new LeagueClashTeam
            {
                EventId = eventId,
                Name = clean,
                SortOrder = LeagueClashTeamRules.NextSortOrder(existing.Select(t => t.SortOrder)),
                CreatedUtc = DateTime.UtcNow,
            };
            _db.LeagueClashTeams.Add(team);
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                _db.Entry(team).State = EntityState.Detached;
                return TeamFail("The team could not be saved. A team with that name may have been added at the same moment. Reload the page.");
            }
            return TeamOk($"{clean} was added.", team, 0);
        }

        public async Task<LeagueClashTeamResponse> RenameTeamAsync(int teamId, string? name)
        {
            var team = await _db.LeagueClashTeams.FirstOrDefaultAsync(t => t.Id == teamId);
            if (team == null) return TeamFail("That team no longer exists. Reload the page.");
            var status = await _db.LeagueClashEvents.AsNoTracking().Where(e => e.Id == team.EventId)
                .Select(e => (LeagueClashStatus?)e.Status).FirstOrDefaultAsync();
            if (status == null) return TeamFail("That clash no longer exists.");
            var openError = LeagueClashTeamRules.CheckClashOpen(status.Value);
            if (openError != null) return TeamFail(openError);
            var (clean, nameError) = LeagueClashTeamRules.CleanName(name);
            if (nameError != null) return TeamFail(nameError);

            var siblings = await _db.LeagueClashTeams.AsNoTracking().Where(t => t.EventId == team.EventId)
                .Select(t => new { t.Id, t.Name }).ToListAsync();
            var error = LeagueClashTeamRules.CheckRename(siblings.Select(t => (t.Id, t.Name)).ToList(), teamId, clean);
            if (error != null) return TeamFail(error);

            team.Name = clean;
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                _db.Entry(team).State = EntityState.Detached;
                return TeamFail("The team could not be renamed. A team with that name may have been added at the same moment. Reload the page.");
            }
            var games = await _db.LeagueMatches.CountAsync(m => m.ClashTeamId == teamId);
            return TeamOk($"The team is now called {clean}.", team, games);
        }

        // Only a team with no games, and never the last team of a clash. The delete itself re-checks "no games" in one statement.
        public async Task<LeagueClashTeamResponse> DeleteTeamAsync(int teamId)
        {
            var team = await _db.LeagueClashTeams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
            if (team == null) return TeamFail("That team no longer exists. Reload the page.");
            var status = await _db.LeagueClashEvents.AsNoTracking().Where(e => e.Id == team.EventId)
                .Select(e => (LeagueClashStatus?)e.Status).FirstOrDefaultAsync();
            if (status == null) return TeamFail("That clash no longer exists.");
            var openError = LeagueClashTeamRules.CheckClashOpen(status.Value);
            if (openError != null) return TeamFail(openError);

            var games = await _db.LeagueMatches.CountAsync(m => m.ClashTeamId == teamId);
            var teamCount = await _db.LeagueClashTeams.CountAsync(t => t.EventId == team.EventId);
            var error = LeagueClashTeamRules.CheckDelete(games, teamCount);
            if (error != null) return TeamFail(error);

            var rows = await _db.LeagueClashTeams
                .Where(t => t.Id == teamId && !_db.LeagueMatches.Any(m => m.ClashTeamId == teamId))
                .ExecuteDeleteAsync();
            if (rows == 0) return TeamFail("The team changed just now, so it was not deleted. Reload the page.");
            return TeamOk($"{team.Name} was deleted.", null, 0);
        }

        // ------------------------------------------------------------------ notifications

        // Spec section 4 + owner decision: one personal notification per non-guest league profile whose group may open the
        // response page (LeagueClash > Respond ticked), so nobody gets a link that ends on "no access". Guests never.
        public async Task<List<string>> GetRecipientUserIdsAsync()
        {
            var groupIds = _db.GroupPermissions
                .Where(p => p.ControllerName == RespondController && p.ActionName == RespondAction && p.Allowed)
                .Select(p => p.PermissionGroupId);

            return await (from lp in _db.LeagueProfiles
                          where !lp.IsGuest && lp.UserId != null
                          join u in _db.Users on lp.UserId equals u.Id
                          where u.PermissionGroupId != null && groupIds.Contains(u.PermissionGroupId.Value)
                          select u.Id).Distinct().ToListAsync();
        }

        // The event is already saved when this runs, so a notification problem is logged and never breaks the schedule.
        private async Task<int> NotifyAsync(int eventId, string message)
        {
            var sent = 0;
            try
            {
                foreach (var userId in await GetRecipientUserIdsAsync())
                {
                    await _notifications.CreateForUserAsync(userId, message, RespondLink(eventId));
                    sent++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send every Clash notification for event {EventId} ({Sent} sent).", eventId, sent);
            }
            return sent;
        }

        // ------------------------------------------------------------------ member side

        public async Task<LeagueClashRespondPageModel?> GetRespondPageAsync(int eventId, string userId)
        {
            var ev = await _db.LeagueClashEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null) return null;
            var profileId = await _db.LeagueProfiles.Where(p => p.UserId == userId && !p.IsGuest).Select(p => (int?)p.Id).FirstOrDefaultAsync();
            LeagueClashAnswer? answer = null;
            if (profileId != null)
                answer = await _db.LeagueClashResponses.Where(r => r.EventId == eventId && r.ProfileId == profileId)
                    .Select(r => (LeagueClashAnswer?)r.Status).FirstOrDefaultAsync();
            return new LeagueClashRespondPageModel
            {
                EventId = ev.Id, Title = ev.Title, StartsAtUtc = ev.StartsAtUtc, Mode = ev.Mode, Status = ev.Status,
                HasProfile = profileId != null, Answer = answer,
            };
        }

        // A user answers only for their OWN profile (found from the signed-in user, never from the form). Allowed only
        // while the clash is Open; the answer can be changed freely until game 1 starts. Upsert on (event, profile).
        public async Task<(bool Ok, string Message)> RespondAsync(int eventId, string userId, string? answer)
        {
            LeagueClashAnswer value;
            if (string.Equals(answer, "free", StringComparison.OrdinalIgnoreCase)) value = LeagueClashAnswer.Free;
            else if (string.Equals(answer, "busy", StringComparison.OrdinalIgnoreCase)) value = LeagueClashAnswer.Busy;
            else return (false, "Pick I can join or Busy.");

            var ev = await _db.LeagueClashEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null) return (false, "That clash no longer exists.");
            if (ev.Status != LeagueClashStatus.Open)
                return (false, ev.Status == LeagueClashStatus.Cancelled ? "This clash was cancelled." : "The clash already started, so answers are locked.");

            var profileId = await _db.LeagueProfiles.Where(p => p.UserId == userId && !p.IsGuest).Select(p => (int?)p.Id).FirstOrDefaultAsync();
            if (profileId == null) return (false, "Create your league profile first, then answer.");

            var now = DateTime.UtcNow;
            var row = await _db.LeagueClashResponses.FirstOrDefaultAsync(r => r.EventId == eventId && r.ProfileId == profileId);
            if (row == null)
                _db.LeagueClashResponses.Add(new LeagueClashResponse { EventId = eventId, ProfileId = profileId.Value, Status = value, RespondedUtc = now });
            else { row.Status = value; row.RespondedUtc = now; }

            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Two clicks at once: the unique (event, profile) index refused the second insert. The first answer stands.
                return (false, "Your answer was already being saved. Reload the page.");
            }
            return (true, value == LeagueClashAnswer.Free ? "Saved: you can join." : "Saved: you are busy.");
        }

        // Step 8i: a moderator (LeagueClash > SetGuestBusy) saves a GUEST's answer for a clash. Only a guest profile is
        // accepted (a member answers for themselves, 6c). Allowed while the clash is Open or InProgress. Upsert on (event, profile).
        public async Task<(bool Ok, string Message)> SetGuestBusyAsync(int eventId, int profileId, bool busy)
        {
            var ev = await _db.LeagueClashEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null) return (false, "That clash no longer exists.");
            var blocked = LeagueGuestBusyRules.CheckStatus(ev.Status);
            if (blocked != null) return (false, blocked);

            var name = await _db.LeagueProfiles.AsNoTracking().Where(p => p.Id == profileId && p.IsGuest)
                .Select(p => p.InGameName).FirstOrDefaultAsync();
            if (name == null) return (false, "That guest no longer exists. Reload the page.");

            var value = LeagueGuestBusyRules.AnswerFor(busy);
            var now = DateTime.UtcNow;
            var row = await _db.LeagueClashResponses.FirstOrDefaultAsync(r => r.EventId == eventId && r.ProfileId == profileId);
            if (row == null)
                _db.LeagueClashResponses.Add(new LeagueClashResponse { EventId = eventId, ProfileId = profileId, Status = value, RespondedUtc = now });
            else { row.Status = value; row.RespondedUtc = now; }

            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Two clicks at once: the unique (event, profile) index refused the second insert. The first answer stands.
                return (false, "That answer was already being saved. Reload the page.");
            }
            return (true, LeagueGuestBusyRules.SavedMessage(name, busy));
        }
    }
}
