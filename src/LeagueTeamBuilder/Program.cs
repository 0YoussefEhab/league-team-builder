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
builder.Services.AddScoped<TeamBalancer>();
builder.Services.AddScoped<ClashTeamBuilder>();

// Posts a picture to Discord when Discord:LeagueWebhookUrl is set; with no URL the feature is off and its buttons are hidden.
builder.Services.AddHttpClient<LeagueDiscordSender>();

var app = builder.Build();

await app.InitializeLeagueDatabaseAsync();

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
