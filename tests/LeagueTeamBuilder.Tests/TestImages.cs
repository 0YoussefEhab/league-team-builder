using SkiaSharp;

namespace LeagueTeamBuilder.Tests
{
    // Real picture bytes for the badge tests. PNG, JPEG and WebP are encoded by SkiaSharp itself; the "wrong kind" files
    // (GIF, BMP, an SVG text) are built by hand because SkiaSharp cannot encode them.
    internal static class TestImages
    {
        public static byte[] Make(int width, int height, SKEncodedImageFormat format)
        {
            using var bmp = new SKBitmap(width, height);
            bmp.Erase(new SKColor(200, 60, 60));
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(format, 90);
            return data.ToArray();
        }

        public static byte[] Png(int w = 64, int h = 64) => Make(w, h, SKEncodedImageFormat.Png);
        public static byte[] Jpeg(int w = 64, int h = 64) => Make(w, h, SKEncodedImageFormat.Jpeg);
        public static byte[] Webp(int w = 64, int h = 64) => Make(w, h, SKEncodedImageFormat.Webp);

        // A real 16 x 16 GIF (68 bytes, made with an image library).
        public static byte[] Gif16() => Convert.FromBase64String(
            "R0lGODdhEAAQAIEAAMg8PAAAAAAAAAAAACwAAAAAEAAQAEAIHQABCBxIsKDBgwgTKlzIsKHDhxAjSpxIsaLFgQEBADs=");

        // A plain 16 x 16, 24-bit BMP: 54 header bytes + 16 rows of 48 bytes.
        public static byte[] Bmp16()
        {
            const int w = 16, h = 16;
            const int pixelBytes = w * h * 3;
            var b = new byte[54 + pixelBytes];
            b[0] = (byte)'B'; b[1] = (byte)'M';
            BitConverter.GetBytes(b.Length).CopyTo(b, 2);
            BitConverter.GetBytes(54).CopyTo(b, 10);
            BitConverter.GetBytes(40).CopyTo(b, 14);
            BitConverter.GetBytes(w).CopyTo(b, 18);
            BitConverter.GetBytes(h).CopyTo(b, 22);
            BitConverter.GetBytes((short)1).CopyTo(b, 26);
            BitConverter.GetBytes((short)24).CopyTo(b, 28);
            BitConverter.GetBytes(pixelBytes).CopyTo(b, 34);
            for (var i = 54; i < b.Length; i++) b[i] = 0x80;
            return b;
        }

        public static byte[] SvgText() => System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><script>alert(1)</script></svg>");
    }
}
