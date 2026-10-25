namespace LeagueTeamBuilder.Services
{
    // Results/export step 12h1: what the server accepts from the browser before it posts a League picture to Discord.
    // Pure (no I/O), covered by LeagueTeamBuilder.Tests/LeaguePictureRulesTests.cs. The picture is drawn in the browser; the server only
    // checks that it really is a 1280 x 720 PNG of a sane size, so the League webhook cannot be used to post anything else.
    public static class LeaguePictureRules
    {
        public const int Width = 1280;
        public const int Height = 720;
        public const int MaxBytes = 8 * 1024 * 1024;   // Discord's limit for a webhook upload is about 8-10 MB
        public const string DefaultFileName = "hfe-league.png";

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        // PNG signature, then the IHDR chunk with width and height (big-endian) at bytes 16-23.
        public static bool IsLeaguePng(byte[]? data)
        {
            if (data == null || data.Length < 33 || data.Length > MaxBytes) return false;
            for (var i = 0; i < PngSignature.Length; i++)
                if (data[i] != PngSignature[i]) return false;
            if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R') return false;
            return ReadInt(data, 16) == Width && ReadInt(data, 20) == Height;
        }

        private static int ReadInt(byte[] d, int at) => (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];

        // Letters, digits, dash, underscore and dot only; always ends in .png; at most 80 characters.
        public static string SafeFileName(string? name)
        {
            var clean = new string((name ?? "").Where(c => c < 128 && (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')).ToArray()).Trim('.');
            if (clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) clean = clean[..^4];
            clean = clean.Replace(".", "-");
            if (clean.Length > 76) clean = clean[..76];
            return clean.Length == 0 ? DefaultFileName : clean + ".png";
        }
    }
}
