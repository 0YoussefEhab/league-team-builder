using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Step 8g: who holds the badges right now (Step 13 added the Clash winner badge) (decisions: PLAN-league.md, "Step 8g - decisions").
    // Rank = the #1 ranked player(s) of LeagueRanking (a full tie shares it). Mvp = the most custom DRAFT MVPs
    // (ties: all; nobody while the best count is 0). MVP redesign Step 5b: Clash and custom ARAM/Arena MVPs do not count here.
    public class LeagueBadgeHolders
    {
        public HashSet<int> Rank { get; init; } = new();
        public HashSet<int> Mvp { get; init; } = new();
        // Step 13 (owner request 2026-10-18): the players of the team shown as the best Clash team on the Overview, i.e. the most
        // recent ended Clash that was won. A newer won Clash replaces them (the badge moves with the home-screen team).
        public HashSet<int> Clash { get; init; } = new();

        public bool IsRankKing(int profileId) => Rank.Contains(profileId);
        public bool IsMvpKing(int profileId) => Mvp.Contains(profileId);
        public bool IsClashKing(int profileId) => Clash.Contains(profileId);
    }

    // Static on purpose: no new service to register (process rule 9) and no constructor change on the services that use it.
    public static class LeagueBadges
    {
        // Pure: the holders for a list of ranked players (the MVP counts are already in the inputs).
        public static LeagueBadgeHolders Holders(IEnumerable<RankInput> players, IEnumerable<int>? clashHolders = null)
        {
            var list = players.ToList();
            return new LeagueBadgeHolders
            {
                Rank = LeagueRanking.RankBadgeHolders(list),
                Mvp = LeagueRanking.MvpBadgeHolders(list),
                Clash = clashHolders == null ? new HashSet<int>() : new HashSet<int>(clashHolders),
            };
        }

        // Pure: the profile ids that hold the Clash badge = the Clash team (index 0) of the game the Overview shows for the best Clash.
        // No best Clash (none won and ended yet) = nobody. A player whose profile was deleted or merged away (null id) holds nothing.
        public static HashSet<int> ClashHolderIds(OvBestClash? best, IEnumerable<(int MatchId, int TeamIndex, int? ProfileId)> players)
        {
            var set = new HashSet<int>();
            if (best == null) return set;
            foreach (var p in players)
                if (p.MatchId == best.GameMatchId && p.TeamIndex == 0 && p.ProfileId is int id) set.Add(id);
            return set;
        }

        // Read-only: the same "best Clash" the Overview uses (LeagueOverviewStats.BestClash), then its team's profile ids.
        public static async Task<HashSet<int>> LoadClashHoldersAsync(ApplicationDbContext db, CancellationToken ct = default)
        {
            var events = await db.LeagueClashEvents.AsNoTracking()
                .Where(e => e.Status == LeagueClashStatus.Done)
                .Select(e => new { e.Id, e.Title, e.StartsAtUtc, e.EndedUtc })
                .ToListAsync(ct);
            if (events.Count == 0) return new HashSet<int>();
            var matches = await db.LeagueMatches.AsNoTracking()
                .Where(m => m.Status == LeagueMatchStatus.Done && m.Kind == LeagueMatchKind.Clash && m.ClashEventId != null)
                .Select(m => new { m.Id, m.Kind, m.ModeKey, m.ClashEventId, m.WinnerTeamIndex, m.PlayedUtc, m.DurationSeconds })
                .ToListAsync(ct);
            var best = LeagueOverviewStats.BestClash(
                events.Select(e => new OvClashEvent(e.Id, e.Title, e.StartsAtUtc, e.EndedUtc)),
                matches.Select(m => new OvMatch(m.Id, m.Kind, m.ModeKey, m.ClashEventId, m.WinnerTeamIndex, m.PlayedUtc, m.DurationSeconds)));
            if (best == null) return new HashSet<int>();
            var rows = await db.LeagueMatchPlayers.AsNoTracking()
                .Where(p => p.Team!.MatchId == best.GameMatchId && p.Team.Index == 0)
                .Select(p => new { MatchId = p.Team!.MatchId, TeamIndex = p.Team.Index, p.ProfileId })
                .ToListAsync(ct);
            return ClashHolderIds(best, rows.Select(r => (r.MatchId, r.TeamIndex, r.ProfileId)));
        }

        // Two small read-only queries (profiles, and the MVP columns of finished matches), then the pure rule.
        public static async Task<LeagueBadgeHolders> LoadAsync(ApplicationDbContext db, CancellationToken ct = default)
        {
            var profiles = await db.LeagueProfiles.AsNoTracking()
                .Select(p => new { p.Id, p.InGameName, p.CurTier, p.CurDiv, p.PeakTier, p.PeakDiv })
                .ToListAsync(ct);
            var matches = await db.LeagueMatches.AsNoTracking()
                .Where(m => m.Status == LeagueMatchStatus.Done)
                .Select(m => new { m.Kind, m.ModeKey, m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId })
                .ToListAsync(ct);
            var counts = LeagueOverviewStats.DraftMvpCounts(
                matches.Select(m => (m.Kind, m.ModeKey, new MvpPicks(m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId))));
            return Holders(profiles.Select(p => new RankInput(p.Id, p.InGameName, p.CurTier, p.CurDiv, p.PeakTier, p.PeakDiv,
                counts.GetValueOrDefault(p.Id))), await LoadClashHoldersAsync(db, ct));
        }
    }
}
