using System.Globalization;

namespace LeagueTeamBuilder.Services
{
    // Results/export step 1: "Played on" and Cairo day maths. Pure rules (no EF, no clock: the caller passes "now"),
    // covered by LeagueTeamBuilder.Tests/LeagueDayRulesTests.cs.
    //   PlayedUtc is stored in UTC. Everything the member sees (and every "today / yesterday / pick a day" export)
    //   uses the CAIRO calendar day, with local midnight as the boundary (docs/LEAGUE_BUILD_SPEC.md section 18.4).
    //   Egypt has daylight saving time again (since 2023), so a Cairo day is 23, 24 or 25 hours long: a day is
    //   always a half-open range [midnight, next midnight) in UTC, never "+24 hours".
    // Every method takes an optional TimeZoneInfo so tests can pass their own zone; production uses Cairo.
    public static class LeagueDayRules
    {
        // A match whose "played" time is more than this far from the time its result was entered gets the "Added later" tag.
        public static readonly TimeSpan AddedLaterAfter = TimeSpan.FromHours(6);

        // A typed "played on" may be at most this far in the future (clock differences between phone and server).
        public static readonly TimeSpan MaxFuture = TimeSpan.FromMinutes(5);

        // Nothing in this app was played before this; catches a mistyped year such as 0026.
        public static readonly DateTime EarliestPlayedUtc = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // What <input type="datetime-local"> sends: Cairo local time, no zone.
        public const string InputFormat = "yyyy-MM-dd'T'HH:mm";
        private static readonly string[] AcceptedInputFormats = { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" };

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Cairo (Africa/Cairo; Windows id as fallback). If neither exists on the host, fixed UTC+2 so nothing crashes
        // (off by one hour in summer only; same fallback as AdminNfcCardController).
        public static readonly TimeZoneInfo Cairo = FindCairo();

        private static TimeZoneInfo FindCairo()
        {
            foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.CreateCustomTimeZone("Cairo-fixed", TimeSpan.FromHours(2), "Cairo", "Cairo");
        }

        private static DateTime AsUtc(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        // A UTC instant as Cairo wall-clock time (Kind Unspecified).
        public static DateTime ToCairo(DateTime utc, TimeZoneInfo? tz = null) =>
            TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), tz ?? Cairo);

        // The Cairo calendar day an instant falls on.
        public static DateOnly CairoDay(DateTime utc, TimeZoneInfo? tz = null) =>
            DateOnly.FromDateTime(ToCairo(utc, tz));

        public static DateOnly Today(DateTime nowUtc, TimeZoneInfo? tz = null) => CairoDay(nowUtc, tz);

        public static DateOnly Yesterday(DateTime nowUtc, TimeZoneInfo? tz = null) => CairoDay(nowUtc, tz).AddDays(-1);

        // Cairo wall-clock time -> UTC instant.
        //   Skipped hour (spring forward): moved forward one hour, as AdminNfcCardController does.
        //   Repeated hour (fall back): the FIRST of the two instants (summer time).
        public static DateTime LocalToUtc(DateTime local, TimeZoneInfo? tz = null)
        {
            var zone = tz ?? Cairo;
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) local = local.AddHours(1);
            if (zone.IsAmbiguousTime(local))
            {
                var offset = zone.GetAmbiguousTimeOffsets(local).Max();
                return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
            }
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        // The UTC range of one Cairo day: StartUtc inclusive, EndUtc EXCLUSIVE (= the next day's midnight).
        // Use  m.PlayedUtc >= StartUtc && m.PlayedUtc < EndUtc.
        public static (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateOnly day, TimeZoneInfo? tz = null)
        {
            var start = LocalToUtc(day.ToDateTime(TimeOnly.MinValue), tz);
            var end = LocalToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), tz);
            return (start, end);
        }

        // "Added later": the result was entered more than 6 hours away from the time the match was played.
        // Not finished (no FinishedUtc) = never.
        public static bool IsAddedLater(DateTime playedUtc, DateTime? finishedUtc)
        {
            if (finishedUtc is not DateTime finished) return false;
            return (finished - playedUtc).Duration() > AddedLaterAfter;
        }

        // The value an <input type="datetime-local"> shows for this instant (Cairo wall-clock, "2026-10-16T21:05").
        public static string InputValue(DateTime utc, TimeZoneInfo? tz = null) =>
            ToCairo(utc, tz).ToString(InputFormat, Inv);

        // Plain text for the server-rendered fallback (JavaScript off): "16 Oct 2026 21:05 Cairo".
        public static string PlainCairo(DateTime utc, TimeZoneInfo? tz = null) =>
            ToCairo(utc, tz).ToString("d MMM yyyy HH:mm", Inv) + " Cairo";

        // Reads what the form sent. null or blank = "not given" (Utc null, no error): the caller keeps "now".
        // Otherwise the text must be Cairo local "yyyy-MM-ddTHH:mm"; the result is UTC. Errors are shown to the member.
        public static (DateTime? Utc, string? Error) ParsePlayedOn(string? text, DateTime nowUtc, TimeZoneInfo? tz = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return (null, null);
            if (!DateTime.TryParseExact(text.Trim(), AcceptedInputFormats, Inv, DateTimeStyles.None, out var local))
                return (null, "The played date is not a valid date and time.");

            var utc = LocalToUtc(local, tz);
            if (utc > AsUtc(nowUtc) + MaxFuture) return (null, "A match cannot have been played in the future.");
            if (utc < EarliestPlayedUtc) return (null, "That played date is too far in the past.");
            return (utc, null);
        }
    }
}
