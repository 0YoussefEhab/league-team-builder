using LeagueTeamBuilder.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

// Root namespace on purpose: the copied League services only import LeagueTeamBuilder.Models, and HFECloud kept the context in its root namespace.
namespace LeagueTeamBuilder
{
    // Identity tables + the 15 League tables + the data-protection key table. SQLite only (decision 5, revised in A3).
    // Delete rules matter here: the League code calls ExecuteDelete / ExecuteUpdate, which do NOT run EF's in-memory
    // cascade, so every cascade and SET NULL below is enforced by the database itself.
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<LeagueProfile> LeagueProfiles => Set<LeagueProfile>();
        public DbSet<LeagueProfileLane> LeagueProfileLanes => Set<LeagueProfileLane>();
        public DbSet<LeagueProfileChamp> LeagueProfileChamps => Set<LeagueProfileChamp>();
        public DbSet<LeagueChampion> LeagueChampions => Set<LeagueChampion>();
        public DbSet<LeagueCustomTier> LeagueCustomTiers => Set<LeagueCustomTier>();
        public DbSet<LeagueModeSettings> LeagueModeSettings => Set<LeagueModeSettings>();
        public DbSet<LeagueMapSpot> LeagueMapSpots => Set<LeagueMapSpot>();
        public DbSet<LeagueMatch> LeagueMatches => Set<LeagueMatch>();
        public DbSet<LeagueMatchTeam> LeagueMatchTeams => Set<LeagueMatchTeam>();
        public DbSet<LeagueMatchPlayer> LeagueMatchPlayers => Set<LeagueMatchPlayer>();
        public DbSet<LeagueClashEvent> LeagueClashEvents => Set<LeagueClashEvent>();
        public DbSet<LeagueClashTeam> LeagueClashTeams => Set<LeagueClashTeam>();
        public DbSet<LeagueClashResponse> LeagueClashResponses => Set<LeagueClashResponse>();
        public DbSet<LeagueAutoSendSettings> LeagueAutoSendSettings => Set<LeagueAutoSendSettings>();
        public DbSet<LeagueGuestLinkLog> LeagueGuestLinkLogs => Set<LeagueGuestLinkLog>();

        // Seams that replace HFECloud's NotificationService and AdminActivityLogger (step A4).
        public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
        public DbSet<ActivityLogEntry> ActivityLogEntries => Set<ActivityLogEntry>();

        // Sign-in cookies stay valid across restarts and redeploys (keys live in the database, see Program.cs).
        public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

