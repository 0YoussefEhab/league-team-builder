using System.Security.Claims;
using LeagueTeamBuilder.Models;
using LeagueTeamBuilder.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // A7: the allowed looks and which look a request gets (cookie, saved choice, configured default).
    public class LeagueLooksTests
    {
        [Fact]
        public void The_three_looks_are_listed_with_steel_first()
        {
            Assert.Equal(new[] { "steel", "brutal", "forest" }, LeagueLooks.All.Select(l => l.Key).ToArray());
        }

        [Theory]
        [InlineData("steel", "steel")]
        [InlineData("BRUTAL", "brutal")]
        [InlineData("  Forest  ", "forest")]
        public void Find_ignores_case_and_spaces(string typed, string expected)
        {
            Assert.Equal(expected, LeagueLooks.Find(typed)?.Key);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("neon")]
        [InlineData("steel;forest")]
        public void Find_gives_null_for_anything_that_is_not_listed(string? typed)
        {
            Assert.Null(LeagueLooks.Find(typed));
        }

        [Fact]
        public void Every_look_has_its_font_file_and_every_non_default_look_has_a_css_block()
        {
            var looksCss = WebProject.Find("wwwroot", "css", "leaguehub-looks.css");
            Assert.True(looksCss is not null && File.Exists(looksCss), "Could not find wwwroot/css/leaguehub-looks.css above the test folder.");
            var css = File.ReadAllText(looksCss!);

            foreach (var look in LeagueLooks.All)
            {
                Assert.StartsWith("~/", look.FontsHref);
                var fontFile = WebProject.Find(new[] { "wwwroot" }.Concat(look.FontsHref[2..].Split('/')).ToArray());
                Assert.True(fontFile is not null && File.Exists(fontFile), "Missing fonts stylesheet for " + look.Key);

                // Steel is the :root default in leaguehub-theme.css; the others are [data-look] blocks.
                if (look.Key != "steel")
                    Assert.Contains($"[data-look=\"{look.Key}\"]", css);
            }
        }

        // ---- LookResolver ----

        private static IConfiguration Config(string? defaultLook = null)
        {
            var values = new Dictionary<string, string?>();
            if (defaultLook != null) values["Theme:DefaultLook"] = defaultLook;
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        private static DefaultHttpContext Request(string? cookie = null, string? signedInUserId = null)
        {
            var http = new DefaultHttpContext();
            if (cookie != null) http.Request.Headers["Cookie"] = LeagueLooks.CookieName + "=" + cookie;
            if (signedInUserId != null)
                http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, signedInUserId) }, "test"));
            return http;
        }

        private static async Task AddUserAsync(SqliteTestDb testDb, string id, string? look)
        {
            await using var db = testDb.NewContext();
            db.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), Look = look });
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task With_nothing_set_the_look_is_steel()
        {
            using var testDb = new SqliteTestDb();
            await using var db = testDb.NewContext();
            var look = await new LookResolver(db, Config()).ResolveAsync(Request());
            Assert.Equal("steel", look.Key);
        }

        [Fact]
        public async Task The_configured_default_is_used_and_a_bad_default_falls_back_to_steel()
        {
            using var testDb = new SqliteTestDb();
            await using var db = testDb.NewContext();
            Assert.Equal("brutal", (await new LookResolver(db, Config("brutal")).ResolveAsync(Request())).Key);
            Assert.Equal("steel", (await new LookResolver(db, Config("neon")).ResolveAsync(Request())).Key);
        }

        [Fact]
        public async Task The_cookie_wins_over_the_saved_look()
        {
            using var testDb = new SqliteTestDb();
            await AddUserAsync(testDb, "u1", "forest");
            await using var db = testDb.NewContext();
            var look = await new LookResolver(db, Config()).ResolveAsync(Request(cookie: "brutal", signedInUserId: "u1"));
            Assert.Equal("brutal", look.Key);
        }

        [Fact]
        public async Task A_signed_in_user_without_a_cookie_gets_the_saved_look()
        {
            using var testDb = new SqliteTestDb();
            await AddUserAsync(testDb, "u1", "forest");
            await using var db = testDb.NewContext();
            var look = await new LookResolver(db, Config("brutal")).ResolveAsync(Request(signedInUserId: "u1"));
            Assert.Equal("forest", look.Key);
        }

        [Fact]
        public async Task A_cookie_that_is_not_a_listed_look_counts_as_missing()
        {
            using var testDb = new SqliteTestDb();
            await AddUserAsync(testDb, "u1", "forest");
            await using var db = testDb.NewContext();
            var look = await new LookResolver(db, Config()).ResolveAsync(Request(cookie: "neon", signedInUserId: "u1"));
            Assert.Equal("forest", look.Key);
        }

        [Fact]
        public async Task A_saved_look_that_is_not_listed_falls_back_to_the_default()
        {
            using var testDb = new SqliteTestDb();
            await AddUserAsync(testDb, "u1", "neon");
            await using var db = testDb.NewContext();
            var look = await new LookResolver(db, Config("brutal")).ResolveAsync(Request(signedInUserId: "u1"));
            Assert.Equal("brutal", look.Key);
        }

        [Fact]
        public async Task A_visitor_who_is_not_signed_in_never_gets_a_saved_look()
        {
            using var testDb = new SqliteTestDb();
            await AddUserAsync(testDb, "u1", "forest");
            await using var db = testDb.NewContext();
            var look = await new LookResolver(db, Config()).ResolveAsync(Request());
            Assert.Equal("steel", look.Key);
        }

        [Fact]
        public async Task The_answer_is_kept_for_the_rest_of_the_request()
        {
            using var testDb = new SqliteTestDb();
            await using var db = testDb.NewContext();
            var resolver = new LookResolver(db, Config());
            var http = Request(cookie: "brutal");
            var first = await resolver.ResolveAsync(http);
            http.Request.Headers["Cookie"] = LeagueLooks.CookieName + "=forest";
            var second = await resolver.ResolveAsync(http);
            Assert.Same(first, second);
        }
    }
}
