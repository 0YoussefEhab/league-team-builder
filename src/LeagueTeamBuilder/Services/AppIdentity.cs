using LeagueTeamBuilder.Data;
using LeagueTeamBuilder.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeagueTeamBuilder.Services
{
    // Everyone who registers through the Identity pages becomes a Member. The Identity default UI cannot be hooked
    // without copying its pages, so the hook lives here: the "with password" overload is the one the Register page calls.
    // The "without password" overload is NOT overridden on purpose: the admin seed uses it so the first admin gets
    // the Admin role only.
    public class AppUserManager : UserManager<ApplicationUser>
    {
        public AppUserManager(
            IUserStore<ApplicationUser> store,
            IOptions<IdentityOptions> optionsAccessor,
            IPasswordHasher<ApplicationUser> passwordHasher,
            IEnumerable<IUserValidator<ApplicationUser>> userValidators,
            IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
            ILookupNormalizer keyNormalizer,
            IdentityErrorDescriber errors,
            IServiceProvider services,
            ILogger<UserManager<ApplicationUser>> logger)
            : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
        {
        }

        public override async Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
        {
            var created = await base.CreateAsync(user, password);
            if (!created.Succeeded) return created;

            var roleResult = await AddToRoleAsync(user, AppRoles.Member);
            return roleResult.Succeeded ? created : roleResult;
        }
    }

    // A deactivated account cannot sign in. Deactivating also changes the security stamp (AdminUsersController), which
    // ends a session that is already open within about a minute (see Program.cs).
    public class AppSignInManager : SignInManager<ApplicationUser>
    {
        public AppSignInManager(
            UserManager<ApplicationUser> userManager,
            IHttpContextAccessor contextAccessor,
            IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
            IOptions<IdentityOptions> optionsAccessor,
            ILogger<SignInManager<ApplicationUser>> logger,
            IAuthenticationSchemeProvider schemes,
            IUserConfirmation<ApplicationUser> confirmation)
            : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
        {
        }

        public override async Task<bool> CanSignInAsync(ApplicationUser user) =>
            !user.IsDeactivated && await base.CanSignInAsync(user);
    }

    // No e-mail is sent (no confirmation, no password-reset mail). An admin resets a forgotten password by hand for now.
    public class NoOpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
    }
}
