using System.Security.Claims;
using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    public static class LeagueRankRules
    {
        // Master and above have no division.
        public static bool HasDivision(LeagueRank rank) => rank < LeagueRank.Master;

        // Comparable strength of a rank: tier first, then division (1 is the best division).
        public static int Value(LeagueRank rank, int? div) =>
            (int)rank * 10 + (HasDivision(rank) ? 4 - (div ?? 4) : 0);
    }

    // The member's own league profile (docs/LEAGUE_BUILD_SPEC.md sections 3, 9, 11). Completely
    // separate from the main profile: this service READS ApplicationUser.DisplayName/AvatarUrl
    // when asked to copy them and never writes to ApplicationUser.
    public class LeagueProfileService
    {
        public const int MaxNameLength = 40;
        public const int MaxRiotIdLength = 64;

        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly AdminActivityLogger _audit;

        public LeagueProfileService(ApplicationDbContext db, IWebHostEnvironment env, AdminActivityLogger audit)
        {
            _db = db;
            _env = env;
            _audit = audit;
        }

        public Task<LeagueProfile?> GetByUserIdAsync(string userId) =>
            _db.LeagueProfiles
                .AsNoTracking()
                .Include(p => p.Lanes)
                .Include(p => p.Champs).ThenInclude(c => c.Champion)
                .FirstOrDefaultAsync(p => p.UserId == userId);

        public static LeagueProfileForm ToForm(LeagueProfile p)
        {
            // Step 8f: the five slider values (a lane with no row is level 1) and the lanes that are played (above level 1).
            var levels = LeagueLaneLevels.LevelsOf(p.Lanes.Select(l => new LaneLevel(l.Lane, l.Level, l.Priority)));
            var lanes = LeagueLaneLevels.AllLanes.Where(l => levels[l] > LeagueLaneLevels.Min).ToList();
            var form = new LeagueProfileForm
            {
                InGameName = p.InGameName,
                CurTier = p.CurTier,
                CurDiv = p.CurDiv,
                PeakTier = p.PeakTier,
                PeakDiv = p.PeakDiv,
                MainLane = p.MainLane,
                Lanes = lanes,
                DoBetterWithId = p.DoBetterWithId,
                RiotId = p.RiotId,
            };
            foreach (var (lane, level) in levels) form.SetLevel(lane, level);
            foreach (var lane in lanes)
                form.ChampsFor(lane).AddRange(p.Champs.Where(c => c.Lane == lane).Select(c => c.ChampionId));
            return form;
        }

        public async Task<List<(int Id, string Name)>> GetOtherProfilesAsync(int? exceptId)
        {
            var rows = await _db.LeagueProfiles.AsNoTracking()
                .Where(p => exceptId == null || p.Id != exceptId)
                .OrderBy(p => p.InGameName)
                .Select(p => new { p.Id, p.InGameName })
                .ToListAsync();
            return rows.Select(r => (r.Id, r.InGameName)).ToList();
        }

        // Validates and saves the member's own profile. Returns the field errors (empty on success).
        public async Task<Dictionary<string, string>> SaveOwnAsync(ApplicationUser user, LeagueProfileForm f)
        {
            var errors = new Dictionary<string, string>();

            // ---- validation (nothing is written or uploaded until all of it passes) ----
            var name = (f.InGameName ?? string.Empty).Trim();
            if (name.Length == 0)
                errors["InGameName"] = "Enter your in-game name.";
            else if (name.Length > MaxNameLength)
                errors["InGameName"] = $"In-game name can be at most {MaxNameLength} characters.";

            if (!Enum.IsDefined(f.CurTier) || !Enum.IsDefined(f.PeakTier))
                errors["Rank"] = "Pick a valid rank.";
            else
            {
                var curDiv = LeagueRankRules.HasDivision(f.CurTier) ? f.CurDiv : null;
                var peakDiv = LeagueRankRules.HasDivision(f.PeakTier) ? f.PeakDiv : null;
                if (LeagueRankRules.HasDivision(f.CurTier) && curDiv is not (>= 1 and <= 4))
                    errors["CurDiv"] = "Pick your current division (1-4).";
                if (LeagueRankRules.HasDivision(f.PeakTier) && peakDiv is not (>= 1 and <= 4))
                    errors["PeakDiv"] = "Pick your peak division (1-4).";
                if (!errors.ContainsKey("CurDiv") && !errors.ContainsKey("PeakDiv") &&
                    LeagueRankRules.Value(f.PeakTier, peakDiv) < LeagueRankRules.Value(f.CurTier, curDiv))
                    errors["Peak"] = "Peak rank can't be lower than your current rank.";
            }

            var existing = await _db.LeagueProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            // Step 8f: lane levels 1-5. The old order (Priority) only breaks ties between equal levels.
            var oldPriority = new Dictionary<LeagueLane, int>();
            if (existing is not null)
                oldPriority = (await _db.LeagueProfileLanes.Where(l => l.ProfileId == existing.Id)
                    .Select(l => new { l.Lane, l.Priority }).ToListAsync())
                    .ToDictionary(l => l.Lane, l => l.Priority);
            var laneOk = LeagueLaneLevels.TryPlanSave(f.Levels(), oldPriority, out var lanePlan, out var laneError);
            if (!laneOk)
                errors["Lanes"] = laneError;
            var ticked = laneOk ? lanePlan.Rows.Select(r => r.Lane).ToList() : new List<LeagueLane>();

            var riotId = string.IsNullOrWhiteSpace(f.RiotId) ? null : f.RiotId.Trim();
            if (riotId is not null && riotId.Length > MaxRiotIdLength)
                errors["RiotId"] = $"Riot ID can be at most {MaxRiotIdLength} characters.";

            if (f.DoBetterWithId is int withId)
            {
                if (existing is not null && withId == existing.Id)
                    errors["DoBetterWith"] = "You can't pick yourself.";
                else if (!await _db.LeagueProfiles.AnyAsync(p => p.Id == withId))
                    errors["DoBetterWith"] = "That player doesn't exist.";
            }

            // Champions: every id must exist and be active, except ones already on this profile
            // in that lane (retired champions stay until the member removes them).
            var activeIds = (await _db.LeagueChampions.Where(c => c.IsActive).Select(c => c.Id).ToListAsync()).ToHashSet();
            var keptIds = new Dictionary<LeagueLane, HashSet<int>>();
            if (existing is not null)
            {
                var current = await _db.LeagueProfileChamps.Where(c => c.ProfileId == existing.Id)
                    .Select(c => new { c.Lane, c.ChampionId }).ToListAsync();
                keptIds = current.GroupBy(c => c.Lane).ToDictionary(g => g.Key, g => g.Select(c => c.ChampionId).ToHashSet());
            }
            var champsByLane = new Dictionary<LeagueLane, List<int>>();
            foreach (var lane in ticked)
            {
                var ids = f.ChampsFor(lane).Distinct().ToList();
                keptIds.TryGetValue(lane, out var kept);
                if (ids.Any(id => !activeIds.Contains(id) && !(kept?.Contains(id) ?? false)))
                    errors["Champs"] = "Some selected champions are not in the catalog. Remove them and pick again.";
                champsByLane[lane] = ids;
            }

            if (errors.Count > 0)
                return errors;

            // ---- picture (copy of the main picture URL, a new upload, or removal) ----
            var newAvatarUrl = existing?.AvatarUrl;
            var newSource = existing?.AvatarSource ?? LeagueAvatarSource.None;
            string? deleteOld = null;
            bool oldWasUploaded = existing?.AvatarSource == LeagueAvatarSource.Uploaded;

            if (f.Avatar is { Length: > 0 })
            {
                var (url, _, error) = await FileUploadService.SaveValidatedAsync(
                    f.Avatar, _env.WebRootPath, "uploads/league-avatars",
                    MediaUploadLimits.AvatarExtensions, MediaUploadLimits.AvatarMaxBytes);
                if (error is not null)
                {
                    errors["Avatar"] = error;
                    return errors;
                }
                if (url is not null)
                {
                    if (oldWasUploaded) deleteOld = existing!.AvatarUrl;
                    newAvatarUrl = url;
                    newSource = LeagueAvatarSource.Uploaded;
                }
            }
            else if (f.UseMainAvatar && !string.IsNullOrEmpty(user.AvatarUrl))
            {
                // Copy only the URL string. The main avatar file is never touched.
                if (oldWasUploaded) deleteOld = existing!.AvatarUrl;
                newAvatarUrl = user.AvatarUrl;
                newSource = LeagueAvatarSource.Copied;
            }
            else if (f.RemoveAvatar)
            {
                if (oldWasUploaded) deleteOld = existing!.AvatarUrl;
                newAvatarUrl = null;
                newSource = LeagueAvatarSource.None;
            }

            // ---- save ----
            await using var tx = await _db.Database.BeginTransactionAsync();

            var profile = existing;
            if (profile is null)
            {
                profile = new LeagueProfile { UserId = user.Id, IsGuest = false, CreatedUtc = DateTime.UtcNow };
                _db.LeagueProfiles.Add(profile);
            }

            profile.InGameName = name;
            profile.CurTier = f.CurTier;
            profile.CurDiv = LeagueRankRules.HasDivision(f.CurTier) ? f.CurDiv : null;
            profile.PeakTier = f.PeakTier;
            profile.PeakDiv = LeagueRankRules.HasDivision(f.PeakTier) ? f.PeakDiv : null;
            profile.MainLane = lanePlan.Main;
            profile.DoBetterWithId = f.DoBetterWithId;
            profile.RiotId = riotId;
            profile.AvatarUrl = newAvatarUrl;
            profile.AvatarSource = newSource;
            await _db.SaveChangesAsync();

            await _db.LeagueProfileLanes.Where(l => l.ProfileId == profile.Id).ExecuteDeleteAsync();
            await _db.LeagueProfileChamps.Where(c => c.ProfileId == profile.Id).ExecuteDeleteAsync();

            foreach (var row in lanePlan.Rows)
                _db.LeagueProfileLanes.Add(new LeagueProfileLane { ProfileId = profile.Id, Lane = row.Lane, Priority = row.Priority, Level = row.Level });
            foreach (var (lane, ids) in champsByLane)
                foreach (var id in ids)
                    _db.LeagueProfileChamps.Add(new LeagueProfileChamp { ProfileId = profile.Id, Lane = lane, ChampionId = id });
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            // Only an upload the league itself made is ever deleted; a copied main-profile
            // picture is never removed from storage.
            if (deleteOld is not null)
                FileUploadService.DeleteIfExists(deleteOld, _env.WebRootPath);

            return errors;
        }

        // Step 8e: an admin (LeagueSettings > EditPlayerRank) changes the current and peak rank of any league
        // profile, member or guest. Same rules as the own profile form (LeaguePlayerRules.ValidateRank). Writes
        // only to LeagueProfiles, never to ApplicationUser. One line goes to the Admin activity log, in the same save.
        public async Task<(bool Ok, string Message)> SetRankAsync(LeagueRankEditRequest req, ClaimsPrincipal actor)
        {
            var check = LeaguePlayerRules.ValidateRank(req.CurTier, req.CurDiv, req.PeakTier, req.PeakDiv);
            if (!check.Ok) return (false, check.Error!);

            var profile = await _db.LeagueProfiles.FirstOrDefaultAsync(p => p.Id == req.ProfileId);
            if (profile is null) return (false, "That player no longer exists.");

            var unchanged = profile.CurTier == check.CurTier && profile.CurDiv == check.CurDiv
                && profile.PeakTier == check.PeakTier && profile.PeakDiv == check.PeakDiv;
            var nowText = LeaguePlayerRules.RankPairText(check.CurTier, check.CurDiv, check.PeakTier, check.PeakDiv);
            if (unchanged) return (true, $"{profile.InGameName} already has {nowText}. Nothing was changed.");

            var target = LeaguePlayerRules.RankChangeText(profile.InGameName,
                profile.CurTier, profile.CurDiv, profile.PeakTier, profile.PeakDiv,
                check.CurTier, check.CurDiv, check.PeakTier, check.PeakDiv);

            profile.CurTier = check.CurTier;
            profile.CurDiv = check.CurDiv;
            profile.PeakTier = check.PeakTier;
            profile.PeakDiv = check.PeakDiv;
            _audit.Log(actor, LeaguePlayerRules.AuditAction, target);
            await _db.SaveChangesAsync();

            return (true, $"{profile.InGameName}'s rank is now {nowText}.");
        }

        // Guest edit screen helper: RiotId is not on LeagueGuestForm, so read it here.
        public Task<string?> GetRiotIdAsync(int profileId) =>
            _db.LeagueProfiles.AsNoTracking().Where(p => p.Id == profileId).Select(p => p.RiotId).FirstOrDefaultAsync();

        // Step 3 of PLAN-admin-fixes.md. An admin changes a person's names (Admin > Users > Edit names, or
        // League Settings > Guests > Edit names). Since 2026-10-18 also League > Player > Change names (anyProfile = true:
        // a member's league profile, in-game name and Riot ID only, the account's DisplayName is never touched). Owner decisions: Riot name = RiotId (Name#TAG, optional, blank
        // clears it); old match history is rewritten to the new in-game name (NameSnapshot); every change is
        // audit-logged as old -> new. The account's DisplayName is changed on the tracked user in the SAME
        // save as the League fields (UserManager and this service share one scoped DbContext), so it is all or nothing.
        public const int MaxDisplayNameLength = 200;

        public async Task<(bool Ok, string Message, Dictionary<string, string> Errors)> EditNamesAsync(AdminNamesEdit req, ClaimsPrincipal actor, bool anyProfile = false)
        {
            var errors = new Dictionary<string, string>();

            ApplicationUser? user = null;
            if (!string.IsNullOrEmpty(req.UserId))
            {
                user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId);
                if (user is null)
                {
                    errors["User"] = "That user no longer exists.";
                    return (false, errors["User"], errors);
                }
            }

            LeagueProfile? profile = null;
            if (user is not null)
            {
                if (req.IncludeLeague)
                    profile = await _db.LeagueProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            }
            else
            {
                profile = req.ProfileId is int pid
                    ? await _db.LeagueProfiles.FirstOrDefaultAsync(p => p.Id == pid && (anyProfile || p.IsGuest))
                    : null;
                if (profile is null)
                {
                    errors["Guest"] = anyProfile
                        ? "That player no longer exists."
                        : "That guest no longer exists. It may already have been linked to a user.";
                    return (false, errors["Guest"], errors);
                }
            }

            // ---- validate (trim, one line, limits) ----
            var displayName = (req.DisplayName ?? string.Empty).Trim();
            if (user is not null)
            {
                if (displayName.Any(char.IsControl)) errors["DisplayName"] = "The display name must be on one line.";
                else if (displayName.Length > MaxDisplayNameLength) errors["DisplayName"] = $"The display name can be at most {MaxDisplayNameLength} characters.";
            }

            var inGameName = (req.InGameName ?? string.Empty).Trim();
            string? riotId = string.IsNullOrWhiteSpace(req.RiotId) ? null : req.RiotId.Trim();
            if (profile is not null)
            {
                if (inGameName.Length == 0) errors["InGameName"] = "Enter the in-game name.";
                else if (inGameName.Any(char.IsControl)) errors["InGameName"] = "The in-game name must be on one line.";
                else if (inGameName.Length > MaxNameLength) errors["InGameName"] = $"The in-game name can be at most {MaxNameLength} characters.";

                if (riotId is not null && riotId.Any(char.IsControl)) errors["RiotId"] = "The Riot ID must be on one line.";
                else if (riotId is not null && riotId.Length > MaxRiotIdLength) errors["RiotId"] = $"Riot ID can be at most {MaxRiotIdLength} characters.";
            }
            if (errors.Count > 0) return (false, "Please fix the highlighted fields and save again.", errors);

            // ---- what actually changes ----
            static string Q(string? v) => string.IsNullOrEmpty(v) ? "(empty)" : $"\"{v}\"";
            var changes = new List<string>();
            var displayChanged = user is not null && !string.Equals(user.DisplayName ?? string.Empty, displayName, StringComparison.Ordinal);
            var nameChanged = profile is not null && !string.Equals(profile.InGameName, inGameName, StringComparison.Ordinal);
            var riotChanged = profile is not null && !string.Equals(profile.RiotId ?? string.Empty, riotId ?? string.Empty, StringComparison.Ordinal);

            if (displayChanged) changes.Add($"display name {Q(user!.DisplayName)} -> {Q(displayName)}");
            if (nameChanged) changes.Add($"in-game name {Q(profile!.InGameName)} -> {Q(inGameName)}");
            if (riotChanged) changes.Add($"Riot ID {Q(profile!.RiotId)} -> {Q(riotId)}");

            var who = user is not null ? (user.Email ?? user.UserName ?? user.Id) : $"{(profile!.IsGuest ? "guest" : "player")} {profile.InGameName}";
            if (changes.Count == 0) return (true, "Nothing was changed.", errors);

            await using var tx = await _db.Database.BeginTransactionAsync();
            if (displayChanged) user!.DisplayName = displayName;
            if (profile is not null)
            {
                if (nameChanged)
                {
                    profile.InGameName = inGameName;
                    // Owner decision Q4: past matches show the new name too.
                    await _db.LeagueMatchPlayers.Where(m => m.ProfileId == profile.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.NameSnapshot, inGameName));
                }
                if (riotChanged) profile.RiotId = riotId;
            }
            _audit.Log(actor, "Changed names", $"{who}: {string.Join("; ", changes)}");
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return (true, $"Saved names for {who}.", errors);
        }
    

        // Step 13a ("Fill from screenshots", step 1): saves the game name of match players who have none. The name goes into the
        // profile's Riot ID, and ONLY where that profile has no game name yet (a name that is already there is never changed here).
        // All or nothing: one refused row writes nothing. Two players of the match may not end up with the same game name.
        // Each saved name is audit-logged like a names change (Admin > Activity).
        public const int MaxImportNameRows = 20;

        public async Task<LeagueImportNamesResponse> SaveGameNamesAsync(LeagueImportNamesRequest req, ClaimsPrincipal actor)
        {
            static LeagueImportNamesResponse Fail(string msg) => new() { Ok = false, Error = msg };
            if ((req.Names?.Count ?? 0) > MaxImportNameRows) return Fail("Too many names.");

            var match = await _db.LeagueMatches.AsNoTracking()
                .Include(x => x.Teams).ThenInclude(t => t.Players)
                .FirstOrDefaultAsync(x => x.Id == req.Id);
            if (match == null) return Fail("That match no longer exists.");
            if (match.Status != LeagueMatchStatus.Done) return Fail("Names can only be added for a finished match.");

            var players = match.Teams.SelectMany(t => t.Players).ToList();
            var profileIds = players.Where(p => p.ProfileId != null).Select(p => p.ProfileId!.Value).Distinct().ToList();
            var profiles = await _db.LeagueProfiles.Where(p => profileIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

            // What to write: profile id -> text to store as the Riot ID (only profiles with no game name yet).
            var writes = new Dictionary<int, string>();
            var skipped = new List<string>();
            foreach (var row in req.Names ?? new List<LeagueImportNameDto?>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.GameName)) continue;
                var player = players.FirstOrDefault(p => p.Id == row.PlayerId);
                if (player == null) return Fail("A player in the request is not in this match. Reload the page.");
                var who = string.IsNullOrWhiteSpace(player.NameSnapshot) ? "A player" : player.NameSnapshot.Trim();

                var error = LeagueGameNameRules.ValidateTyped(row.GameName, out var clean);
                if (error != null) return Fail($"{who}: {error}");
                if (player.ProfileId is not int pid || !profiles.TryGetValue(pid, out var profile))
                    return Fail($"{who}'s league profile no longer exists, so a name cannot be saved for them.");

                if (LeagueGameNameRules.FromRiotId(profile.RiotId) != null) { skipped.Add(who); continue; }
                writes[pid] = clean;
            }

            // No two players of this match with the same game name (what is saved now plus what is there already).
            var finalNames = players.Select(p =>
                p.ProfileId is int id && profiles.TryGetValue(id, out var pr)
                    ? LeagueGameNameRules.FromRiotId(writes.TryGetValue(id, out var w) ? w : pr.RiotId)
                    : null).ToList();
            var dup = LeagueGameNameRules.FindDuplicate(finalNames);
            if (dup != null) return Fail($"Two players in this match would have the game name \"{dup}\". Check the names.");

            if (writes.Count > 0)
            {
                foreach (var (pid, text) in writes)
                {
                    var profile = profiles[pid];
                    _audit.Log(actor, "Set game name", $"player {profile.InGameName}: Riot ID (empty) -> \"{text}\" (from Fill from screenshots, match {match.Id})");
                    profile.RiotId = text;
                }
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateException) { return Fail("The names could not be saved. Reload the page and try again."); }
            }

            var res = new LeagueImportNamesResponse { Ok = true, Saved = writes.Count };
            res.Message = writes.Count == 0 && skipped.Count == 0 ? "Nothing to save."
                : $"Saved {writes.Count} name{(writes.Count == 1 ? "" : "s")}."
                  + (skipped.Count > 0 ? $" Not changed (they already have a game name): {string.Join(", ", skipped)}." : "");
            res.Players = players.Select(p => new LeagueImportNameDto
            {
                PlayerId = p.Id,
                GameName = p.ProfileId is int id && profiles.TryGetValue(id, out var pr) ? LeagueGameNameRules.FromRiotId(pr.RiotId) : null,
            }).ToList();
            return res;
        }
    }
}
