using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Admin-defined custom ranking tiers (docs/LEAGUE_BUILD_SPEC.md R3, sections 3 and 11). SortOrder is
    // 1..n and the TOP of the list (SortOrder 1) is the strongest tier. Balancing mode 4 (Step 4) uses
    // them, and must fall back to rank for a player with no tier.
    public class LeagueTierService
    {
        public const int MaxNameLength = 40;

        private readonly ApplicationDbContext _db;

        public LeagueTierService(ApplicationDbContext db)
        {
            _db = db;
        }

        public record TierRow(LeagueCustomTier Tier, int PlayerCount);
        public record PlayerTierRow(int ProfileId, string Name, bool IsGuest, LeagueRank Rank, int? Div, int? TierId);

        public async Task<List<TierRow>> GetTiersAsync()
        {
            var tiers = await _db.LeagueCustomTiers.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
            var counts = await _db.LeagueProfiles.Where(p => p.CustomTierId != null)
                .GroupBy(p => p.CustomTierId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);
            return tiers.Select(t => new TierRow(t, counts.TryGetValue(t.Id, out var n) ? n : 0)).ToList();
        }

        public async Task<List<PlayerTierRow>> GetPlayersAsync() =>
            (await _db.LeagueProfiles.AsNoTracking().OrderBy(p => p.InGameName)
                .Select(p => new { p.Id, p.InGameName, p.IsGuest, p.CurTier, p.CurDiv, p.CustomTierId })
                .ToListAsync())
            .Select(p => new PlayerTierRow(p.Id, p.InGameName, p.IsGuest, p.CurTier, p.CurDiv, p.CustomTierId))
            .ToList();

        private static string? Clean(string? name)
        {
            var n = (name ?? string.Empty).Trim();
            return n.Length is >= 1 and <= MaxNameLength ? n : null;
        }

        private async Task RenumberAsync()
        {
            var all = await _db.LeagueCustomTiers.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
            for (var i = 0; i < all.Count; i++) all[i].SortOrder = i + 1;
            await _db.SaveChangesAsync();
        }

        public async Task<(bool Ok, string Message)> AddAsync(string? name)
        {
            var clean = Clean(name);
            if (clean is null) return (false, $"Enter a tier name (1-{MaxNameLength} characters).");
            var last = await _db.LeagueCustomTiers.MaxAsync(t => (int?)t.SortOrder) ?? 0;
            _db.LeagueCustomTiers.Add(new LeagueCustomTier { Name = clean, SortOrder = last + 1 });
            await _db.SaveChangesAsync();
            return (true, $"Added tier {clean} at the bottom.");
        }

        public async Task<(bool Ok, string Message)> RenameAsync(int id, string? name)
        {
            var clean = Clean(name);
            if (clean is null) return (false, $"Enter a tier name (1-{MaxNameLength} characters).");
            var tier = await _db.LeagueCustomTiers.FindAsync(id);
            if (tier is null) return (false, "That tier no longer exists.");
            tier.Name = clean;
            await _db.SaveChangesAsync();
            return (true, $"Renamed the tier to {clean}.");
        }

        public async Task<(bool Ok, string Message)> MoveAsync(int id, bool up)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var all = await _db.LeagueCustomTiers.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
            var i = all.FindIndex(t => t.Id == id);
            if (i < 0) return (false, "That tier no longer exists.");
            var j = up ? i - 1 : i + 1;
            if (j < 0 || j >= all.Count) return (false, up ? "It is already at the top." : "It is already at the bottom.");
            (all[i], all[j]) = (all[j], all[i]);
            for (var k = 0; k < all.Count; k++) all[k].SortOrder = k + 1;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return (true, "Moved.");
        }

        // The tier's players go to the neighbouring tier (the one above, else below) in the same
        // transaction. With no other tier left they simply have no tier.
        public async Task<(bool Ok, string Message)> RemoveAsync(int id)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var all = await _db.LeagueCustomTiers.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
            var i = all.FindIndex(t => t.Id == id);
            if (i < 0) return (false, "That tier no longer exists.");

            int? neighbourId = i > 0 ? all[i - 1].Id : i + 1 < all.Count ? all[i + 1].Id : null;
            var moved = await _db.LeagueProfiles.Where(p => p.CustomTierId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CustomTierId, neighbourId));

            var name = all[i].Name;
            _db.LeagueCustomTiers.Remove(all[i]);
            await _db.SaveChangesAsync();
            await RenumberAsync();
            await tx.CommitAsync();

            var where = neighbourId is null ? "have no tier now" : "moved to a neighbouring tier";
            return (true, moved == 0 ? $"Removed tier {name}." : $"Removed tier {name}. {moved} player(s) {where}.");
        }

        public async Task<(bool Ok, string Message)> SetPlayerTierAsync(int profileId, int? tierId)
        {
            var profile = await _db.LeagueProfiles.FindAsync(profileId);
            if (profile is null) return (false, "That player no longer exists.");
            if (tierId is int t && !await _db.LeagueCustomTiers.AnyAsync(x => x.Id == t))
                return (false, "That tier no longer exists.");
            profile.CustomTierId = tierId;
            await _db.SaveChangesAsync();
            return (true, $"Updated the tier for {profile.InGameName}.");
        }
    }
}
