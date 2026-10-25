using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Admin fixes Step 4d: the Unlinked / Linked / All filter on League Settings > Guests. Default Unlinked.
    public static class LeagueGuestFilter
    {
        public const string Unlinked = "unlinked";
        public const string Linked = "linked";
        public const string All = "all";

        public static string Parse(string? value)
        {
            var v = (value ?? string.Empty).Trim().ToLowerInvariant();
            return v is Linked or All ? v : Unlinked;
        }
    }

    // Guest profiles an officer manages (docs/LEAGUE_BUILD_SPEC.md sections 3 and 6). A guest is a
    // LeagueProfile row with IsGuest = true and UserId = null. Linking a guest to a real user is Step 7.
    public class LeagueGuestService
    {
        public const int MaxNameLength = 40;

        private readonly ApplicationDbContext _db;

        public LeagueGuestService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<LeagueGuestRow>> GetGuestsAsync()
        {
            var guests = await _db.LeagueProfiles.AsNoTracking().Include(p => p.Lanes)
                .Where(p => p.IsGuest).OrderBy(p => p.InGameName).ToListAsync();
            return guests.Select(ToRow).ToList();
        }

        private static LeagueGuestRow ToRow(LeagueProfile g) => new()
        {
            Id = g.Id, Name = g.InGameName,
            CurTier = g.CurTier, CurDiv = g.CurDiv, PeakTier = g.PeakTier, PeakDiv = g.PeakDiv,
            MainLane = g.MainLane,
            OtherLanes = g.Lanes.Where(l => l.Lane != g.MainLane && l.Level > LeagueLaneLevels.Min).OrderBy(l => l.Priority).Select(l => l.Lane).ToList(),
        };

        // Linked = a member profile (not a guest) that has an active link-log entry for its current user, plus older
        // links without a log that are detected by match entries made while it was a guest (WasGuest). The second
        // part is a guess: a guest who never played has no trace and is not found.
        private IQueryable<LeagueProfile> LinkedProfiles() =>
            _db.LeagueProfiles.AsNoTracking().Where(p => !p.IsGuest && p.UserId != null
                && (_db.LeagueGuestLinkLogs.Any(l => l.ProfileId == p.Id && l.UserId == p.UserId && l.UndoneUtc == null)
                    || _db.LeagueMatchPlayers.Any(m => m.ProfileId == p.Id && m.WasGuest)));

        public async Task<List<LeagueGuestRow>> GetLinkedAsync()
        {
            var linked = await LinkedProfiles().Include(p => p.Lanes).Include(p => p.User)
                .OrderBy(p => p.InGameName).ToListAsync();
            var ids = linked.Select(p => p.Id).ToList();
            var logs = await _db.LeagueGuestLinkLogs.AsNoTracking()
                .Where(l => ids.Contains(l.ProfileId) && l.UndoneUtc == null)
                .OrderByDescending(l => l.LinkedUtc).ToListAsync();

            return linked.Select(p =>
            {
                var row = ToRow(p);
                var log = logs.FirstOrDefault(l => l.ProfileId == p.Id && l.UserId == p.UserId);
                row.IsLinked = true;
                row.UserLabel = p.User is null ? "(unknown user)"
                    : !string.IsNullOrWhiteSpace(p.User.DisplayName) ? p.User.DisplayName
                    : (p.User.UserName ?? p.User.Email ?? "(unknown user)");
                row.LogId = log?.Id;
                row.LinkedUtc = log?.LinkedUtc;
                row.LinkedBy = log?.LinkedByLabel;
                return row;
            }).ToList();
        }

        // The rows for the chosen filter and the two counts for the filter buttons.
        public async Task<(List<LeagueGuestRow> Rows, int UnlinkedCount, int LinkedCount)> GetRowsAsync(string? filter)
        {
            filter = LeagueGuestFilter.Parse(filter);
            var rows = new List<LeagueGuestRow>();
            if (filter != LeagueGuestFilter.Linked) rows.AddRange(await GetGuestsAsync());
            if (filter != LeagueGuestFilter.Unlinked) rows.AddRange(await GetLinkedAsync());
            var unlinked = await _db.LeagueProfiles.CountAsync(p => p.IsGuest);
            var linked = await LinkedProfiles().CountAsync();
            return (rows, unlinked, linked);
        }

        public async Task<LeagueGuestForm?> GetFormAsync(int id)
        {
            var g = await _db.LeagueProfiles.AsNoTracking().Include(p => p.Lanes)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsGuest);
            if (g is null) return null;
            var form = new LeagueGuestForm
            {
                Id = g.Id, InGameName = g.InGameName,
                CurTier = g.CurTier, CurDiv = g.CurDiv, PeakTier = g.PeakTier, PeakDiv = g.PeakDiv,
                PeakSameAsCur = g.PeakTier == g.CurTier && g.PeakDiv == g.CurDiv,
                MainLane = g.MainLane,
                Lanes = g.Lanes.Where(l => l.Lane != g.MainLane).Select(l => l.Lane).ToList(),
            };
            // Step 8f: the five slider values (a lane with no row is level 1).
            foreach (var (lane, level) in LeagueLaneLevels.LevelsOf(g.Lanes.Select(l => new LaneLevel(l.Lane, l.Level, l.Priority))))
                form.SetLevel(lane, level);
            return form;
        }

        // Creates (form.Id == 0) or updates a guest. Returns the field errors (empty on success) and the id.
        public async Task<(Dictionary<string, string> Errors, int Id)> SaveAsync(LeagueGuestForm f)
        {
            var errors = new Dictionary<string, string>();

            var name = (f.InGameName ?? string.Empty).Trim();
            if (name.Length == 0) errors["InGameName"] = "Enter the guest's name.";
            else if (name.Length > MaxNameLength) errors["InGameName"] = $"The name can be at most {MaxNameLength} characters.";

            LeagueRank peakTier = f.PeakTier;
            int? peakDiv = f.PeakDiv;
            if (!Enum.IsDefined(f.CurTier) || (!f.PeakSameAsCur && !Enum.IsDefined(f.PeakTier)))
                errors["Rank"] = "Pick a valid rank.";
            else
            {
                var curDiv = LeagueRankRules.HasDivision(f.CurTier) ? f.CurDiv : null;
                if (LeagueRankRules.HasDivision(f.CurTier) && curDiv is not (>= 1 and <= 4))
                    errors["CurDiv"] = "Pick the current division (1-4).";
                if (f.PeakSameAsCur) { peakTier = f.CurTier; peakDiv = curDiv; }
                else
                {
                    peakDiv = LeagueRankRules.HasDivision(f.PeakTier) ? f.PeakDiv : null;
                    if (LeagueRankRules.HasDivision(f.PeakTier) && peakDiv is not (>= 1 and <= 4))
                        errors["PeakDiv"] = "Pick the peak division (1-4).";
                    else if (!errors.ContainsKey("CurDiv") &&
                             LeagueRankRules.Value(f.PeakTier, peakDiv) < LeagueRankRules.Value(f.CurTier, curDiv))
                        errors["Peak"] = "Peak rank can't be lower than the current rank.";
                }
            }

            LeagueProfile? guest = null;
            if (f.Id != 0)
            {
                guest = await _db.LeagueProfiles.FirstOrDefaultAsync(p => p.Id == f.Id && p.IsGuest);
                if (guest is null) errors["Guest"] = "That guest no longer exists.";
            }

            // Step 8f: lane levels 1-5 (the sliders). The old order (Priority) only breaks ties between equal levels.
            var oldPriority = new Dictionary<LeagueLane, int>();
            if (guest is not null)
                oldPriority = (await _db.LeagueProfileLanes.Where(l => l.ProfileId == guest.Id)
                    .Select(l => new { l.Lane, l.Priority }).ToListAsync())
                    .ToDictionary(l => l.Lane, l => l.Priority);
            if (!LeagueLaneLevels.TryPlanSave(f.Levels(), oldPriority, out var lanePlan, out var laneError))
                errors["Lanes"] = laneError;
            if (errors.Count > 0) return (errors, f.Id);

            await using var tx = await _db.Database.BeginTransactionAsync();
            if (guest is null)
            {
                guest = new LeagueProfile { IsGuest = true, UserId = null, CreatedUtc = DateTime.UtcNow };
                _db.LeagueProfiles.Add(guest);
            }
            guest.InGameName = name;
            guest.CurTier = f.CurTier;
            guest.CurDiv = LeagueRankRules.HasDivision(f.CurTier) ? f.CurDiv : null;
            guest.PeakTier = peakTier;
            guest.PeakDiv = LeagueRankRules.HasDivision(peakTier) ? peakDiv : null;
            guest.MainLane = lanePlan.Main;
            await _db.SaveChangesAsync();

            await _db.LeagueProfileLanes.Where(l => l.ProfileId == guest.Id).ExecuteDeleteAsync();
            foreach (var row in lanePlan.Rows)
                _db.LeagueProfileLanes.Add(new LeagueProfileLane { ProfileId = guest.Id, Lane = row.Lane, Priority = row.Priority, Level = row.Level });
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return (errors, guest.Id);
        }

        // Only a guest can be deleted here; a member's league profile is theirs to manage.
        public async Task<(bool Ok, string Message)> DeleteAsync(int id)
        {
            var guest = await _db.LeagueProfiles.FirstOrDefaultAsync(p => p.Id == id && p.IsGuest);
            if (guest is null) return (false, "That guest no longer exists.");
            var name = guest.InGameName;
            _db.LeagueProfiles.Remove(guest);
            await _db.SaveChangesAsync();
            return (true, $"Deleted guest {name}.");
        }
    }
}
