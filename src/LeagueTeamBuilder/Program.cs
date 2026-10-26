using LeagueTeamBuilder;
using LeagueTeamBuilder.Data;
using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- database (SQLite) and sign-in -------------------------------------------------------------------------
builder.Services.AddLeagueDatabase(builder.Configuration, builder.Environment);

builder.Services
    .AddDefaultIdentity<ApplicationUser>(o =>
    {
        // No e-mail is sent, so nothing can be confirmed by e-mail.
        o.SignIn.RequireConfirmedAccount = false;
        // Length matters more than character classes.
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddUserManager<AppUserManager>()
    .AddSignInManager<AppSignInManager>();

builder.Services.AddTransient<IEmailSender, LeagueTeamBuilder.Services.NoOpEmailSender>();

// A role change or a deactivation shows up in an open session within about a minute (the default is 30 minutes).
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

// Keys that protect the sign-in cookie are kept in the database, so users stay signed in across restarts and redeploys.
builder.Services.AddDataProtection()
    .SetApplicationName("LeagueTeamBuilder")
    .PersistKeysToDbContext<ApplicationDbContext>();

// Every action of a League controller is checked against the role table (Services/RolePermissions.cs).
builder.Services.AddControllersWithViews(o => o.Filters.Add<RolePermissionFilter>());
builder.Services.AddRazorPages();

// ---- seams that replace HFECloud's permission groups, NotificationService and AdminActivityLogger -----------------
builder.Services.AddScoped<IPermissionChecker, RolePermissionChecker>();
builder.Services.AddScoped<ILeagueNotifier, LeagueNotifier>();
builder.Services.AddScoped<IActivityLog, ActivityLog>();

// The look picker: which of the League looks (Steel, Brutal, Forest) this request gets. See Services/LeagueLooks.cs.
builder.Services.AddScoped<LookResolver>();

// ---- League services (scoped: each one works on the request's database context) ------------------------------------
builder.Services.AddScoped<ChampionCatalogService>();
builder.Services.AddScoped<LeagueSettingsService>();
builder.Services.AddScoped<LeagueTierService>();
builder.Services.AddScoped<LeagueGuestService>();
builder.Services.AddScoped<LeagueGuestLinkService>();
builder.Services.AddScoped<LeagueProfileService>();
builder.Services.AddScoped<LeagueClashScheduleService>();
builder.Services.AddScoped<LeagueClashBoardService>();
builder.Services.AddScoped<LeagueAutoSendService>();
builder.Services.AddScoped<LeagueBuilderService>();
builder.Services.AddScoped<LeagueMatchService>();
builder.Services.AddScoped<LeagueHistoryService>();
builder.Services.AddScoped<LeagueOverviewService>();
// Rank badge pictures (Phase B): the catalog is one shared in-memory copy, the service changes the database and refreshes it.
builder.Services.AddSingleton<RankBadgeCatalog>();
builder.Services.AddScoped<LeagueRankBadgeService>();
builder.Services.AddScoped<TeamBalancer>();
builder.Services.AddScoped<ClashTeamBuilder>();

// Posts a picture to Discord when Discord:LeagueWebhookUrl is set; with no URL the feature is off and its buttons are hidden.
builder.Services.AddHttpClient<LeagueDiscordSender>();

var app = builder.Build();

await app.InitializeLeagueDatabaseAsync();

// Fill the rank badge catalog from the database. A failure (for example the migration for the badge table was not made yet)
// must not stop the site: every badge then shows its default picture.
try
{
    using var badgeScope = app.Services.CreateScope();
    await badgeScope.ServiceProvider.GetRequiredService<LeagueRankBadgeService>().ReloadCatalogAsync();
}
catch (Exception ex)
{
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RankBadges")
        .LogWarning(ex, "Could not load the rank badges; every tier shows its default picture. Did you run: dotnet ef migrations add AddLeagueRankBadges ?");
}

// A League action missing from the role table is denied for everybody. Say so at start-up instead of leaving it to a 403 later.
{
    var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Permissions");
    foreach (var missing in RolePermissions.FindUnlistedActions(typeof(Program).Assembly))
        log.LogWarning("League action {Action} is not in the role table (Services/RolePermissions.cs), so nobody can use it.", missing);
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();
app.MapControllerRoute(name: "default", pattern: "{controller=League}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();
