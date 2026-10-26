using LeagueTeamBuilder.Services;
using SkiaSharp;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // B4: the pure upload rules (RankBadgeRules.Check) and the limits in MediaUploadLimits.
    public class RankBadgeRulesTests
    {
        [Theory]
        [InlineData(SKEncodedImageFormat.Png, ".png")]
        [InlineData(SKEncodedImageFormat.Jpeg, ".jpg")]
        [InlineData(SKEncodedImageFormat.Jpeg, ".jpeg")]
        [InlineData(SKEncodedImageFormat.Webp, ".webp")]
        public void A_real_picture_with_the_matching_ending_is_accepted(SKEncodedImageFormat format, string ext)
        {
            Assert.Null(RankBadgeRules.Check(TestImages.Make(64, 64, format), ext));
        }

        [Fact]
        public void The_ending_is_not_case_sensitive()
        {
            Assert.Null(RankBadgeRules.Check(TestImages.Png(), ".PNG"));
            Assert.Null(RankBadgeRules.Check(TestImages.Jpeg(), ".JPG"));
        }

        [Theory]
        [InlineData(SKEncodedImageFormat.Png, ".jpg")]
        [InlineData(SKEncodedImageFormat.Png, ".webp")]
        [InlineData(SKEncodedImageFormat.Jpeg, ".png")]
        [InlineData(SKEncodedImageFormat.Webp, ".png")]
        [InlineData(SKEncodedImageFormat.Webp, ".jpeg")]
        public void A_picture_whose_ending_names_another_format_is_refused(SKEncodedImageFormat format, string ext)
        {
            var problem = RankBadgeRules.Check(TestImages.Make(64, 64, format), ext);
            Assert.NotNull(problem);
            Assert.Contains("doesn't match", problem!);
        }

        [Fact]
        public void An_empty_file_is_refused()
        {
            Assert.NotNull(RankBadgeRules.Check(Array.Empty<byte>(), ".png"));
        }

        [Fact]
        public void Text_renamed_to_png_is_not_a_readable_image()
        {
            var problem = RankBadgeRules.Check(System.Text.Encoding.UTF8.GetBytes("hello, I am not a picture"), ".png");
            Assert.NotNull(problem);
            Assert.Contains("readable image", problem!);
        }

        [Fact]
        public void An_svg_renamed_to_png_is_refused_and_never_stored()
        {
            var problem = RankBadgeRules.Check(TestImages.SvgText(), ".png");
            Assert.NotNull(problem);
            Assert.Contains("readable image", problem!);
        }

        [Theory]
        [InlineData(".png")]
        [InlineData(".gif")]
        public void A_gif_is_refused_even_when_renamed_to_png(string ext)
        {
            var problem = RankBadgeRules.Check(TestImages.Gif16(), ext);
            Assert.NotNull(problem);
            Assert.Contains("must be a PNG", problem!);
        }

        [Fact]
        public void A_bmp_renamed_to_png_is_refused()
        {
            var problem = RankBadgeRules.Check(TestImages.Bmp16(), ".png");
            Assert.NotNull(problem);
            Assert.Contains("must be a PNG", problem!);
        }

        [Fact]
        public void The_smallest_and_the_biggest_allowed_sizes_pass()
        {
            Assert.Null(RankBadgeRules.Check(TestImages.Png(16, 16), ".png"));
            Assert.Null(RankBadgeRules.Check(TestImages.Png(2048, 16), ".png"));
            Assert.Null(RankBadgeRules.Check(TestImages.Png(16, 2048), ".png"));
        }

        [Theory]
        [InlineData(15, 16)]
        [InlineData(16, 15)]
        [InlineData(1, 1)]
        public void A_picture_under_16_pixels_is_too_small(int w, int h)
        {
            var problem = RankBadgeRules.Check(TestImages.Png(w, h), ".png");
            Assert.NotNull(problem);
            Assert.Contains("too small", problem!);
        }

        [Theory]
        [InlineData(2049, 16)]
        [InlineData(16, 2049)]
        public void A_picture_over_2048_pixels_is_too_big(int w, int h)
        {
            var problem = RankBadgeRules.Check(TestImages.Png(w, h), ".png");
            Assert.NotNull(problem);
            Assert.Contains("too big", problem!);
        }

        [Theory]
        [InlineData(SKEncodedImageFormat.Png, ".png", true)]
        [InlineData(SKEncodedImageFormat.Png, ".jpg", false)]
        [InlineData(SKEncodedImageFormat.Jpeg, ".jpg", true)]
        [InlineData(SKEncodedImageFormat.Jpeg, ".jpeg", true)]
        [InlineData(SKEncodedImageFormat.Jpeg, ".webp", false)]
        [InlineData(SKEncodedImageFormat.Webp, ".webp", true)]
        [InlineData(SKEncodedImageFormat.Gif, ".gif", false)]
        [InlineData(SKEncodedImageFormat.Bmp, ".bmp", false)]
        public void ExtensionMatches_follows_the_table(SKEncodedImageFormat format, string ext, bool expected)
        {
            Assert.Equal(expected, RankBadgeRules.ExtensionMatches(ext, format));
        }

        [Fact]
        public void The_allowed_endings_never_include_svg_or_gif_and_the_limit_is_one_megabyte()
        {
            var allowed = MediaUploadLimits.RankBadgeExtensions;
            Assert.Equal(new[] { ".jpeg", ".jpg", ".png", ".webp" }, allowed.OrderBy(x => x).ToArray());
            Assert.DoesNotContain(".svg", allowed);
            Assert.DoesNotContain(".gif", allowed);
            Assert.Equal(1024 * 1024, MediaUploadLimits.RankBadgeMaxBytes);
        }
    }
}
