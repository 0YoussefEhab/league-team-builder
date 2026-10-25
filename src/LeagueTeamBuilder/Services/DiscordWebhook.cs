namespace LeagueTeamBuilder.Services
{
    // Replaces HFECloud's DiscordNotifier.IsValidWebhookUrl (the only part of that class League used).
    public static class DiscordWebhook
    {
        private static readonly string[] Hosts = { "discord.com", "ptb.discord.com", "canary.discord.com", "discordapp.com" };

        // True only for an https Discord webhook address, so a mistyped or hostile setting can never make the site post somewhere else.
        public static bool IsValidUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttps
                && Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
