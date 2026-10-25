using System.Globalization;
using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace LeagueTeamBuilder.Services
{
    // Limits and labels for the three game modes (docs/LEAGUE_BUILD_SPEC.md section 11). Nothing about
    // player counts is hard-coded elsewhere, so the numbers can follow whatever Riot changes.
    public static class LeagueModeRules
    {
        public const int MinPlayers = 2;
        public const int MinArenaTeams = 2;
        public const int MaxArenaTeams = 20;
        public const int DefaultArenaTeams = 8;

        public static readonly LeagueMode[] All = { LeagueMode.Normal, LeagueMode.Aram, LeagueMode.Arena };

        public static string Name(LeagueMode m) => m switch
        {
            LeagueMode.Normal => "Normal Draft",
            LeagueMode.Aram => "ARAM",
            _ => "Arena",
        };

        // Normal Draft is lane based (5 lanes per team) so it tops out at 10.
        public static int MaxPlayers(LeagueMode m) => m == LeagueMode.Normal ? 10 : 40;

        public static int DefaultPlayers(LeagueMode m) => m switch
        {
            LeagueMode.Normal => 10,
            LeagueMode.Aram => 10,
            _ => 16,
        };

        public static bool HasSpots(LeagueMode m) => m != LeagueMode.Arena;

        // Spots per team: the 5 lanes in Normal, half the players (rounded up) in ARAM.
        public static int SlotsPerTeam(LeagueMode m, int playerCount) => m switch
        {
            LeagueMode.Normal => 5,
            LeagueMode.Aram => (playerCount + 1) / 2,
            _ => 0,
        };

        public static string SlotLabel(LeagueMode m, int team, int slot) =>
            $"T{team + 1} " + (m == LeagueMode.Normal ? ((LeagueLane)slot).ToString() : "#" + (slot + 1));
    }

    // One field of the spot editor, bound from spots[i].Team / .Slot / .X / .Y. X and Y are strings so a
    // blank means "no position" and a decimal never depends on the server's culture.
    public class LeagueSpotInput
    {
        public int Team { get; set; }
        public int Slot { get; set; }
        public string? X { get; set; }
        public string? Y { get; set; }
    }

    // Per-mode settings, map image and map spots (docs/LEAGUE_BUILD_SPEC.md sections 3 and 11).
    public class LeagueSettingsService
    {
        public const string MapFolder = "uploads/league-maps";

        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;

        public LeagueSettingsService(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // All three modes, in Normal / ARAM / Arena order. Missing rows are created with the defaults.
        public async Task<List<LeagueModeSettings>> GetAllAsync()
        {
            var rows = await _db.LeagueModeSettings.Include(m => m.Spots).ToListAsync();
            var created = false;
            foreach (var mode in LeagueModeRules.All)
            {
                if (rows.Any(r => r.Mode == mode)) continue;
                var row = new LeagueModeSettings
                {
                    Mode = mode,
                    PlayerCount = LeagueModeRules.DefaultPlayers(mode),
                    TeamCount = mode == LeagueMode.Arena ? LeagueModeRules.DefaultArenaTeams : null,
                };
                _db.LeagueModeSettings.Add(row);
                rows.Add(row);
                created = true;
            }
            if (created) await _db.SaveChangesAsync();
            return LeagueModeRules.All.Select(m => rows.First(r => r.Mode == m)).ToList();
        }

        private async Task<LeagueModeSettings> GetOneAsync(LeagueMode mode) =>
            (await GetAllAsync()).First(m => m.Mode == mode);

        public async Task<(bool Ok, string Message)> SaveCountsAsync(int normal, int aram, int arena, int arenaTeams)
        {
            var wanted = new Dictionary<LeagueMode, int>
            {
                [LeagueMode.Normal] = normal, [LeagueMode.Aram] = aram, [LeagueMode.Arena] = arena,
            };
            foreach (var (mode, count) in wanted)
                if (count < LeagueModeRules.MinPlayers || count > LeagueModeRules.MaxPlayers(mode))
                    return (false, $"{LeagueModeRules.Name(mode)} needs {LeagueModeRules.MinPlayers}-{LeagueModeRules.MaxPlayers(mode)} players.");
            if (arenaTeams < LeagueModeRules.MinArenaTeams || arenaTeams > LeagueModeRules.MaxArenaTeams)
                return (false, $"Arena teams must be {LeagueModeRules.MinArenaTeams}-{LeagueModeRules.MaxArenaTeams}.");

            var rows = await GetAllAsync();
            foreach (var row in rows)
            {
                row.PlayerCount = wanted[row.Mode];
                if (row.Mode == LeagueMode.Arena) row.TeamCount = arenaTeams;

                // Spots for slots that no longer exist (fewer ARAM players) are dropped.
                var keep = LeagueModeRules.SlotsPerTeam(row.Mode, row.PlayerCount);
                var stale = row.Spots.Where(s => s.Slot >= keep).ToList();
                if (stale.Count > 0) _db.LeagueMapSpots.RemoveRange(stale);
            }
            await _db.SaveChangesAsync();
            return (true, "Saved players per mode.");
        }

        // Validates the file (type, size, and that it really decodes as an image), reads its real
        // aspect ratio, uploads it, then swaps it in. The old file is deleted only after the save.
        public async Task<(bool Ok, string Message)> SetMapImageAsync(LeagueMode mode, IFormFile? file)
        {
            if (file is null || file.Length == 0)
                return (false, "Choose an image first.");

            var problem = FileUploadService.ValidatePublicFile(file, "Map image",
                MediaUploadLimits.PublicImageExtensions.Where(e => e != ".gif" && e != ".jfif").ToArray(),
                MediaUploadLimits.PublicImageMaxBytes);
            if (problem is not null) return (false, problem);

            double aspect;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                using var data = SKData.CreateCopy(ms.ToArray());
                using var codec = SKCodec.Create(data);
                if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                    return (false, "That file isn't a readable image.");
                aspect = Math.Clamp((double)codec.Info.Width / codec.Info.Height, 0.25, 4.0);
            }

            var url = await FileUploadService.SaveAsync(file, _env.WebRootPath, MapFolder);
            if (url is null) return (false, "The upload failed. Try again.");

            var row = await GetOneAsync(mode);
            var old = row.MapImageUrl;
            row.MapImageUrl = url;
            row.MapAspect = Math.Round(aspect, 4);
            await _db.SaveChangesAsync();
            FileUploadService.DeleteReplacedUpload(old, url, _env.WebRootPath);
            return (true, $"Updated the {LeagueModeRules.Name(mode)} map image.");
        }

        public async Task<(bool Ok, string Message)> RemoveMapImageAsync(LeagueMode mode)
        {
            var row = await GetOneAsync(mode);
            if (row.MapImageUrl is null) return (false, "There is no map image to remove.");
            var old = row.MapImageUrl;
            row.MapImageUrl = null;
            row.MapAspect = 16d / 9d;
            await _db.SaveChangesAsync();
            FileUploadService.DeleteReplacedUpload(old, null, _env.WebRootPath);
            return (true, $"Removed the {LeagueModeRules.Name(mode)} map image.");
        }

        // Replaces every spot of the mode with what the form posted. Blank X and Y = no position.
        public async Task<(bool Ok, string Message)> SaveSpotsAsync(LeagueMode mode, List<LeagueSpotInput>? inputs)
        {
            if (!LeagueModeRules.HasSpots(mode))
                return (false, "Arena has no map positions.");

            var row = await GetOneAsync(mode);
            var perTeam = LeagueModeRules.SlotsPerTeam(mode, row.PlayerCount);
            var spots = new Dictionary<(int Team, int Slot), LeagueMapSpot>();

            foreach (var i in inputs ?? new List<LeagueSpotInput>())
            {
                if (i.Team is < 0 or > 1 || i.Slot < 0 || i.Slot >= perTeam)
                    return (false, "That position doesn't exist for this mode.");
                var hasX = !string.IsNullOrWhiteSpace(i.X);
                var hasY = !string.IsNullOrWhiteSpace(i.Y);
                if (!hasX && !hasY) continue;
                var label = LeagueModeRules.SlotLabel(mode, i.Team, i.Slot);
                if (!hasX || !hasY)
                    return (false, $"{label}: fill in both X and Y, or clear both.");
                if (!double.TryParse(i.X!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !double.TryParse(i.Y!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                    double.IsNaN(x) || double.IsNaN(y) || x < 0 || x > 100 || y < 0 || y > 100)
                    return (false, $"{label}: X and Y must be numbers from 0 to 100.");
                spots[(i.Team, i.Slot)] = new LeagueMapSpot
                {
                    Mode = mode, Team = i.Team, Slot = i.Slot,
                    X = Math.Round(x, 1), Y = Math.Round(y, 1),
                };
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            await _db.LeagueMapSpots.Where(s => s.Mode == mode).ExecuteDeleteAsync();
            // The old spots were loaded (tracked) by GetOneAsync; drop them so the new rows with the
            // same keys can be added without an "already tracked" error.
            _db.ChangeTracker.Clear();
            _db.LeagueMapSpots.AddRange(spots.Values);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return (true, $"Saved {spots.Count} position(s) for {LeagueModeRules.Name(mode)}.");
        }
    }
}
