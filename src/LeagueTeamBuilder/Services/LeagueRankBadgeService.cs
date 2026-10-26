using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Phase B1: uploads, removes and resets the rank badge pictures (the page is built in B3). Admin work only (RolePermissions).
    // Rules: png / jpg / jpeg / webp only (never SVG), 1 MB, must decode as an image (RankBadgeRules). A new file is saved first,
    // then the row, and the OLD file is deleted only after the row is saved, so a failed save never loses a working badge.
    public class LeagueRankBadgeService
    {
        public const string Folder = "uploads/rank-badges";

        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly RankBadgeCatalog _catalog;

        public LeagueRankBadgeService(ApplicationDbContext db, IWebHostEnvironment env, RankBadgeCatalog catalog)
        {
            _db = db;
            _env = env;
            _catalog = catalog;
        }

        public record BadgeRow(LeagueRank Tier, string? UploadedUrl, string Url);

        // The ten tiers, weakest first, for the Badges page.
        public async Task<List<BadgeRow>> GetAllAsync()
        {
            var rows = await _db.LeagueRankBadges.AsNoTracking().ToDictionaryAsync(r => r.Tier, r => r.ImageUrl);
            return Enum.GetValues<LeagueRank>().OrderBy(r => (int)r)
                .Select(t =>
                {
                    var up = rows.GetValueOrDefault(t);
                    return new BadgeRow(t, up, string.IsNullOrWhiteSpace(up) ? RankBadgeCatalog.DefaultUrl(t) : up);
                }).ToList();
        }

        // Reads the table into the catalog. Called at start-up and after every change.
        public async Task ReloadCatalogAsync()
        {
            var rows = await _db.LeagueRankBadges.AsNoTracking().ToListAsync();
            _catalog.Replace(rows);
        }

        public async Task<(bool Ok, string Message)> SetAsync(LeagueRank tier, IFormFile? file)
        {
            if (!Enum.IsDefined(tier)) return (false, "Unknown rank.");

            var problem = FileUploadService.ValidatePublicFile(file, "Rank badge",
                MediaUploadLimits.RankBadgeExtensions, MediaUploadLimits.RankBadgeMaxBytes);
            if (problem is not null) return (false, problem);

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await file!.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
            var ext = (Path.GetExtension(file.FileName) ?? "").ToLowerInvariant();
            var bad = RankBadgeRules.Check(bytes, ext);
            if (bad is not null) return (false, bad);

            var url = await FileUploadService.SaveAsync(file, _env.WebRootPath, Folder);
            if (url is null) return (false, "The upload failed. Try again.");

            var row = await _db.LeagueRankBadges.FirstOrDefaultAsync(r => r.Tier == tier);
            var old = row?.ImageUrl;
            if (row is null) _db.LeagueRankBadges.Add(new LeagueRankBadge { Tier = tier, ImageUrl = url });
            else row.ImageUrl = url;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Nothing was saved: the new file is an orphan, the old badge is untouched.
                FileUploadService.DeleteIfExists(url, _env.WebRootPath);
                return (false, "The badge could not be saved. Try again.");
            }

            FileUploadService.DeleteReplacedUpload(old, url, _env.WebRootPath);
            await ReloadCatalogAsync();
            return (true, $"Updated the {tier} badge.");
        }

        public async Task<(bool Ok, string Message)> RemoveAsync(LeagueRank tier)
        {
            var row = await _db.LeagueRankBadges.FirstOrDefaultAsync(r => r.Tier == tier);
            if (row is null || string.IsNullOrWhiteSpace(row.ImageUrl))
                return (false, $"The {tier} badge already uses the default picture.");

            var old = row.ImageUrl;
            _db.LeagueRankBadges.Remove(row);
            await _db.SaveChangesAsync();
            FileUploadService.DeleteReplacedUpload(old, null, _env.WebRootPath);
            await ReloadCatalogAsync();
            return (true, $"The {tier} badge is back to the default picture.");
        }

        public async Task<(bool Ok, string Message)> ResetAllAsync()
        {
            var rows = await _db.LeagueRankBadges.ToListAsync();
            if (rows.Count == 0) return (false, "Every badge already uses the default picture.");

            var olds = rows.Select(r => r.ImageUrl).ToList();
            _db.LeagueRankBadges.RemoveRange(rows);
            await _db.SaveChangesAsync();
            foreach (var old in olds) FileUploadService.DeleteReplacedUpload(old, null, _env.WebRootPath);
            await ReloadCatalogAsync();
            return (true, "All rank badges are back to the default pictures.");
        }
    }
}
