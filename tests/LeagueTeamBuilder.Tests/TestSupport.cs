using LeagueTeamBuilder.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Tests
{
    // A7: shared helpers for the new tests (the B4 badge tests keep their own setup).

    // A real SQLite database that lives in memory for one test. Every NewContext() sees the same data.
    internal sealed class SqliteTestDb : IDisposable
    {
        private readonly SqliteConnection _conn;
        public DbContextOptions<ApplicationDbContext> Options { get; }

        public SqliteTestDb()
        {
            _conn = new SqliteConnection("DataSource=:memory:");
            _conn.Open();
            Options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_conn).Options;
            using var setup = new ApplicationDbContext(Options);
            setup.Database.EnsureCreated();
        }

        public ApplicationDbContext NewContext() => new(Options);

        public void Dispose() => _conn.Dispose();
    }

    // Finds files of the web project from the test's bin folder (walks up until it meets src/LeagueTeamBuilder).
    internal static class WebProject
    {
        public static string? Find(params string[] relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var root = Path.Combine(dir.FullName, "src", "LeagueTeamBuilder");
                if (Directory.Exists(root))
                    return Path.Combine(new[] { root }.Concat(relative).ToArray());
                dir = dir.Parent;
            }
            return null;
        }
    }
}
