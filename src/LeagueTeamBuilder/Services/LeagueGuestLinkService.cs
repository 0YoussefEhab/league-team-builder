using System.Security.Claims;
using System.Text.Json;
using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    public enum LeagueLinkKind { BecomeProfile, Merge }

    // Pure rules, unit-tested without a database.
    public static class LeagueGuestLinkRules
    {
        public const string AuditAction = "Linked league guest to user";
        public const string UndoAuditAction = "Undid league guest link";
        public const string UnlinkAuditAction = "Unlinked league profile from user";
        public const int MaxSearchRows = 100;
        public const int DefaultSearchRows = 50;

        // No league profile yet: the guest row itself becomes the user's profile. Otherwise the guest is merged away.
        public static LeagueLinkKind Plan(bool userHasLeagueProfile) =>
            userHasLeagueProfile ? LeagueLinkKind.Merge : LeagueLinkKind.BecomeProfile;

        // The name used when the officer ticks "use the user's name": display name, else user name, cut to the profile limit.
        public static string? MemberName(string? displayName, string? userName)
        {
            var name = !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() : (userName ?? string.Empty).Trim();
            if (name.Length == 0) return null;
            return name.Length > LeagueGuestService.MaxNameLength ? name[..LeagueGuestService.MaxNameLength].TrimEnd() : name;
        }

        // AvatarUrl is stored in a 500-character column.
        public static bool CanCopyPicture(string? url) => !string.IsNullOrEmpty(url) && url.Length <= 500;

        // Admin fixes Step 4: the guest as it is right before the link, kept in the link log for Undo.
        public static LeagueGuestSnapshot Snapshot(LeagueProfile g) => new()
        {
            InGameName = g.InGameName,
            AvatarUrl = g.AvatarUrl,
            AvatarSource = g.AvatarSource,
            CurTier = g.CurTier,
            CurDiv = g.CurDiv,
            PeakTier = g.PeakTier,
            PeakDiv = g.PeakDiv,
            MainLane = g.MainLane,
            CustomTierId = g.CustomTierId,
            DoBetterWithId = g.DoBetterWithId,
            RiotId = g.RiotId,
            CreatedUtc = g.CreatedUtc,
            Lanes = g.Lanes.OrderBy(l => l.Priority)
                .Select(l => new LeagueGuestLaneSnapshot { Lane = l.Lane, Priority = l.Priority, Level = l.Level }).ToList(),
        };

        public static string ActorLabel(ClaimsPrincipal? actor) =>
            string.IsNullOrWhiteSpace(actor?.Identity?.Name) ? "(unknown)" : actor!.Identity!.Name!;

        public static string UndoMessage(string guestName, string userLabel, LeagueLinkKind kind, int entriesMoved) =>
            kind == LeagueLinkKind.Merge
                ? $"Undid the link. {guestName} is a guest again with {entriesMoved} match entr{(entriesMoved == 1 ? "y" : "ies")} moved back from {userLabel}."
                : $"Undid the link. {guestName} is a guest again and no longer belongs to {userLabel}.";

        // Admin fixes Step 4d: manual Unlink, for older links that have no log entry. Null = allowed.
        public static string? UnlinkBlockReason(bool isMember, bool hasActiveLog, int guestEntries)
        {
            if (!isMember)
                return "That profile is not a linked member any more.";
            if (hasActiveLog)
                return "This link is in the link log, so it can be undone exactly (Undo link). Manual Unlink is only for older links with no log.";
            if (guestEntries == 0)
                return "This profile has no match entries from the time it was a guest, so there is no sign it ever was one. Nothing was changed.";
            return null;
        }

        public static string UnlinkMessage(string name, string userLabel) =>
            $"Unlinked. {name} is a guest again and no longer belongs to {userLabel}.";

        public static int ClampTake(int take) => take < 1 ? DefaultSearchRows : Math.Min(take, MaxSearchRows);

        public static string AuditTarget(string guestName, string userLabel, bool merged) =>
            $"{guestName} -> {userLabel} ({(merged ? "merged" : "not merged")})";

        public static string ResultMessage(string guestName, string userLabel, bool merged, int matchEntriesMoved, int notified)
        {
            if (merged)
                return $"Merged {guestName} into {userLabel}'s league profile. {matchEntriesMoved} match entr{(matchEntriesMoved == 1 ? "y" : "ies")} moved to it.";
            var text = $"Linked {guestName} to {userLabel}. They are now a member, not a guest.";
            if (notified > 0)
                text += $" {notified} open clash notification{(notified == 1 ? "" : "s")} sent.";
            return text;
        }
    }

    // Step 7 (docs/LEAGUE_BUILD_SPEC.md section 6): turn a guest into a real member, or merge it into the member's
    // existing league profile. Server only; the screen is Step 7b.
    public class LeagueGuestLinkService
    {
        private readonly ApplicationDbContext _db;
        private readonly NotificationService _notifications;
        private readonly AdminActivityLogger _audit;
        private readonly ILogger<LeagueGuestLinkService> _logger;

        public LeagueGuestLinkService(ApplicationDbContext db, NotificationService notifications,
            AdminActivityLogger audit, ILogger<LeagueGuestLinkService> logger)
        {
            _db = db;
            _notifications = notifications;
            _audit = audit;
            _logger = logger;
        }

        // Users whose group has League > Index ticked (owner decision), with or without a league profile.
        public async Task<List<LeagueLinkableUser>> GetLinkableUsersAsync(string? search, int take = LeagueGuestLinkRules.DefaultSearchRows)
        {
            take = LeagueGuestLinkRules.ClampTake(take);
            var groupIds = GroupsWith("League", "Index");

            var users = _db.Users.AsNoTracking()
                .Where(u => !u.IsDeactivated && u.PermissionGroupId != null && groupIds.Contains(u.PermissionGroupId.Value));

            var term = (search ?? string.Empty).Trim().ToLower();
            if (term.Length > 0)
                users = users.Where(u => u.DisplayName.ToLower().Contains(term)
                    || (u.UserName != null && u.UserName.ToLower().Contains(term))
                    || (u.Email != null && u.Email.ToLower().Contains(term)));

            var rows = await (from u in users
                              join lp in _db.LeagueProfiles.AsNoTracking() on u.Id equals lp.UserId into profiles
                              from lp in profiles.DefaultIfEmpty()
                              orderby u.DisplayName, u.UserName
                              select new LeagueLinkableUser
                              {
                                  UserId = u.Id,
                                  DisplayName = u.DisplayName != "" ? u.DisplayName : (u.UserName ?? ""),
                                  Email = u.Email,
                                  ProfileId = lp != null ? lp.Id : (int?)null,
                                  ProfileName = lp != null ? lp.InGameName : null,
                              }).Take(take).ToListAsync();
            return rows;
        }

        // The confirm step (Step 7b). Null when the guest is gone or the user can no longer be linked
        // (deactivated, or their group lost League > Index). Same eligibility as the list and as LinkAsync.
        public async Task<LeagueLinkPreview?> GetPreviewAsync(int guestId, string userId)
        {
            var guest = await _db.LeagueProfiles.AsNoTracking()
                .Where(p => p.Id == guestId && p.IsGuest)
                .Select(p => new { p.Id, p.InGameName }).FirstOrDefaultAsync();
            if (guest is null) return null;

            var groupIds = GroupsWith("League", "Index");
            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeactivated
                    && u.PermissionGroupId != null && groupIds.Contains(u.PermissionGroupId.Value));
            if (user is null) return null;

            var profile = await _db.LeagueProfiles.AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => new { p.Id, p.InGameName }).FirstOrDefaultAsync();
            var merge = LeagueGuestLinkRules.Plan(profile is not null) == LeagueLinkKind.Merge;

            return new LeagueLinkPreview
            {
                GuestId = guest.Id,
                GuestName = guest.InGameName,
                User = new LeagueLinkableUser
                {
                    UserId = user.Id,
                    DisplayName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : (user.UserName ?? ""),
                    Email = user.Email,
                    ProfileId = profile?.Id,
                    ProfileName = profile?.InGameName,
                },
                MatchEntries = merge ? await _db.LeagueMatchPlayers.CountAsync(m => m.ProfileId == guest.Id) : 0,
                MemberName = merge ? null : LeagueGuestLinkRules.MemberName(user.DisplayName, user.UserName),
                CanCopyPicture = !merge && LeagueGuestLinkRules.CanCopyPicture(user.AvatarUrl),
            };
        }

        public async Task<LeagueLinkResult> LinkAsync(LeagueLinkRequest req, ClaimsPrincipal actor)
        {
            var guest = await _db.LeagueProfiles.AsNoTracking().Include(p => p.Lanes).FirstOrDefaultAsync(p => p.Id == req.GuestId && p.IsGuest);
            if (guest is null) return Fail("That guest no longer exists. It may already have been linked.");

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.UserId);
            if (user is null || user.IsDeactivated) return Fail("That user was not found.");
            if (!await GroupAllowsAsync(user.PermissionGroupId, "League", "Index"))
                return Fail("That user's group does not have League access, so they cannot be linked.");

            var existing = await _db.LeagueProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
            var plan = LeagueGuestLinkRules.Plan(existing is not null);
            var userLabel = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : (user.UserName ?? user.Email ?? "the user");
            var auditLabel = user.Email ?? user.UserName ?? userLabel;
            var guestName = guest.InGameName;
            var data = new LeagueGuestLinkData { Guest = LeagueGuestLinkRules.Snapshot(guest) };
            int logId = 0;

            int profileId;
            var moved = 0;
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                if (plan == LeagueLinkKind.BecomeProfile)
                {
                    // Conditional: if someone linked this guest a moment ago, nothing is changed.
                    var rows = await _db.LeagueProfiles.Where(p => p.Id == guest.Id && p.IsGuest)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.UserId, user.Id).SetProperty(p => p.IsGuest, false));
                    if (rows == 0) return Fail("That guest was linked a moment ago. Nothing was changed.");
                    profileId = guest.Id;

                    var newName = req.UseUserName ? LeagueGuestLinkRules.MemberName(user.DisplayName, user.UserName) : null;
                    if (newName is not null)
                    {
                        await _db.LeagueProfiles.Where(p => p.Id == profileId)
                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.InGameName, newName));
                        data.NameCopied = true;
                    }

                    if (req.UseUserPicture && LeagueGuestLinkRules.CanCopyPicture(user.AvatarUrl))
                    {
                        await _db.LeagueProfiles.Where(p => p.Id == profileId)
                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AvatarUrl, user.AvatarUrl)
                                                      .SetProperty(p => p.AvatarSource, LeagueAvatarSource.Copied));
                        data.PictureCopied = true;
                    }
                }
                else
                {
                    profileId = existing!.Id;

                    // Record exactly what is about to move (Undo moves back only these). Must run before the updates below.
                    data.MatchPlayerIds = await _db.LeagueMatchPlayers.Where(p => p.ProfileId == guest.Id).Select(p => p.Id).ToListAsync();
                    data.MvpMatchIds = await _db.LeagueMatches.Where(m => m.MvpProfileId == guest.Id).Select(m => m.Id).ToListAsync();
                    data.TeamMvpMatchIds = await _db.LeagueMatches.Where(m => m.TeamMvpProfileId == guest.Id).Select(m => m.Id).ToListAsync();
                    data.StatsMvpMatchIds = await _db.LeagueMatches.Where(m => m.StatsMvpProfileId == guest.Id).Select(m => m.Id).ToListAsync();
                    data.StatsTeamMvpMatchIds = await _db.LeagueMatches.Where(m => m.StatsTeamMvpProfileId == guest.Id).Select(m => m.Id).ToListAsync();
                    data.DoBetterWithProfileIds = await _db.LeagueProfiles.Where(p => p.DoBetterWithId == guest.Id && p.Id != profileId).Select(p => p.Id).ToListAsync();
                    data.MemberDoBetterWithCleared = existing.DoBetterWithId == guest.Id;

                    // History and stats carry over. The snapshots on each row (name, guest flag, ranks) stay as they were.
                    moved = await _db.LeagueMatchPlayers.Where(p => p.ProfileId == guest.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.ProfileId, (int?)profileId));

                    // Step 8: the guest's MVP picks go with the history (the FK is SetNull, so this must run before the delete below).
                    await _db.LeagueMatches.Where(m => m.MvpProfileId == guest.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.MvpProfileId, (int?)profileId));
                    await _db.LeagueMatches.Where(m => m.TeamMvpProfileId == guest.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.TeamMvpProfileId, (int?)profileId));
                    await _db.LeagueMatches.Where(m => m.StatsMvpProfileId == guest.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.StatsMvpProfileId, (int?)profileId));
                    await _db.LeagueMatches.Where(m => m.StatsTeamMvpProfileId == guest.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.StatsTeamMvpProfileId, (int?)profileId));

                    // "Does better with" links to the guest now point at the member. The member's own link to the
                    // guest would point at itself, so that one is cleared.
                    await _db.LeagueProfiles.Where(p => p.DoBetterWithId == guest.Id && p.Id != profileId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.DoBetterWithId, (int?)profileId));
                    await _db.LeagueProfiles.Where(p => p.DoBetterWithId == guest.Id && p.Id == profileId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.DoBetterWithId, (int?)null));

                    // Guests never answer a clash, so this is only defensive: keep the member's own answer on a clash.
                    var answered = _db.LeagueClashResponses.Where(r => r.ProfileId == profileId).Select(r => r.EventId);
                    await _db.LeagueClashResponses.Where(r => r.ProfileId == guest.Id && answered.Contains(r.EventId))
                        .ExecuteDeleteAsync();
                    await _db.LeagueClashResponses.Where(r => r.ProfileId == guest.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProfileId, profileId));

                    // Last: the conditional delete. 0 rows means the guest was already gone, so everything above is undone.
                    var deleted = await _db.LeagueProfiles.Where(p => p.Id == guest.Id && p.IsGuest).ExecuteDeleteAsync();
                    if (deleted == 0) return Fail("That guest was merged a moment ago. Nothing was changed.");
                }

                _audit.Log(actor, LeagueGuestLinkRules.AuditAction,
                    LeagueGuestLinkRules.AuditTarget(guestName, auditLabel, plan == LeagueLinkKind.Merge));
                var logRow = new LeagueGuestLinkLog
                {
                    LinkedUtc = DateTime.UtcNow,
                    LinkedByLabel = LeagueGuestLinkRules.ActorLabel(actor),
                    UserId = user.Id,
                    UserLabel = auditLabel.Length > 200 ? auditLabel[..200] : auditLabel,
                    Kind = plan,
                    GuestId = guest.Id,
                    ProfileId = profileId,
                    GuestName = guestName,
                    DataJson = JsonSerializer.Serialize(data),
                };
                _db.LeagueGuestLinkLogs.Add(logRow);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                logId = logRow.Id;
            }
            catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
            {
                // Most likely the unique index on UserId: two links for the same user raced.
                // (ExecuteUpdate reports it as the provider's DbException, SaveChanges as DbUpdateException.)
                _logger.LogWarning(ex, "Linking guest {GuestId} to user {UserId} failed.", req.GuestId, req.UserId);
                return Fail("That user got a league profile a moment ago, or the link could not be saved. Nothing was changed. Try again.");
            }

            var merged = plan == LeagueLinkKind.Merge;
            var notified = merged ? 0 : await NotifyOpenClashesAsync(user, profileId);
            return new LeagueLinkResult
            {
                Ok = true,
                Merged = merged,
                ProfileId = profileId,
                LogId = logId,
                MatchEntriesMoved = moved,
                Notified = notified,
                Message = LeagueGuestLinkRules.ResultMessage(guestName, userLabel, merged, moved, notified),
            };
        }

        // One personal notification per Open clash this profile has not answered, same text and link as the scheduling
        // notification. Only when the user's group may open the response page (Step 6 decision). The link is already
        // saved, so a problem here is logged and never undoes it.
        private async Task<int> NotifyOpenClashesAsync(ApplicationUser user, int profileId)
        {
            var sent = 0;
            try
            {
                if (!await GroupAllowsAsync(user.PermissionGroupId, LeagueClashScheduleService.RespondController, LeagueClashScheduleService.RespondAction))
                    return 0;

                var answered = _db.LeagueClashResponses.Where(r => r.ProfileId == profileId).Select(r => r.EventId);
                var open = await _db.LeagueClashEvents.AsNoTracking()
                    .Where(e => e.Status == LeagueClashStatus.Open && !answered.Contains(e.Id))
                    .OrderBy(e => e.StartsAtUtc).ToListAsync();
                foreach (var ev in open)
                {
                    await _notifications.CreateForUserAsync(user.Id,
                        LeagueClashScheduleService.ScheduledMessage(ev.Title, ev.StartsAtUtc),
                        LeagueClashScheduleService.RespondLink(ev.Id));
                    sent++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send every clash notification to user {UserId} after a guest link ({Sent} sent).", user.Id, sent);
            }
            return sent;
        }

        // Admin fixes Step 4: reverse one link using its log row. One transaction; nothing is changed on a failure.
        // Become: the same profile row goes back to being a guest (picture and champions are dropped, Q6).
        // Merge: the guest is recreated from the snapshot (new id) and ONLY the recorded entries move back.
        public async Task<LeagueUndoResult> UndoLinkAsync(int logId, ClaimsPrincipal actor)
        {
            var log = await _db.LeagueGuestLinkLogs.FirstOrDefaultAsync(l => l.Id == logId);
            if (log is null) return UndoFail("That link log entry was not found.");
            if (log.UndoneUtc is not null) return UndoFail("That link was already undone.");

            LeagueGuestLinkData? data;
            try { data = JsonSerializer.Deserialize<LeagueGuestLinkData>(log.DataJson); }
            catch (JsonException) { data = null; }
            if (data is null) return UndoFail("The saved data for that link could not be read, so it cannot be undone.");

            var snap = data.Guest;
            var moved = 0;
            int guestId;
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                if (log.Kind == LeagueLinkKind.BecomeProfile)
                {
                    // Conditional: only if the row is still this user's member profile.
                    var rows = await _db.LeagueProfiles.Where(p => p.Id == log.ProfileId && !p.IsGuest && p.UserId == log.UserId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.UserId, (string?)null).SetProperty(p => p.IsGuest, true));
                    if (rows == 0)
                        return UndoFail("That profile is no longer linked to that user (it was already unlinked, moved or deleted). Nothing was changed.");
                    guestId = log.ProfileId;

                    if (data.NameCopied)
                        await _db.LeagueProfiles.Where(p => p.Id == guestId)
                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.InGameName, snap.InGameName));

                    // Guests have no champions and no member picture: drop what the member added, put back the guest's own.
                    await _db.LeagueProfileChamps.Where(c => c.ProfileId == guestId).ExecuteDeleteAsync();
                    await _db.LeagueProfiles.Where(p => p.Id == guestId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.AvatarUrl, snap.AvatarUrl)
                                                  .SetProperty(p => p.AvatarSource, snap.AvatarSource));
                }
                else
                {
                    var member = await _db.LeagueProfiles.FirstOrDefaultAsync(p => p.Id == log.ProfileId && !p.IsGuest);
                    if (member is null)
                        return UndoFail("The member's league profile no longer exists, so the merge cannot be undone. Nothing was changed.");

                    // Links to rows that may be gone by now are dropped instead of failing.
                    var tierOk = snap.CustomTierId is int tid && await _db.LeagueCustomTiers.AnyAsync(t => t.Id == tid);
                    var betterOk = snap.DoBetterWithId is int bid && bid != member.Id
                        && await _db.LeagueProfiles.AnyAsync(p => p.Id == bid);

                    var guest = new LeagueProfile
                    {
                        UserId = null,
                        IsGuest = true,
                        InGameName = snap.InGameName,
                        AvatarUrl = snap.AvatarUrl,
                        AvatarSource = snap.AvatarSource,
                        CurTier = snap.CurTier,
                        CurDiv = snap.CurDiv,
                        PeakTier = snap.PeakTier,
                        PeakDiv = snap.PeakDiv,
                        MainLane = snap.MainLane,
                        CustomTierId = tierOk ? snap.CustomTierId : null,
                        DoBetterWithId = betterOk ? snap.DoBetterWithId : null,
                        RiotId = snap.RiotId,
                        CreatedUtc = snap.CreatedUtc,
                        Lanes = snap.Lanes.Select(l => new LeagueProfileLane { Lane = l.Lane, Priority = l.Priority, Level = l.Level }).ToList(),
                    };
                    _db.LeagueProfiles.Add(guest);
                    await _db.SaveChangesAsync();
                    guestId = guest.Id;

                    // Only the recorded rows, and only if they still belong to the member (anything edited since stays).
                    moved = await _db.LeagueMatchPlayers.Where(p => data.MatchPlayerIds.Contains(p.Id) && p.ProfileId == member.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.ProfileId, (int?)guestId));
                    await _db.LeagueMatches.Where(m => data.MvpMatchIds.Contains(m.Id) && m.MvpProfileId == member.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.MvpProfileId, (int?)guestId));
                    await _db.LeagueMatches.Where(m => data.TeamMvpMatchIds.Contains(m.Id) && m.TeamMvpProfileId == member.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.TeamMvpProfileId, (int?)guestId));
                    await _db.LeagueMatches.Where(m => data.StatsMvpMatchIds.Contains(m.Id) && m.StatsMvpProfileId == member.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.StatsMvpProfileId, (int?)guestId));
                    await _db.LeagueMatches.Where(m => data.StatsTeamMvpMatchIds.Contains(m.Id) && m.StatsTeamMvpProfileId == member.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.StatsTeamMvpProfileId, (int?)guestId));
                    await _db.LeagueProfiles.Where(p => data.DoBetterWithProfileIds.Contains(p.Id) && p.DoBetterWithId == member.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.DoBetterWithId, (int?)guestId));
                    if (data.MemberDoBetterWithCleared && member.DoBetterWithId is null)
                        await _db.LeagueProfiles.Where(p => p.Id == member.Id && p.DoBetterWithId == null)
                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DoBetterWithId, (int?)guestId));
                }

                log.UndoneUtc = DateTime.UtcNow;
                log.UndoneByLabel = LeagueGuestLinkRules.ActorLabel(actor);
                _audit.Log(actor, LeagueGuestLinkRules.UndoAuditAction,
                    $"{log.GuestName} <- {log.UserLabel} ({(log.Kind == LeagueLinkKind.Merge ? "merge undone" : "link undone")})");
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
            {
                _logger.LogWarning(ex, "Undoing guest link log {LogId} failed.", logId);
                return UndoFail("The link could not be undone. Nothing was changed. Try again.");
            }

            return new LeagueUndoResult
            {
                Ok = true,
                GuestId = guestId,
                Kind = log.Kind,
                MatchEntriesMoved = moved,
                Message = LeagueGuestLinkRules.UndoMessage(log.GuestName, log.UserLabel, log.Kind, moved),
            };
        }

        // Admin fixes Step 4e: the link log for League Settings > Link log, newest first.
        public async Task<LeagueLinkedGuestsPageModel> GetLinkLogAsync(int take = 200)
        {
            take = LeagueGuestLinkRules.ClampTake(take);
            var logs = await _db.LeagueGuestLinkLogs.AsNoTracking()
                .OrderByDescending(l => l.LinkedUtc).ThenByDescending(l => l.Id).Take(take).ToListAsync();
            return new LeagueLinkedGuestsPageModel
            {
                ActiveCount = await _db.LeagueGuestLinkLogs.CountAsync(l => l.UndoneUtc == null),
                Rows = logs.Select(l => new LeagueLinkLogRow
                {
                    LogId = l.Id, LinkedUtc = l.LinkedUtc, LinkedBy = l.LinkedByLabel,
                    GuestName = l.GuestName, UserLabel = l.UserLabel, ProfileId = l.ProfileId, Kind = l.Kind,
                    UndoneUtc = l.UndoneUtc, UndoneBy = l.UndoneByLabel,
                }).ToList(),
            };
        }

        // Admin fixes Step 4d: the confirm screen of the manual Unlink. Null when the profile is not a linked member.
        public async Task<LeagueUnlinkPreview?> GetUnlinkPreviewAsync(int profileId)
        {
            var p = await _db.LeagueProfiles.AsNoTracking().Include(x => x.User).FirstOrDefaultAsync(x => x.Id == profileId);
            if (p is null || p.IsGuest || p.UserId is null) return null;

            var hasLog = await _db.LeagueGuestLinkLogs.AnyAsync(l => l.ProfileId == p.Id && l.UserId == p.UserId && l.UndoneUtc == null);
            var guestEntries = await _db.LeagueMatchPlayers.CountAsync(m => m.ProfileId == p.Id && m.WasGuest);
            return new LeagueUnlinkPreview
            {
                ProfileId = p.Id,
                Name = p.InGameName,
                UserLabel = UserLabelOf(p.User),
                Email = p.User?.Email,
                MatchEntries = await _db.LeagueMatchPlayers.CountAsync(m => m.ProfileId == p.Id),
                GuestEntries = guestEntries,
                Champions = await _db.LeagueProfileChamps.CountAsync(c => c.ProfileId == p.Id),
                HasPicture = !string.IsNullOrEmpty(p.AvatarUrl),
                BlockReason = LeagueGuestLinkRules.UnlinkBlockReason(true, hasLog, guestEntries),
            };
        }

        // Admin fixes Step 4d: manual Unlink for an older link with no log entry. The profile row stays and goes back to
        // being a guest (UserId null, IsGuest true), like a Become undo. It cannot restore a guest that was merged
        // (that row is gone), and it cannot know the guest's old name or picture, so the name stays as it is.
        // Champions and the member picture are dropped, as guests have none (owner Q6). One transaction.
        public async Task<LeagueUndoResult> UnlinkAsync(int profileId, ClaimsPrincipal actor)
        {
            var p = await _db.LeagueProfiles.AsNoTracking().Include(x => x.User).FirstOrDefaultAsync(x => x.Id == profileId);
            if (p is null || p.IsGuest || p.UserId is null)
                return UndoFail(LeagueGuestLinkRules.UnlinkBlockReason(false, false, 0)!);

            var hasLog = await _db.LeagueGuestLinkLogs.AnyAsync(l => l.ProfileId == p.Id && l.UserId == p.UserId && l.UndoneUtc == null);
            var guestEntries = await _db.LeagueMatchPlayers.CountAsync(m => m.ProfileId == p.Id && m.WasGuest);
            var reason = LeagueGuestLinkRules.UnlinkBlockReason(true, hasLog, guestEntries);
            if (reason is not null) return UndoFail(reason);

            var userId = p.UserId;
            var name = p.InGameName;
            var userLabel = UserLabelOf(p.User);
            var auditLabel = p.User?.Email ?? p.User?.UserName ?? userLabel;
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync();

                // Conditional: only if the row is still this user's member profile.
                var rows = await _db.LeagueProfiles.Where(x => x.Id == p.Id && !x.IsGuest && x.UserId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, (string?)null).SetProperty(x => x.IsGuest, true));
                if (rows == 0)
                    return UndoFail("That profile was unlinked a moment ago, or it changed. Nothing was changed.");

                await _db.LeagueProfileChamps.Where(c => c.ProfileId == p.Id).ExecuteDeleteAsync();
                await _db.LeagueProfiles.Where(x => x.Id == p.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.AvatarUrl, (string?)null)
                                              .SetProperty(x => x.AvatarSource, LeagueAvatarSource.None));

                _audit.Log(actor, LeagueGuestLinkRules.UnlinkAuditAction, $"{name} <- {auditLabel} (manual unlink, no log)");
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
            {
                _logger.LogWarning(ex, "Manual unlink of league profile {ProfileId} failed.", profileId);
                return UndoFail("The profile could not be unlinked. Nothing was changed. Try again.");
            }

            return new LeagueUndoResult
            {
                Ok = true,
                GuestId = p.Id,
                Kind = LeagueLinkKind.BecomeProfile,
                Message = LeagueGuestLinkRules.UnlinkMessage(name, userLabel),
            };
        }

        private static string UserLabelOf(ApplicationUser? u) =>
            u is null ? "(unknown user)"
            : !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName
            : (u.UserName ?? u.Email ?? "(unknown user)");

        private static LeagueUndoResult UndoFail(string message) => new() { Ok = false, Message = message };

        private IQueryable<int> GroupsWith(string controller, string action) =>
            _db.GroupPermissions.Where(p => p.ControllerName == controller && p.ActionName == action && p.Allowed)
                .Select(p => p.PermissionGroupId);

        private async Task<bool> GroupAllowsAsync(int? groupId, string controller, string action) =>
            groupId is not null && await GroupsWith(controller, action).AnyAsync(id => id == groupId.Value);

        private static LeagueLinkResult Fail(string message) => new() { Ok = false, Message = message };
    }
}
