using Microsoft.EntityFrameworkCore;

namespace LeagueTeamBuilder.Data
{
    // Database wiring. SQLite only: one migration set, and the same file format for the web and the desktop edition.
    // Config (all optional):
    //   Database:Path           where the file lives. Default App_Data/leagueteambuilder.db. A relative path is resolved against
    //                           the app folder (ContentRootPath), NOT the folder the shell happens to be in.
    //   Database:MigrateOnStart default true. Applies pending migrations when the app starts, so nobody runs dotnet ef by hand.
    public static class DatabaseSetup
    {
        public static IServiceCollection AddLeagueDatabase(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
        {
            var path = ResolveDatabasePath(config, env);
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={path}"));
            return services;
        }

        public static string ResolveDatabasePath(IConfiguration config, IHostEnvironment env)
        {
            var configured = config["Database:Path"];
            var path = string.IsNullOrWhiteSpace(configured) ? Path.Combine("App_Data", "leagueteambuilder.db") : configured.Trim();
            if (!Path.IsPathRooted(path)) path = Path.Combine(env.ContentRootPath, path);
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        // Called once from Program.cs, before the first request.
        public static async Task InitializeLeagueDatabaseAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Database");

            if (app.Configuration.GetValue("Database:MigrateOnStart", true))
            {
                if (!db.Database.GetMigrations().Any())
                    throw new InvalidOperationException(
                        "The project has no EF migrations yet. Run: dotnet ef migrations add InitialCreate (step A6).");
                await db.Database.MigrateAsync();
            }

            // Write-ahead logging: readers do not block the writer. A failure here is harmless (the default mode still works).
            try { await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;"); }
            catch (Exception ex) { log.LogWarning(ex, "Could not switch SQLite to WAL mode; continuing in the default mode."); }

            await IdentitySeeder.SeedAsync(sp, app.Configuration, log);

            // Step D1: fake League data, only when Demo:Seed is true (it never runs by default).
            await DemoSeeder.SeedAsync(db, app.Configuration, log);
        }
    }
}
