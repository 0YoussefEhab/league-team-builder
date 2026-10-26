using SkiaSharp;

namespace LeagueTeamBuilder.Services
{
    // Phase B1: the pure rules for a rank badge upload, so they can be unit-tested (B4) without a web request.
    // The extension list and the size limit are in MediaUploadLimits (RankBadgeExtensions, RankBadgeMaxBytes).
    public static class RankBadgeRules
    {
        public const int MinSide = 16;
        public const int MaxSide = 2048;

        // Null when the bytes are acceptable, otherwise the sentence to show. Checks, in order: the file decodes as an image,
        // it is really a PNG, JPEG or WebP (so a GIF or an SVG renamed to .png is refused), the format matches the extension
        // the file was uploaded with, and the picture is between 16 and 2048 px on each side.
        public static string? Check(byte[] bytes, string extension)
        {
            if (bytes.Length == 0) return "Rank badge: choose a file first.";

            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                return "That file isn't a readable image.";

            var format = codec.EncodedFormat;
            if (format is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp))
                return "A rank badge must be a PNG, JPG or WebP picture.";
            if (!ExtensionMatches(extension, format))
                return "The file ending doesn't match what is inside the file. Save it again as a PNG, JPG or WebP.";

            var w = codec.Info.Width; var h = codec.Info.Height;
            if (w < MinSide || h < MinSide) return $"The picture is too small. Use at least {MinSide} x {MinSide} pixels.";
            if (w > MaxSide || h > MaxSide) return $"The picture is too big. Use at most {MaxSide} x {MaxSide} pixels.";
            return null;
        }

        public static bool ExtensionMatches(string extension, SKEncodedImageFormat format)
        {
            var ext = (extension ?? "").ToLowerInvariant();
            return format switch
            {
                SKEncodedImageFormat.Png => ext == ".png",
                SKEncodedImageFormat.Jpeg => ext is ".jpg" or ".jpeg",
                SKEncodedImageFormat.Webp => ext == ".webp",
                _ => false,
            };
        }
    }
}