        // SQLite has no date type: EF writes DateTime as text and reads it back with Kind = Unspecified. The League code
        // and its JSON/JS expect UTC ("...Z"), so every DateTime is read back as UTC. Without this the browser would
        // show every time shifted by the viewer's UTC offset.
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        }

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            // ---------------------------------------------------------------- profiles
            b.Entity<LeagueProfile>(e =>
            {
                // One league profile per user (guests have a null UserId; SQLite allows many NULLs in a unique index).
                e.HasIndex(p => p.UserId).IsUnique();
                e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(p => p.DoBetterWith).WithMany().HasForeignKey(p => p.DoBetterWithId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(p => p.CustomTier).WithMany().HasForeignKey(p => p.CustomTierId).OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<LeagueProfileLane>(e =>
            {
                e.HasKey(l => new { l.ProfileId, l.Lane });
                e.HasOne(l => l.Profile).WithMany(p => p.Lanes).HasForeignKey(l => l.ProfileId).OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<LeagueProfileChamp>(e =>
            {
                e.HasKey(c => new { c.ProfileId, c.Lane, c.ChampionId });
                e.HasOne(c => c.Profile).WithMany(p => p.Champs).HasForeignKey(c => c.ProfileId).OnDelete(DeleteBehavior.Cascade);
                // Champions in use are disabled, never deleted, so a delete that would orphan a row should fail loudly.
                e.HasOne(c => c.Champion).WithMany().HasForeignKey(c => c.ChampionId).OnDelete(DeleteBehavior.Restrict);
            });

            // ---------------------------------------------------------------- per-mode settings
            b.Entity<LeagueModeSettings>(e =>
            {
                e.HasKey(s => s.Mode);
                e.Property(s => s.Mode).ValueGeneratedNever();
            });

            b.Entity<LeagueMapSpot>(e =>
            {
                e.HasKey(s => new { s.Mode, s.Team, s.Slot });
                e.HasOne(s => s.Settings).WithMany(m => m.Spots).HasForeignKey(s => s.Mode).OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<LeagueAutoSendSettings>(e =>
            {
                e.HasKey(s => s.Id);
                e.Property(s => s.Id).ValueGeneratedNever(); // the single row has Id = 1
            });

            // ---------------------------------------------------------------- clash
            b.Entity<LeagueClashEvent>(e =>
            {
                e.HasMany(c => c.Teams).WithOne(t => t.Event).HasForeignKey(t => t.EventId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(c => c.Responses).WithOne(r => r.Event).HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Cascade);
                // Games of a clash are history: deleting an event that still has games must fail, not wipe them.
                e.HasMany(c => c.Matches).WithOne(m => m.ClashEvent).HasForeignKey(m => m.ClashEventId).OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(c => c.StartsAtUtc);
            });

            b.Entity<LeagueClashTeam>(e =>
            {
                // "Name is unique per clash" (model comment). The service catches the DbUpdateException this raises on a race.
                e.HasIndex(t => new { t.EventId, t.Name }).IsUnique();
                e.HasMany(t => t.Matches).WithOne(m => m.ClashTeam).HasForeignKey(m => m.ClashTeamId).OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<LeagueClashResponse>(e =>
            {
                // "Unique per (event, profile)".
                e.HasIndex(r => new { r.EventId, r.ProfileId }).IsUnique();
                e.HasOne(r => r.Profile).WithMany().HasForeignKey(r => r.ProfileId).OnDelete(DeleteBehavior.Cascade);
            });

            // ---------------------------------------------------------------- matches (snapshots; see the model comment)
            b.Entity<LeagueMatch>(e =>
            {
                e.HasIndex(m => m.PlayedUtc);
                e.HasIndex(m => new { m.ClashEventId, m.GameNumber });
                // Four live links to the MVP profiles: SET NULL when the profile is deleted or merged away.
                e.HasOne<LeagueProfile>().WithMany().HasForeignKey(m => m.MvpProfileId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne<LeagueProfile>().WithMany().HasForeignKey(m => m.TeamMvpProfileId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne<LeagueProfile>().WithMany().HasForeignKey(m => m.StatsMvpProfileId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne<LeagueProfile>().WithMany().HasForeignKey(m => m.StatsTeamMvpProfileId).OnDelete(DeleteBehavior.SetNull);
                e.HasMany(m => m.Teams).WithOne(t => t.Match).HasForeignKey(t => t.MatchId).OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<LeagueMatchTeam>(e =>
            {
                e.HasIndex(t => new { t.MatchId, t.Index });
                e.HasMany(t => t.Players).WithOne(p => p.Team).HasForeignKey(p => p.TeamId).OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<LeagueMatchPlayer>(e =>
            {
                // The snapshot columns stay when the profile goes; only the live link is cleared.
                e.HasOne(p => p.Profile).WithMany().HasForeignKey(p => p.ProfileId).OnDelete(DeleteBehavior.SetNull);
                e.HasOne(p => p.Champion).WithMany().HasForeignKey(p => p.ChampionId).OnDelete(DeleteBehavior.Restrict);
            });

            // ---------------------------------------------------------------- notifications and audit trail
            b.Entity<UserNotification>(e =>
            {
                e.Property(n => n.Message).HasMaxLength(UserNotification.MaxMessage);
                e.Property(n => n.Link).HasMaxLength(UserNotification.MaxLink);
                e.HasIndex(n => new { n.UserId, n.ReadUtc });
                // Accounts are deactivated, never deleted; if one is ever removed its notifications go with it.
                e.HasOne<ApplicationUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<ActivityLogEntry>(e =>
            {
                e.Property(a => a.Action).HasMaxLength(ActivityLogEntry.MaxAction);
                e.Property(a => a.Target).HasMaxLength(ActivityLogEntry.MaxTarget);
                e.HasIndex(a => a.CreatedUtc);
                // No foreign key on purpose: the trail must outlive the account that wrote it.
            });

            // LeagueGuestLinkLog has no foreign keys on purpose (the user or profile may be deleted later; the log must survive).
        }
    }

    // DateTime <-> text in SQLite. Unspecified is treated as UTC, a Local value is converted, a Utc value is kept.
    public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    public class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter()
            : base(
                v => v.HasValue ? (v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
        {
        }
    }
}
