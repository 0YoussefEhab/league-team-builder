using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Managed champion catalog (docs/LEAGUE_BUILD_SPEC.md section 7). No free text anywhere: members
    // pick from this list, the server validates ids. A champion that is "in use" (picked on a profile
    // lane OR recorded on a match, finished or not) is disabled (hidden from pickers, shown as "retired")
    // instead of deleted. Disabled champions can be enabled again at any time (Step 4 decision).
    public class ChampionCatalogService
    {
        public const int MaxNameLength = 60;

        private readonly ApplicationDbContext _db;

        public ChampionCatalogService(ApplicationDbContext db)
        {
            _db = db;
        }

        // UsageCount = profile picks + match entries (what decides delete vs disable); MatchCount is the match part.
        public record ChampionRow(LeagueChampion Champion, int UsageCount, int MatchCount = 0);

        public Task<List<LeagueChampion>> GetActiveAsync() =>
            _db.LeagueChampions.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();

        public Task<List<LeagueChampion>> GetByIdsAsync(IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            return _db.LeagueChampions.AsNoTracking().Where(c => list.Contains(c.Id)).ToListAsync();
        }

        public async Task<List<ChampionRow>> GetAllWithUsageAsync(string? search)
        {
            var query = _db.LeagueChampions.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(c => c.Name.ToLower().Contains(s));
            }
            var champs = await query.OrderBy(c => c.Name).ToListAsync();

            var usage = await _db.LeagueProfileChamps
                .GroupBy(p => p.ChampionId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);

            var matchUsage = await _db.LeagueMatchPlayers
                .Where(p => p.ChampionId != null)
                .GroupBy(p => p.ChampionId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);

            return champs.Select(c =>
            {
                var picks = usage.TryGetValue(c.Id, out var n) ? n : 0;
                var matches = matchUsage.TryGetValue(c.Id, out var m) ? m : 0;
                return new ChampionRow(c, picks + matches, matches);
            }).ToList();
        }

        public async Task<(bool Ok, string Message)> AddAsync(string? name)
        {
            var clean = Clean(name);
            if (clean is null)
                return (false, $"Enter a champion name (1-{MaxNameLength} characters).");
            if (await NameTakenAsync(clean, null))
                return (false, $"'{clean}' is already in the catalog.");

            _db.LeagueChampions.Add(new LeagueChampion { Name = clean, IsActive = true });
            await _db.SaveChangesAsync();
            return (true, $"Added {clean}.");
        }

        // One name per line. Skips blanks, too-long names and duplicates (case-insensitive).
        public async Task<(int Added, int Skipped)> BulkAddAsync(string? names)
        {
            var wanted = (names ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Clean)
                .Where(n => n is not null)
                .Select(n => n!)
                .GroupBy(n => n.ToLowerInvariant())
                .Select(g => g.First())
                .ToList();

            var existing = (await _db.LeagueChampions.Select(c => c.Name).ToListAsync())
                .Select(n => n.ToLowerInvariant())
                .ToHashSet();

            var toAdd = wanted.Where(n => !existing.Contains(n.ToLowerInvariant())).ToList();
            if (toAdd.Count > 0)
            {
                _db.LeagueChampions.AddRange(toAdd.Select(n => new LeagueChampion { Name = n, IsActive = true }));
                await _db.SaveChangesAsync();
            }

            var totalLines = (names ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Count(l => !string.IsNullOrWhiteSpace(l));
            return (toAdd.Count, totalLines - toAdd.Count);
        }

        public async Task<(bool Ok, string Message)> RenameAsync(int id, string? name)
        {
            var champ = await _db.LeagueChampions.FirstOrDefaultAsync(c => c.Id == id);
            if (champ is null)
                return (false, "Champion not found.");
            var clean = Clean(name);
            if (clean is null)
                return (false, $"Enter a champion name (1-{MaxNameLength} characters).");
            if (await NameTakenAsync(clean, id))
                return (false, $"'{clean}' is already in the catalog.");

            champ.Name = clean;
            await _db.SaveChangesAsync();
            return (true, $"Renamed to {clean}.");
        }

        public async Task<(bool Ok, string Message)> SetActiveAsync(int id, bool active)
        {
            var champ = await _db.LeagueChampions.FirstOrDefaultAsync(c => c.Id == id);
            if (champ is null)
                return (false, "Champion not found.");

            champ.IsActive = active;
            await _db.SaveChangesAsync();
            return (true, active ? $"{champ.Name} enabled." : $"{champ.Name} disabled (hidden from pickers).");
        }

        // Hard-deletes only when the champion is used nowhere (no profile pick, no match entry, past
        // matches included); otherwise disables it. Nothing is cleared from a match any more.
        public async Task<(bool Ok, string Message)> RemoveAsync(int id)
        {
            var champ = await _db.LeagueChampions.FirstOrDefaultAsync(c => c.Id == id);
            if (champ is null)
                return (false, "Champion not found.");

            var usedOnProfile = await _db.LeagueProfileChamps.AnyAsync(p => p.ChampionId == id);
            var usedInMatch = await _db.LeagueMatchPlayers.AnyAsync(p => p.ChampionId == id);
            if (usedOnProfile || usedInMatch)
            {
                champ.IsActive = false;
                await _db.SaveChangesAsync();
                var where = usedOnProfile && usedInMatch ? "profiles and past matches"
                    : usedInMatch ? "past matches" : "existing profiles";
                return (true, $"{champ.Name} is used on {where}, so it was disabled instead of deleted. You can enable it again any time.");
            }

            _db.LeagueChampions.Remove(champ);
            await _db.SaveChangesAsync();
            return (true, $"{champ.Name} deleted.");
        }

        private static string? Clean(string? name)
        {
            var n = (name ?? string.Empty).Trim();
            return n.Length is >= 1 and <= MaxNameLength ? n : null;
        }

        private Task<bool> NameTakenAsync(string name, int? exceptId)
        {
            var lower = name.ToLower();
            return _db.LeagueChampions.AnyAsync(c => c.Name.ToLower() == lower && (exceptId == null || c.Id != exceptId));
        }
    }
}
