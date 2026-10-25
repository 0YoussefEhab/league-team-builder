using System.Net.Http.Headers;
using System.Text.Json;

namespace LeagueTeamBuilder.Services
{
    // Results/export step 12h1: posts a League picture (a PNG the browser drew) to its OWN Discord channel.
    // Webhook URL = Render env var Discord__LeagueWebhookUrl (a discord.com/api/webhooks/... URL). It is separate from the
    // admin-notification webhook (Discord__WebhookUrl) so the two channels never mix. Unset or invalid = feature off, buttons hidden.
    // The URL is read once, never logged, rendered or stored. Nobody is pinged (allowed_mentions parse = none).
    public class LeagueDiscordSender
    {
        private readonly HttpClient _http;
        private readonly string? _webhookUrl;
        private readonly ILogger<LeagueDiscordSender> _logger;

        public LeagueDiscordSender(HttpClient http, IConfiguration config, ILogger<LeagueDiscordSender> logger)
        {
            _http = http;
            _logger = logger;
            var url = config["Discord:LeagueWebhookUrl"];
            _webhookUrl = DiscordWebhook.IsValidUrl(url) ? url!.Trim() : null;
        }

        public bool IsConfigured => _webhookUrl != null;

        public async Task<(bool Ok, string? Error)> SendImageAsync(byte[] png, string fileName)
        {
            if (_webhookUrl == null) return (false, "Sending to Discord is not set up (Discord__LeagueWebhookUrl is missing).");
            try
            {
                using var form = new MultipartFormDataContent();
                var payload = JsonSerializer.Serialize(new
                {
                    username = "HFE League",
                    allowed_mentions = new { parse = Array.Empty<string>() },
                    attachments = new[] { new { id = 0, filename = fileName } },
                });
                form.Add(new StringContent(payload, System.Text.Encoding.UTF8, "application/json"), "payload_json");
                var file = new ByteArrayContent(png);
                file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(file, "files[0]", fileName);

                using var response = await _http.PostAsync(_webhookUrl, form);
                if (response.IsSuccessStatusCode) return (true, null);
                _logger.LogWarning("League Discord post failed with status {Status}.", (int)response.StatusCode);
                return (false, (int)response.StatusCode == 429
                    ? "Discord says too many pictures were sent. Wait a few seconds and try again."
                    : "Discord did not accept the picture.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("League Discord post failed: {Type}.", ex.GetType().Name);
                return (false, "Could not reach Discord. Try again.");
            }
        }
    }
}
