using LeagueTeamBuilder.Models;
using Microsoft.AspNetCore.Identity;

namespace LeagueTeamBuilder.Data
{
    // Start-up seed: the three roles, the first Admin, the champion list.
    // The first Admin comes from configuration, never from a file in the repo and never with a default password:
    //   Seed:AdminEmail, Seed:AdminPassword  (dotnet user-secrets in development, environment variables
    //   Seed__AdminEmail / Seed__AdminPassword in Docker or on a host).
    // It only acts when there is NO active Admin at all, and it never changes an existing password.
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(IServiceProvider sp, IConfiguration config, ILogger log)
        {
            var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var name in AppRoles.All)
            {
                if (await roles.RoleExistsAsync(name)) continue;
                var created = await roles.CreateAsync(new IdentityRole(name));
                if (!created.Succeeded) throw new InvalidOperationException($"Could not create role {name}: {Describe(created)}");
            }

            await SeedAdminAsync(sp, config, log);

            await LeagueChampionSeeder.SeedAsync(sp.GetRequiredService<ApplicationDbContext>());
        }

        private static async Task SeedAdminAsync(IServiceProvider sp, IConfiguration config, ILogger log)
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

            var admins = await users.GetUsersInRoleAsync(AppRoles.Admin);
            if (admins.Any(a => !a.IsDeactivated)) return;

            var email = config["Seed:AdminEmail"]?.Trim();
            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                log.LogWarning("There is no active Admin and Seed:AdminEmail / Seed:AdminPassword are not set, so nobody can manage accounts. " +
                               "Set both (user-secrets or environment variables) and restart.");
                return;
            }

            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                // The overload without a password is used on purpose: AppUserManager adds the Member role only to the
                // overload with a password, and the first admin should be an Admin only.
                var created = await users.CreateAsync(user);
                if (!created.Succeeded) throw new InvalidOperationException($"Could not create the admin account: {Describe(created)}");

                var withPassword = await users.AddPasswordAsync(user, password);
                if (!withPassword.Succeeded)
                {
                    await users.DeleteAsync(user);
                    throw new InvalidOperationException($"Seed:AdminPassword was rejected, so no account was created: {Describe(withPassword)}");
                }
            }
            else if (user.IsDeactivated)
            {
                user.IsDeactivated = false;
                await users.UpdateAsync(user);
            }

            var inRole = await users.AddToRoleAsync(user, AppRoles.Admin);
            if (!inRole.Succeeded) throw new InvalidOperationException($"Could not give {email} the Admin role: {Describe(inRole)}");
            log.LogInformation("Admin account ready: {Email}", email);
        }

        private static string Describe(IdentityResult r) => string.Join("; ", r.Errors.Select(e => e.Description));
    }
}
