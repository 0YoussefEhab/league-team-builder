using LeagueTeamBuilder.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // A7: the champion catalog on a real in-memory SQLite database.
    public class ChampionCatalogServiceTests : IDisposable
    {
        private readonly SqliteTestDb _testDb = new();
        private readonly ApplicationDbContext _db;
        private readonly ChampionCatalogService _svc;

        public ChampionCatalogServiceTests()
        {
            _db = _testDb.NewContext();
            _svc = new ChampionCatalogService(_db);
        }

        public void Dispose()
        {
            _db.Dispose();
            _testDb.Dispose();
        }

        private async Task<List<string>> NamesAsync()
        {
            await using var db = _testDb.NewContext();
            return await db.LeagueChampions.AsNoTracking().OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        }

        [Fact]
        public async Task Add_trims_the_name_and_saves_an_active_champion()
        {
            var (ok, _) = await _svc.AddAsync("  Ahri  ");
            Assert.True(ok);
            Assert.Equal(new[] { "Ahri" }, await NamesAsync());
            Assert.Single(await _svc.GetActiveAsync());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Add_refuses_a_blank_name(string? name)
        {
            var (ok, _) = await _svc.AddAsync(name);
            Assert.False(ok);
            Assert.Empty(await NamesAsync());
        }

        [Fact]
        public async Task Add_refuses_a_name_longer_than_the_limit_and_accepts_one_exactly_at_it()
        {
            Assert.False((await _svc.AddAsync(new string('a', ChampionCatalogService.MaxNameLength + 1))).Ok);
            Assert.True((await _svc.AddAsync(new string('b', ChampionCatalogService.MaxNameLength))).Ok);
            Assert.Single(await NamesAsync());
        }

        [Fact]
        public async Task Add_refuses_a_name_already_in_the_catalog_whatever_its_case()
        {
            await _svc.AddAsync("Ahri");
            var (ok, message) = await _svc.AddAsync("AHRI");
            Assert.False(ok);
            Assert.Contains("already", message);
            Assert.Single(await NamesAsync());
        }

        [Fact]
        public async Task BulkAdd_adds_the_new_names_once_and_counts_the_rest_as_skipped()
        {
            await _svc.AddAsync("Ahri");

            var (added, skipped) = await _svc.BulkAddAsync("Ahri\r\nZed\nzed\n\nLux\n");

            Assert.Equal(2, added);        // Zed and Lux
            Assert.Equal(2, skipped);      // Ahri (already there) and the second zed
            Assert.Equal(new[] { "Ahri", "Lux", "Zed" }, await NamesAsync());
        }

        [Fact]
        public async Task Rename_refuses_a_name_another_champion_has_but_allows_a_change_of_case_on_itself()
        {
            await _svc.AddAsync("Ahri");
            await _svc.AddAsync("Zed");
            int ahriId;
            await using (var db = _testDb.NewContext())
                ahriId = (await db.LeagueChampions.SingleAsync(c => c.Name == "Ahri")).Id;

            Assert.False((await _svc.RenameAsync(ahriId, "zed")).Ok);
            Assert.True((await _svc.RenameAsync(ahriId, "AHRI")).Ok);
            Assert.False((await _svc.RenameAsync(9999, "Lux")).Ok);
            Assert.Equal(new[] { "AHRI", "Zed" }, await NamesAsync());
        }

        [Fact]
        public async Task A_disabled_champion_is_hidden_from_the_active_list_and_can_be_enabled_again()
        {
            await _svc.AddAsync("Ahri");
            int id;
            await using (var db = _testDb.NewContext())
                id = (await db.LeagueChampions.SingleAsync()).Id;

            Assert.True((await _svc.SetActiveAsync(id, false)).Ok);
            Assert.Empty(await _svc.GetActiveAsync());
            Assert.True((await _svc.SetActiveAsync(id, true)).Ok);
            Assert.Single(await _svc.GetActiveAsync());
            Assert.False((await _svc.SetActiveAsync(9999, true)).Ok);
        }

        [Fact]
        public async Task Remove_deletes_a_champion_that_is_used_nowhere()
        {
            await _svc.AddAsync("Ahri");
            int id;
            await using (var db = _testDb.NewContext())
                id = (await db.LeagueChampions.SingleAsync()).Id;

            var (ok, message) = await _svc.RemoveAsync(id);

            Assert.True(ok);
            Assert.Contains("deleted", message);
            Assert.Empty(await NamesAsync());
            Assert.False((await _svc.RemoveAsync(id)).Ok);   // already gone
        }

        [Fact]
        public async Task GetAllWithUsage_searches_by_part_of_the_name_ignoring_case()
        {
            await _svc.BulkAddAsync("Ahri\nAhsoka\nZed");

            var rows = await _svc.GetAllWithUsageAsync("AH");

            Assert.Equal(new[] { "Ahri", "Ahsoka" }, rows.Select(r => r.Champion.Name).ToArray());
            Assert.All(rows, r => Assert.Equal(0, r.UsageCount));
            Assert.Equal(3, (await _svc.GetAllWithUsageAsync(null)).Count);
        }
    }
}
