using LeagueTeamBuilder.Services;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // A7: game names (the part of a Riot ID before the "#"). Pure rules, no database.
    public class LeagueGameNameRulesTests
    {
        [Theory]
        [InlineData("Suguru#EG1", "Suguru")]
        [InlineData("Suguru", "Suguru")]
        [InlineData("  Suguru  #EG1", "Suguru")]
        [InlineData("Su#guru#EG1", "Su")]
        public void FromRiotId_takes_the_part_before_the_hash(string riotId, string expected)
        {
            Assert.Equal(expected, LeagueGameNameRules.FromRiotId(riotId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("#EG1")]
        [InlineData("  #EG1")]
        public void FromRiotId_gives_null_when_there_is_no_name(string? riotId)
        {
            Assert.Null(LeagueGameNameRules.FromRiotId(riotId));
        }

        [Fact]
        public void ValidateTyped_accepts_a_name_with_or_without_a_tag_and_trims_it()
        {
            Assert.Null(LeagueGameNameRules.ValidateTyped("  Suguru  ", out var clean1));
            Assert.Equal("Suguru", clean1);
            Assert.Null(LeagueGameNameRules.ValidateTyped("Suguru#EG1", out var clean2));
            Assert.Equal("Suguru#EG1", clean2);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("    ")]
        public void ValidateTyped_refuses_an_empty_box(string? typed)
        {
            var error = LeagueGameNameRules.ValidateTyped(typed, out var clean);
            Assert.NotNull(error);
            Assert.Equal("", clean);
        }

        [Fact]
        public void ValidateTyped_refuses_a_line_break_a_missing_name_and_too_long_text()
        {
            Assert.Contains("one line", LeagueGameNameRules.ValidateTyped("Su\nguru", out _)!);
            Assert.Contains("before the #", LeagueGameNameRules.ValidateTyped("#EG1", out _)!);

            var tooLongId = new string('a', LeagueProfileService.MaxRiotIdLength + 1);
            Assert.Contains("at most " + LeagueProfileService.MaxRiotIdLength, LeagueGameNameRules.ValidateTyped(tooLongId, out _)!);

            // The whole ID is short enough, but the game name part is longer than a name may be.
            var tooLongName = new string('a', LeagueProfileService.MaxNameLength + 1);
            Assert.Contains("at most " + LeagueProfileService.MaxNameLength, LeagueGameNameRules.ValidateTyped(tooLongName + "#EG1", out _)!);
        }

        [Fact]
        public void Key_treats_case_spaces_and_invisible_marks_as_the_same()
        {
            var expected = LeagueGameNameRules.Key("suguru");
            Assert.Equal("suguru", expected);
            Assert.Equal(expected, LeagueGameNameRules.Key("  SUGURU  "));
            Assert.Equal(expected, LeagueGameNameRules.Key("Su\u200Bguru"));    // zero-width space
            Assert.Equal(expected, LeagueGameNameRules.Key("\uFF33uguru"));     // full-width S
            Assert.Equal("two words", LeagueGameNameRules.Key("Two    Words"));
            Assert.Equal("", LeagueGameNameRules.Key(null));
            Assert.Equal("", LeagueGameNameRules.Key("   "));
        }

        [Fact]
        public void FindDuplicate_returns_the_second_spelling_of_a_shared_name()
        {
            Assert.Equal("a", LeagueGameNameRules.FindDuplicate(new string?[] { "A", "b", "a " }));
            Assert.Null(LeagueGameNameRules.FindDuplicate(new string?[] { "A", "b", "c" }));
            Assert.Null(LeagueGameNameRules.FindDuplicate(new string?[] { "", null, "x", "  " }));
        }
    }
}
