/* Leaderboard (Step 8d). The page works without this file: the form is a normal GET form with a Search button.
   This only submits the form when a filter changes (rank chip, Members/Guests, lane, sort). Typing in the search
   box waits for Enter or the Search button, so the page never reloads while someone is typing.

   Leaderboard step L2: the sort dropdown always holds a value, so a plain submit would always send `sort=...` and a rank
   click on the default Score list would send `sort=score`, breaking the rule "rank picked and no sort chosen = Rank order"
   (LeagueLeaderboardRules.DefaultSort). So the dropdown only goes into the address when the person chose a sort: either the
   address already carried one (data-chosen="1", set by the view) or they changed the dropdown themselves. A chosen sort always wins.
   Without JavaScript the dropdown value is always sent (it counts as chosen). */
(function () {
    'use strict';
    var form = document.getElementById('lg-lb-form');
    if (!form) return;
    var sel = form.querySelector('select[name="sort"]');
    var chosen = !!sel && sel.getAttribute('data-chosen') === '1';
    if (sel) sel.addEventListener('change', function () { chosen = true; });
    // Leave `sort` out of the address while it was never chosen (the server then picks the default for the rank filter).
    form.addEventListener('formdata', function (e) {
        if (!chosen && e.formData && typeof e.formData.delete === 'function') e.formData.delete('sort');
    });
    // Coming back with the Back button keeps the page in memory: start again from what the address says.
    window.addEventListener('pageshow', function (e) {
        if (e.persisted && sel) chosen = sel.getAttribute('data-chosen') === '1';
    });
    form.addEventListener('change', function (e) {
        var t = e.target;
        if (!t || t.name === 'q') return;
        if (typeof form.requestSubmit === 'function') form.requestSubmit();
        else form.submit();
    });
})();
