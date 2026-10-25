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

builder.Services.AddTransient<IEmailSender, NoOpEmailSender>();

// A role change or a deactivation shows up in an open session within about a minute (the default is 30 minutes).
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

// Keys that protect the sign-in cookie are kept in the database, so users stay signed in across restarts and redeploys.
builder.Services.AddDataProtection()
    .SetApplicationName("LeagueTeamBuilder")
    .PersistKeysToDbContext<ApplicationDbContext>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// Step A4 adds the League services, the permission checker and the other seams here.

var app = builder.Build();

await app.InitializeLeagueDatabaseAsync();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();
app.MapControllerRoute(name: "default", pattern: "{controller=League}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();
