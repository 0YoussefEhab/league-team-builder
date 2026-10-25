using Microsoft.AspNetCore.Identity;

namespace LeagueTeamBuilder.Models
{
    // The account. Identity supplies Id, UserName, Email, PasswordHash and so on; the rest is what the League code reads.
    // DisplayName is never null (the League code compares it with "" in queries). When it is empty the code falls back to
    // UserName, so a new account that never sets one still shows up under its login name.
    public class ApplicationUser : IdentityUser
    {
        public string DisplayName { get; set; } = string.Empty;

        // Optional picture URL. The League profile can copy it once ("Copy name and picture"); League never writes to it.
        public string? AvatarUrl { get; set; }

        // A deactivated account cannot sign in and is hidden from "link a guest to a user". Nothing is deleted, so league history stays intact.
        public bool IsDeactivated { get; set; }

        // The user's chosen look ("steel", "brutal", "forest"). Null = use the site default. Read and written by the theme picker in step C1.
        public string? Look { get; set; }
    }
}
