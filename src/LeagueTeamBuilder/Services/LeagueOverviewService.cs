using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Services
{
    // Step 8b: loads the data for the League Overview (top 3 ranked, top 3 MVPs, best Clash team, server stats).
    // Read only: nothing here changes data. The rules are in LeagueRanking (8a) and LeagueOverviewStats (8b).
    // The page (League/Index) comes in 8c. Registered in Program.cs (process rule 9).
    // 8d added GetLeaderboardAsync, 8e added GetPlayerAsync (the Player page).
    public class LeagueOverviewService
    {
        private readonly ApplicationDbContext _db;

        public LeagueOverviewService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<LeagueOverviewModel> GetAsync(CancellationToken ct = default)
        {
            var profiles = await _db.LeagueProfiles.AsNoTracking()
                .Select(p => new { p.Id, p.InGameName, p.IsGuest, p.AvatarUrl, p.CurTier, p.CurDiv, p.PeakTier, p.PeakDiv })
                .ToListAsync(ct);

            var matchRows = await _db.LeagueMatches.AsNoTracking()
                .Where(m => m.Status == LeagueMatchStatus.Done)
                .Select(m => new
                {
                    m.Id, m.Kind, m.ModeKey, m.ClashEventId, m.WinnerTeamIndex, m.PlayedUtc, m.FinishedUtc, m.DurationSeconds,
                    m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId,
                })
                .ToListAsync(ct);

            var playerRows = await _db.LeagueMatchPlayers.AsNoTracking()
                .Where(p => p.Team!.Match!.Status == LeagueMatchStatus.Done)
                .Select(p => new
                {
                    p.Id, MatchId = p.Team!.MatchId, TeamIndex = p.Team.Index, p.ProfileId, p.Lane, p.PlayedLane, p.ChampionId,
                    p.NameSnapshot, p.WasGuest, p.CurRankSnapshot, p.CurDivSnapshot,
                })
                .ToListAsync(ct);

            var events = await _db.LeagueClashEvents.AsNoTracking()
                .Where(e => e.Status == LeagueClashStatus.Done)
                .Select(e => new { e.Id, e.Title, e.StartsAtUtc, e.EndedUtc })
                .ToListAsync(ct);

            var championNames = await _db.LeagueChampions.AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

            // ---- plain rows for the pure rules
            var done = matchRows.Select(m => new OvMatch(m.Id, m.Kind, m.ModeKey, m.ClashEventId, m.WinnerTeamIndex,
                m.PlayedUtc, m.DurationSeconds)).ToList();
            var rows = playerRows.Select(r => new OvRow(r.MatchId, r.TeamIndex, r.ProfileId, r.PlayedLane ?? r.Lane, r.ChampionId)).ToList();
            var doneEvents = events.Select(e => new OvClashEvent(e.Id, e.Title, e.StartsAtUtc, e.EndedUtc)).ToList();
            var names = profiles.ToDictionary(p => p.Id, p => p.InGameName);
            var byId = profiles.ToDictionary(p => p.Id);

            // ---- ranking and MVPs. MVP redesign Step 5b: the MVP number is the CUSTOM DRAFT count (Clash and custom ARAM/Arena
            // have their own numbers, later steps). The MVP columns are only ever set when the stats exist.
            var mvpCounts = LeagueOverviewStats.DraftMvpCounts(
                matchRows.Select(m => (m.Kind, m.ModeKey, new MvpPicks(m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId))));
            var inputs = profiles.Select(p => new RankInput(p.Id, p.InGameName, p.CurTier, p.CurDiv, p.PeakTier, p.PeakDiv,
                mvpCounts.GetValueOrDefault(p.Id))).ToList();

            var holders = LeagueBadges.Holders(inputs, await LeagueBadges.LoadClashHoldersAsync(_db, ct));
            LeagueOverviewPlayer ToCard(RankInput r, int position)
            {
                var p = byId[r.Id];
                return new LeagueOverviewPlayer
                {
                    Id = p.Id, Name = p.InGameName, IsGuest = p.IsGuest, AvatarUrl = p.AvatarUrl,
                    CurTier = p.CurTier, CurDiv = p.CurDiv, PeakTier = p.PeakTier, PeakDiv = p.PeakDiv,
                    MvpCount = r.MvpCount, Position = position,
                    RankKing = holders.IsRankKing(p.Id), MvpKing = holders.IsMvpKing(p.Id), ClashKing = holders.IsClashKing(p.Id),
                };
            }

            var model = new LeagueOverviewModel
            {
                TopRanked = LeagueRanking.TopThree(inputs).Select(e => ToCard(e.Player, e.Position)).ToList(),
                TopMvps = LeagueRanking.TopMvps(inputs).Select((r, i) => ToCard(r, i + 1)).ToList(),
            };

            // ---- best Clash team
            var best = LeagueOverviewStats.BestClash(doneEvents, done);
            if (best != null)
            {
                var team = playerRows.Where(r => r.MatchId == best.GameMatchId && r.TeamIndex == 0)
                    .OrderBy(r => r.Lane == null ? 99 : (int)r.Lane.Value).ThenBy(r => r.Id).ToList();
                model.BestClash = new LeagueOverviewClash
                {
                    EventId = best.Event.Id, Title = best.Event.Title, StartsAtUtc = best.Event.StartsAtUtc,
                    Wins = best.Wins, Games = best.Games,
                    Players = team.Select(r =>
                    {
                        // A live profile wins over the snapshot (name, picture, rank); a deleted profile keeps the snapshot.
                        byId.TryGetValue(r.ProfileId ?? 0, out var live);
                        return new LeagueOverviewClashPlayer
                        {
                            ProfileId = live?.Id,
                            Name = live?.InGameName ?? r.NameSnapshot,
                            IsGuest = live?.IsGuest ?? r.WasGuest,
                            AvatarUrl = live?.AvatarUrl,
                            Lane = r.Lane,
                            Tier = live?.CurTier ?? r.CurRankSnapshot,
                            Div = live != null ? live.CurDiv : r.CurDivSnapshot,
                        };
                    }).ToList(),
                };
            }

            // ---- server stats
            var counts = LeagueOverviewStats.GameCounts(done);
            var records = LeagueOverviewStats.ClashRecords(done);
            var stats = model.Stats;
            stats.Customs = counts.Customs; stats.Draft = counts.Draft; stats.Aram = counts.Aram; stats.Arena = counts.Arena;
            stats.ClashGames = counts.ClashGames;
            var sides = LeagueOverviewStats.SideWins(done);
            stats.SideAWins = sides.SideA; stats.SideBWins = sides.SideB;
            stats.Clashes = doneEvents.Count;
            stats.ClashesWon = LeagueOverviewStats.ClashesWon(doneEvents, records);
            stats.Players = profiles.Count;
            stats.Guests = profiles.Count(p => p.IsGuest);
            stats.Members = stats.Players - stats.Guests;

            if (LeagueOverviewStats.TopChampion(rows, championNames) is { } champ)
            {
                stats.TopChampion = championNames.TryGetValue(champ.ChampionId, out var cn) ? cn : null;
                stats.TopChampionGames = champ.Games;
            }
            if (LeagueOverviewStats.TopLane(rows) is { } lane)
            {
                stats.TopLane = lane.Lane;
                stats.TopLanePercent = lane.Percent;
            }
            if (LeagueOverviewStats.LongestWinStreak(done, rows, names) is { } streak)
                stats.Streak = new LeagueOverviewPersonFact { ProfileId = streak.ProfileId, Name = names[streak.ProfileId], Count = streak.Streak };
            if (LeagueOverviewStats.MostActive(rows, names) is { } active)
                stats.MostActive = new LeagueOverviewPersonFact { ProfileId = active.ProfileId, Name = names[active.ProfileId], Count = active.Games };
            if (LeagueOverviewStats.AverageLengthSeconds(done) is int avg)
            {
                stats.AverageLengthSeconds = avg;
                stats.AverageLengthText = LeagueOverviewStats.FormatDuration(avg);
            }

            return model;
        }

        // Step 8d: every player (members and guests) for the Leaderboard page. Position comes from the FULL ranking
        // (LeagueRanking), then the query filters and sorts (LeagueLeaderboardRules). MVPs are custom matches only,
        // through the same LeagueOverviewStats.DraftMvpCounts the Overview uses (custom draft only). Read only.
        public async Task<LeagueLeaderboardModel> GetLeaderboardAsync(LeagueLeaderboardQuery query, CancellationToken ct = default)
        {
            var profiles = await _db.LeagueProfiles.AsNoTracking()
                .Select(p => new { p.Id, p.InGameName, p.IsGuest, p.AvatarUrl, p.CurTier, p.CurDiv, p.PeakTier, p.PeakDiv, p.MainLane })
                .ToListAsync(ct);

            var laneRows = await _db.LeagueProfileLanes.AsNoTracking()
                .Select(l => new { l.ProfileId, l.Lane, l.Level, l.Priority })
                .ToListAsync(ct);

            var matchRows = await _db.LeagueMatches.AsNoTracking()
                .Where(m => m.Status == LeagueMatchStatus.Done)
                .Select(m => new
                {
                    m.Id, m.Kind, m.ModeKey, m.ClashEventId, m.WinnerTeamIndex, m.PlayedUtc, m.FinishedUtc, m.DurationSeconds,
                    m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId,
                })
                .ToListAsync(ct);

            var playerRows = await _db.LeagueMatchPlayers.AsNoTracking()
                .Where(p => p.Team!.Match!.Status == LeagueMatchStatus.Done)
                .Select(p => new { MatchId = p.Team!.MatchId, TeamIndex = p.Team.Index, p.ProfileId })
                .ToListAsync(ct);

            // Leaderboard step L1: the average match score. Only finished custom Normal Draft matches (the same group as Games),
            // so the stat columns of those players are read. Worked out live; LeagueLeaderboardRules.AverageScores does the maths.
            var draftIds = matchRows.Where(m => LeagueMvpBuckets.BucketOf(m.Kind, m.ModeKey) == LeagueMatchBucket.CustomDraft)
                .Select(m => m.Id).ToList();
            var scoreRows = await _db.LeagueMatchPlayers.AsNoTracking()
                .Where(p => draftIds.Contains(p.Team!.MatchId))
                .Select(p => new
                {
                    MatchId = p.Team!.MatchId, TeamIndex = p.Team.Index, p.ProfileId, p.Lane, p.PlayedLane,
                    p.Kills, p.Deaths, p.Assists, p.Cs, p.DamageDealt, p.DamageTaken, p.Gold, p.VisionScore, p.ObjectiveDamage,
                })
                .ToListAsync(ct);
            var avgScores = LeagueLeaderboardRules.AverageScores(
                scoreRows.GroupBy(r => r.MatchId).Select(g => (IReadOnlyList<MvpPlayer>)g.Select(r => new MvpPlayer(
                    r.ProfileId, r.TeamIndex, r.PlayedLane ?? r.Lane,
                    r.Kills, r.Deaths, r.Assists, r.Cs, r.DamageDealt, r.DamageTaken, r.Gold, r.VisionScore, r.ObjectiveDamage)).ToList()));

            var done = matchRows.Select(m => new OvMatch(m.Id, m.Kind, m.ModeKey, m.ClashEventId, m.WinnerTeamIndex,
                m.PlayedUtc, m.DurationSeconds)).ToList();
            var rows = playerRows.Select(r => new OvRow(r.MatchId, r.TeamIndex, r.ProfileId, null, null)).ToList();
            // MVP redesign Step 5c: Games / Wins / Losses = custom DRAFT only; Clash MVPs are their own column.
            var bucketRecords = LeagueMvpBuckets.Records(done, rows);
            var bucketMvps = LeagueMvpBuckets.MvpCounts(
                matchRows.Select(m => (m.Kind, m.ModeKey, new MvpPicks(m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId))));

            var mvpCounts = LeagueOverviewStats.DraftMvpCounts(
                matchRows.Select(m => (m.Kind, m.ModeKey, new MvpPicks(m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId))));

            var inputs = profiles.Select(p => new RankInput(p.Id, p.InGameName, p.CurTier, p.CurDiv, p.PeakTier, p.PeakDiv,
                mvpCounts.GetValueOrDefault(p.Id))).ToList();
            var byId = profiles.ToDictionary(p => p.Id);
            var lanesByProfile = laneRows.GroupBy(l => l.ProfileId)
                .ToDictionary(g => g.Key, g => g.Select(l => new LaneLevel(l.Lane, l.Level, l.Priority)).ToList());

            var holders = LeagueBadges.Holders(inputs, await LeagueBadges.LoadClashHoldersAsync(_db, ct));
            var all = LeagueRanking.Rank(inputs).Select(e =>
            {
                var p = byId[e.Player.Id];
                var rec = bucketRecords.GetValueOrDefault(p.Id)?.CustomDraft ?? new BucketRecord(0, 0, 0);
                return new LeagueLeaderboardRow
                {
                    Id = p.Id, Name = p.InGameName, IsGuest = p.IsGuest, AvatarUrl = p.AvatarUrl,
                    CurTier = p.CurTier, CurDiv = p.CurDiv, PeakTier = p.PeakTier, PeakDiv = p.PeakDiv,
                    Position = e.Position, Mvps = e.Player.MvpCount, ClashMvps = bucketMvps.GetValueOrDefault(p.Id)?.Clash ?? 0,
                    RankKing = holders.IsRankKing(p.Id), MvpKing = holders.IsMvpKing(p.Id), ClashKing = holders.IsClashKing(p.Id),
                    Games = rec.Games, Wins = rec.Wins, Losses = rec.Losses,
                    AvgScore = avgScores.GetValueOrDefault(p.Id)?.Average,
                    ScoredGames = avgScores.GetValueOrDefault(p.Id)?.ScoredGames ?? 0,
                    Mains = LeagueLeaderboardRules.MainLanes(lanesByProfile.GetValueOrDefault(p.Id) ?? new List<LaneLevel>(), p.MainLane),
                };
            }).ToList();

            return new LeagueLeaderboardModel
            {
                Query = query,
                TotalPlayers = all.Count,
                Rows = LeagueLeaderboardRules.Apply(all, query),
            };
        }

        // Step 8e: one player (member or guest) for the Player page. Place = the position in the FULL ranking (the same
        // LeagueRanking as the Leaderboard); MVPs = custom draft matches only, through LeagueOverviewStats.DraftMvpCounts;
        // games and win rate = custom DRAFT only, LeagueMvpBuckets.Records (the same numbers as the Leaderboard; Clash and ARAM/Arena
        // numbers come with Stats.Buckets, MVP redesign Step 5d). The Step 5d numbers
        // (K/D/A, champions, lanes, last results) are not loaded here: the controller adds them with
        // LeagueHistoryService.GetPlayerStatsAsync. Null when the id is unknown. Read only.
        public async Task<LeaguePlayerModel?> GetPlayerAsync(int id, CancellationToken ct = default)
        {
            var profiles = await _db.LeagueProfiles.AsNoTracking()
                .Select(p => new { p.Id, p.InGameName, p.IsGuest, p.AvatarUrl, p.CurTier, p.CurDiv, p.PeakTier, p.PeakDiv })
                .ToListAsync(ct);
            var me = profiles.FirstOrDefault(p => p.Id == id);
            if (me is null) return null;

            var laneRows = await _db.LeagueProfileLanes.AsNoTracking()
                .Where(l => l.ProfileId == id)
                .Select(l => new { l.Lane, l.Level, l.Priority })
                .ToListAsync(ct);

            var matchRows = await _db.LeagueMatches.AsNoTracking()
                .Where(m => m.Status == LeagueMatchStatus.Done)
                .Select(m => new
                {
                    m.Id, m.Kind, m.ModeKey, m.ClashEventId, m.WinnerTeamIndex, m.PlayedUtc, m.FinishedUtc, m.DurationSeconds,
                    m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId,
                })
                .ToListAsync(ct);

            var playerRows = await _db.LeagueMatchPlayers.AsNoTracking()
                .Where(p => p.ProfileId == id && p.Team!.Match!.Status == LeagueMatchStatus.Done)
                .Select(p => new { MatchId = p.Team!.MatchId, TeamIndex = p.Team.Index, p.ProfileId })
                .ToListAsync(ct);

            var done = matchRows.Select(m => new OvMatch(m.Id, m.Kind, m.ModeKey, m.ClashEventId, m.WinnerTeamIndex,
                m.PlayedUtc, m.DurationSeconds)).ToList();
            var rows = playerRows.Select(r => new OvRow(r.MatchId, r.TeamIndex, r.ProfileId, null, null)).ToList();
            var rec = LeagueMvpBuckets.Records(done, rows).GetValueOrDefault(id)?.CustomDraft ?? new BucketRecord(0, 0, 0);

            var mvpCounts = LeagueOverviewStats.DraftMvpCounts(
                matchRows.Select(m => (m.Kind, m.ModeKey, new MvpPicks(m.MvpProfileId, m.TeamMvpProfileId, m.StatsMvpProfileId, m.StatsTeamMvpProfileId))));
            var inputs = profiles.Select(p => new RankInput(p.Id, p.InGameName, p.CurTier, p.CurDiv, p.PeakTier, p.PeakDiv,
                mvpCounts.GetValueOrDefault(p.Id))).ToList();
            var place = LeaguePlayerRules.PlaceOf(inputs, id);
            var holders = LeagueBadges.Holders(inputs, await LeagueBadges.LoadClashHoldersAsync(_db, ct));

            return new LeaguePlayerModel
            {
                Id = me.Id, Name = me.InGameName, IsGuest = me.IsGuest, AvatarUrl = me.AvatarUrl,
                CurTier = me.CurTier, CurDiv = me.CurDiv, PeakTier = me.PeakTier, PeakDiv = me.PeakDiv,
                Position = place?.Position ?? 0, TotalPlayers = place?.Total ?? profiles.Count,
                Mvps = mvpCounts.GetValueOrDefault(id),
                RankKing = holders.IsRankKing(id), MvpKing = holders.IsMvpKing(id), ClashKing = holders.IsClashKing(id),
                Games = rec.Games, Wins = rec.Wins, Losses = rec.Losses,
                Pyramid = LeagueLaneLevels.Pyramid(laneRows.Select(l => new LaneLevel(l.Lane, l.Level, l.Priority))),
            };
        }
    }
}
