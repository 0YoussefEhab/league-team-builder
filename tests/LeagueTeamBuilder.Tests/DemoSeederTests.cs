using LeagueTeamBuilder.Data;
using LeagueTeamBuilder.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // D1: the demo seed. Real in-memory SQLite, the real champion list, a fixed clock.
    public class DemoSeederTests
    {
        private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

        private static async Task<SqliteTestDb> NewDbAsync()
        {
            var db = new SqliteTestDb();
            using var ctx = db.NewContext();
            await LeagueChampionSeeder.SeedAsync(ctx);
            return db;
        }

        private static async Task<bool> SeedAsync(SqliteTestDb db, bool reset = false)
        {
            using var ctx = db.NewContext();
            return await DemoSeeder.SeedAsync(ctx, reset, NullLogger.Instance, Now);
        }

        [Fact]
        public async Task It_adds_sixteen_guests_with_lanes_and_champions()
        {
            using var db = await NewDbAsync();
            Assert.True(await SeedAsync(db));

            using var ctx = db.NewContext();
            var players = await ctx.LeagueProfiles.Include(p => p.Lanes).Include(p => p.Champs).ToListAsync();
            Assert.Equal(16, players.Count);
            Assert.All(players, p =>
            {
                Assert.True(p.IsGuest);
                Assert.Null(p.UserId);
                Assert.EndsWith(DemoSeeder.RiotSuffix, p.RiotId);
                Assert.NotEmpty(p.Lanes);
                Assert.NotEmpty(p.Champs);
                Assert.Contains(p.Lanes, l => l.Lane == p.MainLane);
            });
            Assert.Equal(16, players.Select(p => p.InGameName).Distinct().Count());
        }

        [Fact]
        public async Task Master_and_above_have_no_division_and_the_peak_is_never_below_the_current_rank()
        {
            using var db = await NewDbAsync();
            await SeedAsync(db);
            using var ctx = db.NewContext();
            foreach (var p in await ctx.LeagueProfiles.ToListAsync())
            {
                Assert.True(p.PeakTier >= p.CurTier, p.InGameName);
                if (p.CurTier >= LeagueRank.Master) Assert.Null(p.CurDiv); else Assert.NotNull(p.CurDiv);
                if (p.PeakTier >= LeagueRank.Master) Assert.Null(p.PeakDiv); else Assert.NotNull(p.PeakDiv);
            }
        }

        [Fact]
        public async Task It_adds_fourteen_finished_games_a_waiting_game_and_three_clash_games()
        {
            using var db = await NewDbAsync();
            await SeedAsync(db);
            using var ctx = db.NewContext();
            var matches = await ctx.LeagueMatches.Include(m => m.Teams).ThenInclude(t => t.Players).ToListAsync();

            var custom = matches.Where(m => m.Kind == LeagueMatchKind.Custom).ToList();
            Assert.Equal(15, custom.Count);
            Assert.Equal(14, custom.Count(m => m.Status == LeagueMatchStatus.Done));
            Assert.Single(custom, m => m.Status == LeagueMatchStatus.Submitted);
            Assert.Equal(4, custom.Count(m => m.ModeKey == "aram"));

            var clash = matches.Where(m => m.Kind == LeagueMatchKind.Clash).OrderBy(m => m.GameNumber).ToList();
            Assert.Equal(new int?[] { 1, 2, 3 }, clash.Select(m => m.GameNumber).ToArray());
            Assert.Equal(new int?[] { 0, 0, 1 }, clash.Select(m => m.WinnerTeamIndex).ToArray());
            Assert.All(clash, m => Assert.Empty(m.Teams.Single(t => t.Index == 1).Players));
        }

        [Fact]
        public async Task Nobody_is_on_a_match_twice_and_every_team_has_five_players()
        {
            using var db = await NewDbAsync();
            await SeedAsync(db);
            using var ctx = db.NewContext();
            var matches = await ctx.LeagueMatches.Include(m => m.Teams).ThenInclude(t => t.Players).ToListAsync();
            foreach (var m in matches)
            {
                var ids = m.Teams.SelectMany(t => t.Players).Select(p => p.ProfileId).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
                foreach (var t in m.Teams.Where(t => t.Players.Count > 0)) Assert.Equal(5, t.Players.Count);
            }
        }

        [Fact]
        public async Task Draft_games_give_each_team_one_player_per_lane()
        {
            using var db = await NewDbAsync();
            await SeedAsync(db);
            using var ctx = db.NewContext();
            var drafts = await ctx.LeagueMatches.Include(m => m.Teams).ThenInclude(t => t.Players)
                .Where(m => m.ModeKey == "normal").ToListAsync();
            Assert.NotEmpty(drafts);
            foreach (var t in drafts.SelectMany(m => m.Teams).Where(t => t.Players.Count > 0))
                Assert.Equal(new LeagueLane?[] { LeagueLane.Top, LeagueLane.Jungle, LeagueLane.Mid, LeagueLane.Adc, LeagueLane.Support },
                    t.Players.OrderBy(p => p.Lane).Select(p => p.Lane).ToArray());
        }

        [Fact]
        public async Task Finished_games_with_stats_have_their_MVPs_and_the_newest_one_has_no_stats()
        {
            using var db = await NewDbAsync();
            await SeedAsync(db);
            using var ctx = db.NewContext();
            var done = await ctx.LeagueMatches.Include(m => m.Teams).ThenInclude(t => t.Players)
                .Where(m => m.Kind == LeagueMatchKind.Custom && m.Status == LeagueMatchStatus.Done)
                .OrderBy(m => m.PlayedUtc).ToListAsync();
            Assert.Equal(14, done.Count);

            var newest = done[^1];
            Assert.All(newest.Teams.SelectMany(t => t.Players), p => Assert.Null(p.Kills));
            Assert.Null(newest.MvpProfileId);

            foreach (var m in done.Take(13))
            {
                Assert.All(m.Teams.SelectMany(t => t.Players), p => { Assert.NotNull(p.Kills); Assert.NotNull(p.ChampionId); });
                Assert.NotNull(m.MvpProfileId);
                var winners = m.Teams.Single(t => t.Index == m.WinnerTeamIndex).Players.Select(p => p.ProfileId);
                Assert.Contains(m.MvpProfileId, winners);
                Assert.Equal(m.Teams.Single(t => t.Index == 0).Players.Sum(p => p.Kills), m.TeamKills0);
            }
        }

        [Fact]
        public async Task Games_are_in_the_past_and_in_order_and_the_waiting_game_is_now()
        {
            using var db = await NewDbAsync();
            await SeedAsync(db);
            using var ctx = db.NewContext();
            var done = await ctx.LeagueMatches.Where(m => m.Kind == LeagueMatchKind.Custom && m.Status == LeagueMatchStatus.Done)
                .Select(m => m.PlayedUtc).ToListAsync();
            Assert.All(done, d => Assert.True(d < Now && d > Now.AddDays(-25)));
            var waiting = await ctx.LeagueMatches.SingleAsync(m => m.Status == LeagueMatchStatus.Submitted);
            Assert.Equal(Now, waiting.PlayedUtc);
            Assert.Null(waiting.WinnerTeamIndex);
        }

        [Fact]
        public async Task It_adds_a_finished_clash_and_an_upcoming_one_with_answers()
        {
            using var db = await NewDbAsync();
            await SeedAsync(db);
            using var ctx = db.NewContext();
            var events = await ctx.LeagueClashEvents.Include(e => e.Teams).Include(e => e.Responses).OrderBy(e => e.StartsAtUtc).ToListAsync();
            Assert.Equal(2, events.Count);
            Assert.Equal(LeagueClashStatus.Done, events[0].Status);
            Assert.Equal(LeagueClashStatus.Open, events[1].Status);
            Assert.True(events[1].StartsAtUtc > Now);
            Assert.All(events, e => Assert.Single(e.Teams));
            Assert.Equal(7, events[1].Responses.Count);
            Assert.Equal(3, events[1].Responses.Count(r => r.Status == LeagueClashAnswer.Busy));
            Assert.Equal(3, await ctx.LeagueMatches.CountAsync(m => m.ClashEventId == events[0].Id && m.ClashTeamId == events[0].Teams[0].Id));
        }

        [Fact]
        public async Task Seeding_twice_adds_nothing_the_second_time()
        {
            using var db = await NewDbAsync();
            Assert.True(await SeedAsync(db));
            Assert.False(await SeedAsync(db));
            using var ctx = db.NewContext();
            Assert.Equal(16, await ctx.LeagueProfiles.CountAsync());
            Assert.Equal(18, await ctx.LeagueMatches.CountAsync());
        }

        [Fact]
        public async Task The_same_numbers_come_out_every_time()
        {
            using var a = await NewDbAsync();
            using var b = await NewDbAsync();
            await SeedAsync(a);
            await SeedAsync(b);
            using var ca = a.NewContext();
            using var cb = b.NewContext();
            var ka = await ca.LeagueMatchPlayers.OrderBy(p => p.Id).Select(p => p.Kills + "/" + p.Deaths + "/" + p.Assists).ToListAsync();
            var kb = await cb.LeagueMatchPlayers.OrderBy(p => p.Id).Select(p => p.Kills + "/" + p.Deaths + "/" + p.Assists).ToListAsync();
            Assert.Equal(ka, kb);
        }

        [Fact]
        public async Task Reset_replaces_the_demo_data_and_leaves_everything_else_alone()
        {
            using var db = await NewDbAsync();
            await SeedAsync(db);

            using (var ctx = db.NewContext())
            {
                var real = new LeagueProfile { IsGuest = true, InGameName = "RealPerson", CurTier = LeagueRank.Gold, PeakTier = LeagueRank.Gold, CurDiv = 2, PeakDiv = 2 };
                ctx.LeagueProfiles.Add(real);
                await ctx.SaveChangesAsync();
                ctx.LeagueMatches.Add(new LeagueMatch
                {
                    Kind = LeagueMatchKind.Custom, ModeKey = "normal", ModeName = "Normal Draft", Status = LeagueMatchStatus.Done,
                    PlayedUtc = Now.AddDays(-1), SubmittedUtc = Now.AddDays(-1), BuilderLog = "Balance: Lanes.",
                    Teams = { new LeagueMatchTeam { Index = 0, Name = "A", Players = { new LeagueMatchPlayer { ProfileId = real.Id, NameSnapshot = "RealPerson" } } } },
                });
                ctx.LeagueClashEvents.Add(new LeagueClashEvent { Title = "Real clash", StartsAtUtc = Now.AddDays(5), Mode = LeagueMode.Normal, Status = LeagueClashStatus.Open, CreatedUtc = Now });
                await ctx.SaveChangesAsync();
            }

            Assert.True(await SeedAsync(db, reset: true));

            using var after = db.NewContext();
            Assert.Equal(17, await after.LeagueProfiles.CountAsync());
            Assert.Single(await after.LeagueProfiles.Where(p => p.InGameName == "RealPerson").ToListAsync());
            Assert.Equal(19, await after.LeagueMatches.CountAsync());
            Assert.Equal(1, await after.LeagueMatches.CountAsync(m => m.BuilderLog == "Balance: Lanes."));
            Assert.Equal(3, await after.LeagueClashEvents.CountAsync());
            Assert.Equal(1, await after.LeagueClashEvents.CountAsync(e => e.Title == "Real clash"));
        }

        [Fact]
        public async Task It_does_nothing_when_the_champion_list_is_empty()
        {
            using var db = new SqliteTestDb();
            Assert.False(await SeedAsync(db));
            using var ctx = db.NewContext();
            Assert.Equal(0, await ctx.LeagueProfiles.CountAsync());
        }
    }
}
