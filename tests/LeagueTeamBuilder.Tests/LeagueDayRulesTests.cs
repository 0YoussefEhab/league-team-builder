using LeagueTeamBuilder.Services;
using Xunit;

namespace LeagueTeamBuilder.Tests
{
    // A7: the match-day rules. A fixed UTC+2 zone is passed in, so the results do not depend on the computer's time zone
    // data or on summer time.
    public class LeagueDayRulesTests
    {
        private static readonly TimeZoneInfo Plus2 = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");

        private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

        [Fact]
        public void A_late_evening_in_UTC_is_already_the_next_day_in_Cairo()
        {
            Assert.Equal(new DateOnly(2026, 1, 16), LeagueDayRules.CairoDay(Utc(2026, 1, 15, 22, 30), Plus2));
            Assert.Equal(new DateOnly(2026, 1, 15), LeagueDayRules.CairoDay(Utc(2026, 1, 15, 21, 59), Plus2));
        }

        [Fact]
        public void Today_and_Yesterday_follow_the_Cairo_day()
        {
            var now = Utc(2026, 1, 15, 23, 0);
            Assert.Equal(new DateOnly(2026, 1, 16), LeagueDayRules.Today(now, Plus2));
            Assert.Equal(new DateOnly(2026, 1, 15), LeagueDayRules.Yesterday(now, Plus2));
        }

        [Fact]
        public void A_day_range_runs_from_one_Cairo_midnight_to_the_next_and_the_end_is_not_included()
        {
            var (start, end) = LeagueDayRules.DayRangeUtc(new DateOnly(2026, 1, 16), Plus2);
            Assert.Equal(Utc(2026, 1, 15, 22), start);
            Assert.Equal(Utc(2026, 1, 16, 22), end);
        }

        [Fact]
        public void LocalToUtc_and_InputValue_are_each_others_opposite()
        {
            var utc = LeagueDayRules.LocalToUtc(new DateTime(2026, 1, 16, 10, 30, 0), Plus2);
            Assert.Equal(Utc(2026, 1, 16, 8, 30), utc);
            Assert.Equal("2026-01-16T10:30", LeagueDayRules.InputValue(utc, Plus2));
            Assert.Equal("16 Jan 2026 10:30 Cairo", LeagueDayRules.PlainCairo(utc, Plus2));
        }

        [Fact]
        public void A_result_entered_more_than_six_hours_from_the_play_time_counts_as_added_later()
        {
            var played = Utc(2026, 1, 15, 12);
            Assert.False(LeagueDayRules.IsAddedLater(played, null));
            Assert.False(LeagueDayRules.IsAddedLater(played, played.AddHours(1)));
            Assert.False(LeagueDayRules.IsAddedLater(played, played.AddHours(6)));          // exactly 6 hours: not later
            Assert.True(LeagueDayRules.IsAddedLater(played, played.AddHours(6).AddMinutes(1)));
            Assert.True(LeagueDayRules.IsAddedLater(played, played.AddHours(-7)));          // the gap counts in both directions
        }

        [Fact]
        public void ParsePlayedOn_reads_the_form_value_as_Cairo_time()
        {
            var now = Utc(2026, 10, 27, 12);
            var (utc, error) = LeagueDayRules.ParsePlayedOn("2026-01-16T10:30", now, Plus2);
            Assert.Null(error);
            Assert.Equal(Utc(2026, 1, 16, 8, 30), utc);

            var (withSeconds, error2) = LeagueDayRules.ParsePlayedOn(" 2026-01-16T10:30:00 ", now, Plus2);
            Assert.Null(error2);
            Assert.Equal(Utc(2026, 1, 16, 8, 30), withSeconds);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ParsePlayedOn_with_nothing_given_is_not_an_error(string? text)
        {
            var (utc, error) = LeagueDayRules.ParsePlayedOn(text, Utc(2026, 10, 27, 12), Plus2);
            Assert.Null(utc);
            Assert.Null(error);
        }

        [Fact]
        public void ParsePlayedOn_refuses_nonsense_the_future_and_dates_before_2020()
        {
            var now = Utc(2026, 10, 27, 12);
            Assert.NotNull(LeagueDayRules.ParsePlayedOn("yesterday", now, Plus2).Error);
            Assert.NotNull(LeagueDayRules.ParsePlayedOn("16/01/2026 10:30", now, Plus2).Error);
            Assert.Contains("future", LeagueDayRules.ParsePlayedOn("2026-10-28T10:30", now, Plus2).Error!);
            Assert.Contains("past", LeagueDayRules.ParsePlayedOn("2019-12-31T10:00", now, Plus2).Error!);
        }
    }
}
