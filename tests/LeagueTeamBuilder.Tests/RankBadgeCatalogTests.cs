using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // B4: the in-memory catalog and the placeholder pictures.
    public class RankBadgeCatalogTests
    {
        [Fact]
        public void A_tier_with_no_upload_uses_its_placeholder()
        {
            var c = new RankBadgeCatalog();
            Assert.Equal("/img/ranks/default/gold.svg", c.Url(LeagueRank.Gold));
            Assert.Null(c.UploadedUrl(LeagueRank.Gold));
        }

        [Fact]
        public void An_uploaded_picture_wins_and_the_others_keep_their_placeholder()
        {
            var c = new RankBadgeCatalog();
            c.Replace(new[] { new LeagueRankBadge { Tier = LeagueRank.Gold, ImageUrl = "/uploads/rank-badges/a.png" } });
            Assert.Equal("/uploads/rank-badges/a.png", c.Url(LeagueRank.Gold));
            Assert.Equal("/img/ranks/default/iron.svg", c.Url(LeagueRank.Iron));
        }

        [Fact]
        public void Replace_ignores_blank_addresses_and_unknown_tiers_and_swaps_the_whole_list()
        {
            var c = new RankBadgeCatalog();
            c.Replace(new[]
            {
                new LeagueRankBadge { Tier = LeagueRank.Gold, ImageUrl = "/uploads/rank-badges/a.png" },
                new LeagueRankBadge { Tier = LeagueRank.Iron, ImageUrl = "  " },
                new LeagueRankBadge { Tier = LeagueRank.Bronze, ImageUrl = null },
                new LeagueRankBadge { Tier = (LeagueRank)99, ImageUrl = "/uploads/rank-badges/x.png" },
            });
            Assert.Equal("/uploads/rank-badges/a.png", c.Url(LeagueRank.Gold));
            Assert.Null(c.UploadedUrl(LeagueRank.Iron));
            Assert.Null(c.UploadedUrl(LeagueRank.Bronze));

            c.Replace(Array.Empty<LeagueRankBadge>());
            Assert.Equal("/img/ranks/default/gold.svg", c.Url(LeagueRank.Gold));
        }

        [Fact]
        public void ByKey_has_all_ten_tiers_by_lower_case_name_and_All_is_weakest_first()
        {
            var c = new RankBadgeCatalog();
            var keys = c.ByKey();
            Assert.Equal(10, keys.Count);
            Assert.Equal("/img/ranks/default/grandmaster.svg", keys["grandmaster"]);
            Assert.Equal(LeagueRank.Iron, c.All()[0].Tier);
            Assert.Equal(LeagueRank.Challenger, c.All()[9].Tier);
        }

        [Fact]
        public void Every_tier_has_a_placeholder_file_in_the_web_project()
        {
            var dir = FindPlaceholderFolder();
            Assert.True(dir is not null, "Could not find src/LeagueTeamBuilder/wwwroot/img/ranks/default above the test folder.");
            foreach (var tier in Enum.GetValues<LeagueRank>())
            {
                var file = Path.Combine(dir!, tier.ToString().ToLowerInvariant() + ".svg");
                Assert.True(File.Exists(file), "Missing placeholder: " + file);
                Assert.DoesNotContain("<script", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string? FindPlaceholderFolder()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "LeagueTeamBuilder", "wwwroot", "img", "ranks", "default");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
