using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Step 12h2: reads and saves the League Discord auto-send switches (one row, both off until saved).
    // It only holds the switches. The picture is drawn and posted by the browser (league-auto-send.js -> POST /LeagueExport/Send).
    public class LeagueAutoSendService
    {
        private readonly ApplicationDbContext _db;

        public LeagueAutoSendService(ApplicationDbContext db) { _db = db; }

        public async Task<LeagueAutoSendSettings> GetAsync()
        {
            var row = await _db.LeagueAutoSendSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == LeagueAutoSendSettings.RowId);
            return row ?? new LeagueAutoSendSettings();
        }

        public async Task SaveAsync(bool gameResult, bool clashProgress, string? userId)
        {
            var row = await _db.LeagueAutoSendSettings.FirstOrDefaultAsync(x => x.Id == LeagueAutoSendSettings.RowId);
            if (row == null)
            {
                row = new LeagueAutoSendSettings { Id = LeagueAutoSendSettings.RowId };
                _db.LeagueAutoSendSettings.Add(row);
            }
            row.GameResult = gameResult;
            row.ClashProgress = clashProgress;
            row.UpdatedUtc = DateTime.UtcNow;
            row.UpdatedByUserId = userId;
            await _db.SaveChangesAsync();
        }
    }
}
