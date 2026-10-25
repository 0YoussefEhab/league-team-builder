/* League time helpers (results/export step 1). Every League time the member sees is Cairo time, whatever time zone
   the phone or laptop is in (docs/LEAGUE_BUILD_SPEC.md section 18.4). The server stores UTC and does the real day maths
   (Services/LeagueDayRules.cs); this file only formats for display and fills <input type="datetime-local">.
   Exposes window.LeagueTime = { zone, format(iso), inputValue(date) }. Nothing here touches the page. */
(function (root) {
    'use strict';

    var ZONE = 'Africa/Cairo';

    // Parts of a date as Cairo wall-clock numbers. null when this browser has no time-zone support (then callers fall
    // back to the device's own time, which is wrong only for someone outside Cairo).
    function cairoParts(date) {
        try {
            var f = new Intl.DateTimeFormat('en-GB', {
                timeZone: ZONE, year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
            });
            var o = {};
            f.formatToParts(date).forEach(function (p) { o[p.type] = p.value; });
            return o;
        } catch (e) {
            return null;
        }
    }

    // "16 Oct 2026, 21:05" for an ISO string; '' when it is not a date.
    function format(iso) {
        var d = new Date(iso);
        if (isNaN(d.getTime())) return '';
        var opts = { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' };
        try {
            return d.toLocaleString('en-GB', Object.assign({ timeZone: ZONE, hourCycle: 'h23' }, opts));
        } catch (e) {
            return d.toLocaleString([], opts);
        }
    }

    // "2026-10-16T21:05": what a datetime-local input expects, in Cairo wall-clock time.
    function inputValue(date) {
        var p = cairoParts(date);
        if (!p) {
            var z = function (n) { return (n < 10 ? '0' : '') + n; };
            return date.getFullYear() + '-' + z(date.getMonth() + 1) + '-' + z(date.getDate()) + 'T' + z(date.getHours()) + ':' + z(date.getMinutes());
        }
        return p.year + '-' + p.month + '-' + p.day + 'T' + p.hour + ':' + p.minute;
    }

    root.LeagueTime = { zone: ZONE, format: format, inputValue: inputValue };
})(window);
