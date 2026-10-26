using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // B4: the upload / replace / remove / reset behaviour of LeagueRankBadgeService on a real (in-memory) SQLite database
    // and a temporary web root. The point of most tests: a file is only deleted AFTER the database change is saved.
    public class LeagueRankBadgeServiceTests : IDisposable
    {
        private readonly SqliteConnection _conn;
        private readonly DbContextOptions<ApplicationDbContext> _options;
        private readonly string _webRoot;
        private readonly RankBadgeCatalog _catalog = new();
        private readonly ApplicationDbContext _db;
        private readonly LeagueRankBadgeService _svc;

        public LeagueRankBadgeServiceTests()
        {
            _conn = new SqliteConnection("DataSource=:memory:");
            _conn.Open();
            _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_conn).Options;
            using (var setup = new ApplicationDbContext(_options)) setup.Database.EnsureCreated();

            _webRoot = Path.Combine(Path.GetTempPath(), "ltb-badges-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_webRoot);

            _db = new ApplicationDbContext(_options);
            _svc = new LeagueRankBadgeService(_db, new FakeEnv(_webRoot), _catalog);
        }

        public void Dispose()
        {
            _db.Dispose();
            _conn.Dispose();
            try { Directory.Delete(_webRoot, true); } catch (IOException) { }
        }

        // ---- helpers ----

        private static IFormFile File(byte[] bytes, string name) =>
            new FormFile(new MemoryStream(bytes), 0, bytes.Length, "image", name);

        private string[] FilesOnDisk()
        {
            var dir = Path.Combine(_webRoot, "uploads", "rank-badges");
            return Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>();
        }

        private string DiskPathOf(string url) =>
            Path.Combine(_webRoot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        private async Task<List<LeagueRankBadge>> RowsAsync()
        {
            // A fresh context, so nothing is read from the service's tracked entities.
            using var db = new ApplicationDbContext(_options);
            return await db.LeagueRankBadges.AsNoTracking().OrderBy(r => r.Tier).ToListAsync();
        }

        // ---- upload ----

        [Fact]
        public async Task Upload_saves_the_file_the_row_and_the_catalog()
        {
            var (ok, _) = await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Png(), "gold.png"));

            Assert.True(ok);
            var rows = await RowsAsync();
            var row = Assert.Single(rows);
            Assert.Equal(LeagueRank.Gold, row.Tier);
            Assert.StartsWith("/uploads/rank-badges/", row.ImageUrl);
            Assert.EndsWith(".png", row.ImageUrl);
            Assert.True(System.IO.File.Exists(DiskPathOf(row.ImageUrl!)));
            Assert.Equal(row.ImageUrl, _catalog.Url(LeagueRank.Gold));
            Assert.Equal("/img/ranks/default/iron.svg", _catalog.Url(LeagueRank.Iron));
        }

        [Fact]
        public async Task Replacing_a_badge_deletes_the_old_file_and_keeps_the_new_one()
        {
            await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Png(), "a.png"));
            var oldUrl = _catalog.Url(LeagueRank.Gold);

            var (ok, _) = await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Jpeg(), "b.jpg"));

            Assert.True(ok);
            var newUrl = _catalog.Url(LeagueRank.Gold);
            Assert.NotEqual(oldUrl, newUrl);
            Assert.EndsWith(".jpg", newUrl);
            Assert.False(System.IO.File.Exists(DiskPathOf(oldUrl)));
            Assert.True(System.IO.File.Exists(DiskPathOf(newUrl)));
            Assert.Single(FilesOnDisk());
            Assert.Equal(newUrl, Assert.Single(await RowsAsync()).ImageUrl);
        }

        [Fact]
        public async Task A_failed_database_save_keeps_the_old_badge_and_removes_the_new_file()
        {
            await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Png(), "a.png"));
            var oldUrl = _catalog.Url(LeagueRank.Gold);

            // Make every UPDATE of the table fail, so the second upload cannot be saved.
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "CREATE TRIGGER block_badge_update BEFORE UPDATE ON LeagueRankBadges BEGIN SELECT RAISE(ABORT, 'blocked'); END;";
                cmd.ExecuteNonQuery();
            }

            var (ok, message) = await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Jpeg(), "b.jpg"));

            Assert.False(ok);
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.Equal(oldUrl, _catalog.Url(LeagueRank.Gold));
            Assert.Equal(oldUrl, Assert.Single(await RowsAsync()).ImageUrl);
            var left = Assert.Single(FilesOnDisk());
            Assert.Equal(Path.GetFileName(oldUrl), Path.GetFileName(left));
        }

        [Fact]
        public async Task A_file_that_is_not_a_picture_changes_nothing()
        {
            var text = System.Text.Encoding.UTF8.GetBytes("not a picture at all");

            var (ok, message) = await _svc.SetAsync(LeagueRank.Iron, File(text, "iron.png"));

            Assert.False(ok);
            Assert.Contains("readable image", message);
            Assert.Empty(await RowsAsync());
            Assert.Empty(FilesOnDisk());
            Assert.Equal("/img/ranks/default/iron.svg", _catalog.Url(LeagueRank.Iron));
        }

        [Theory]
        [InlineData("badge.gif")]
        [InlineData("badge.svg")]
        [InlineData("badge.bmp")]
        [InlineData("badge")]
        public async Task A_file_ending_that_is_not_allowed_is_refused_before_anything_is_saved(string fileName)
        {
            var (ok, _) = await _svc.SetAsync(LeagueRank.Iron, File(TestImages.Png(), fileName));

            Assert.False(ok);
            Assert.Empty(await RowsAsync());
            Assert.Empty(FilesOnDisk());
        }

        [Fact]
        public async Task A_file_over_one_megabyte_is_refused()
        {
            var big = new byte[MediaUploadLimits.RankBadgeMaxBytes + 1];

            var (ok, message) = await _svc.SetAsync(LeagueRank.Iron, File(big, "big.png"));

            Assert.False(ok);
            Assert.Contains("too big", message);
            Assert.Empty(await RowsAsync());
            Assert.Empty(FilesOnDisk());
        }

        [Fact]
        public async Task No_file_and_an_unknown_tier_are_refused()
        {
            var (ok1, _) = await _svc.SetAsync(LeagueRank.Iron, null);
            var (ok2, _) = await _svc.SetAsync((LeagueRank)99, File(TestImages.Png(), "x.png"));

            Assert.False(ok1);
            Assert.False(ok2);
            Assert.Empty(await RowsAsync());
            Assert.Empty(FilesOnDisk());
        }

        [Fact]
        public async Task A_picture_with_the_wrong_size_is_refused_and_leaves_no_file()
        {
            var (ok, message) = await _svc.SetAsync(LeagueRank.Iron, File(TestImages.Png(8, 8), "tiny.png"));

            Assert.False(ok);
            Assert.Contains("too small", message);
            Assert.Empty(FilesOnDisk());
        }

        // ---- remove and reset ----

        [Fact]
        public async Task Remove_deletes_the_row_the_file_and_goes_back_to_the_placeholder()
        {
            await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Png(), "a.png"));
            var url = _catalog.Url(LeagueRank.Gold);

            var (ok, _) = await _svc.RemoveAsync(LeagueRank.Gold);

            Assert.True(ok);
            Assert.Empty(await RowsAsync());
            Assert.False(System.IO.File.Exists(DiskPathOf(url)));
            Assert.Equal("/img/ranks/default/gold.svg", _catalog.Url(LeagueRank.Gold));
        }

        [Fact]
        public async Task Remove_on_a_tier_without_an_upload_says_so_and_changes_nothing()
        {
            var (ok, message) = await _svc.RemoveAsync(LeagueRank.Gold);

            Assert.False(ok);
            Assert.Contains("default", message);
        }

        [Fact]
        public async Task Remove_only_touches_the_chosen_tier()
        {
            await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Png(), "g.png"));
            await _svc.SetAsync(LeagueRank.Iron, File(TestImages.Png(), "i.png"));
            var ironUrl = _catalog.Url(LeagueRank.Iron);

            await _svc.RemoveAsync(LeagueRank.Gold);

            Assert.Equal(ironUrl, _catalog.Url(LeagueRank.Iron));
            Assert.True(System.IO.File.Exists(DiskPathOf(ironUrl)));
            Assert.Equal(LeagueRank.Iron, Assert.Single(await RowsAsync()).Tier);
        }

        [Fact]
        public async Task Reset_all_removes_every_row_and_every_file()
        {
            await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Png(), "g.png"));
            await _svc.SetAsync(LeagueRank.Iron, File(TestImages.Webp(), "i.webp"));
            await _svc.SetAsync(LeagueRank.Master, File(TestImages.Jpeg(), "m.jpg"));
            Assert.Equal(3, FilesOnDisk().Length);

            var (ok, _) = await _svc.ResetAllAsync();

            Assert.True(ok);
            Assert.Empty(await RowsAsync());
            Assert.Empty(FilesOnDisk());
            Assert.All(Enum.GetValues<LeagueRank>(), t => Assert.Null(_catalog.UploadedUrl(t)));
        }

        [Fact]
        public async Task Reset_all_with_nothing_uploaded_says_so()
        {
            var (ok, message) = await _svc.ResetAllAsync();

            Assert.False(ok);
            Assert.Contains("default", message);
        }

        // ---- the page's list and the start-up load ----

        [Fact]
        public async Task GetAll_lists_ten_tiers_weakest_first_with_placeholders_for_the_ones_without_an_upload()
        {
            await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Png(), "g.png"));

            var all = await _svc.GetAllAsync();

            Assert.Equal(10, all.Count);
            Assert.Equal(LeagueRank.Iron, all[0].Tier);
            Assert.Equal(LeagueRank.Challenger, all[9].Tier);
            var gold = all.Single(b => b.Tier == LeagueRank.Gold);
            Assert.NotNull(gold.UploadedUrl);
            Assert.Equal(gold.UploadedUrl, gold.Url);
            var iron = all[0];
            Assert.Null(iron.UploadedUrl);
            Assert.Equal("/img/ranks/default/iron.svg", iron.Url);
        }

        [Fact]
        public async Task ReloadCatalog_reads_the_rows_a_second_catalog_does_not_know_yet()
        {
            await _svc.SetAsync(LeagueRank.Gold, File(TestImages.Png(), "g.png"));
            var url = _catalog.Url(LeagueRank.Gold);

            var freshCatalog = new RankBadgeCatalog();
            using var db2 = new ApplicationDbContext(_options);
            var svc2 = new LeagueRankBadgeService(db2, new FakeEnv(_webRoot), freshCatalog);
            Assert.Equal("/img/ranks/default/gold.svg", freshCatalog.Url(LeagueRank.Gold));

            await svc2.ReloadCatalogAsync();

            Assert.Equal(url, freshCatalog.Url(LeagueRank.Gold));
        }

        // ---- a minimal web host environment: only WebRootPath matters here ----

        private sealed class FakeEnv : IWebHostEnvironment
        {
            public FakeEnv(string webRoot) { WebRootPath = webRoot; ContentRootPath = webRoot; }
            public string WebRootPath { get; set; }
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string ApplicationName { get; set; } = "LeagueTeamBuilder.Tests";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
            public string ContentRootPath { get; set; }
            public string EnvironmentName { get; set; } = "Test";
        }
    }
}
