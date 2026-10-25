/* League Clash (Step 6c): shows clash times in the viewer's own time zone, and sends the picked time to the server
   as UTC. Without this script the times stay in UTC (the text the server rendered) and the form is read as UTC. */
(function () {
    'use strict';

    function pad(n) { return n < 10 ? '0' + n : String(n); }

    function localInputValue(d) {
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }

    function localizeTimes() {
        var list = document.querySelectorAll('time[data-lgc-time]');
        for (var i = 0; i < list.length; i++) {
            var el = list[i];
            var d = new Date(el.getAttribute('datetime'));
            if (isNaN(d.getTime())) continue;
            if (!el.title) el.title = el.textContent;
            var opts = el.hasAttribute('data-lgc-long')
                ? { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' }
                : { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' };
            el.textContent = d.toLocaleString([], opts);
        }
    }

    function wireForms() {
        var forms = document.querySelectorAll('form[data-lgc-form]');
        for (var i = 0; i < forms.length; i++) {
            (function (form) {
                var local = form.querySelector('[data-lgc-local]');
                var utc = form.querySelector('[data-lgc-utc]');
                var tz = form.querySelector('[data-lgc-tz]');
                if (!local || !utc) return;
                if (tz) tz.textContent = '(your time zone)';

                // Edit page: the server put the UTC time in the box as a fallback (and the exact time in data-lgc-iso). Show it as local time instead.
                var iso = utc.getAttribute('data-lgc-iso');
                if (iso) {
                    var d0 = new Date(iso);
                    if (!isNaN(d0.getTime())) local.value = localInputValue(d0);
                }

                function sync() {
                    if (!local.value) { utc.value = ''; return; }
                    var d = new Date(local.value);   // a datetime-local value is read as local time
                    utc.value = isNaN(d.getTime()) ? '' : d.toISOString();
                }
                local.addEventListener('input', sync);
                local.addEventListener('change', sync);
                form.addEventListener('submit', sync);
                sync();
            })(forms[i]);
        }
    }

    localizeTimes();
    wireForms();
})();
