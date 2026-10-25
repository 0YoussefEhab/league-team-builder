/* League Discord auto-send after Finish (Step 12h2).
   Used by league-builder.js (a finished custom game) and league-clash-board.js (a finished Clash game). After the server has saved the
   result, the page calls LeagueAutoSend.gameResult(matchId, notify) or LeagueAutoSend.clashProgress(eventId, teamId, gameId, notify):
   it fetches the picture data (GET /LeagueExport/Render), draws it with LeagueResultImage.render and posts it with
   LeagueResultImage.sendCanvas (POST /LeagueExport/Send). Nothing happens unless the page has <div id="lri-auto"> (Views/Shared/_LeagueSendConfig:
   webhook set, member has LeagueExport > Send and Render, and the matching switch is on in League settings > Discord auto-send).
   A failure never undoes Finish: notify(text) says the result is saved and the picture was not sent. The tab must stay open until it is sent,
   so a leave-the-page warning shows while a picture is on its way. One picture per game per tab (the same game is never sent twice).
   Exposes window.LeagueAutoSend = { enabled, gameResult, clashProgress, pending }. */
(function (root) {
    'use strict';

    var sentKeys = {};
    var inFlight = 0;

    function el() { return typeof document === 'undefined' ? null : document.getElementById('lri-auto'); }

    function enabled(kind) {
        var a = el();
        if (!a || !root.LeagueResultImage || !root.LeagueResultImage.sendUrl() || typeof root.html2canvas !== 'function') return false;
        return a.getAttribute(kind === 'clash' ? 'data-clash' : 'data-game') === '1';
    }

    function warn(e) {
        if (!inFlight) return undefined;
        e.preventDefault();
        e.returnValue = 'A picture is still being sent to Discord.';
        return e.returnValue;
    }
    if (typeof window !== 'undefined') window.addEventListener('beforeunload', warn);

    function fetchSpec(query) {
        var a = el();
        var base = a.getAttribute('data-render');
        return fetch(base + (base.indexOf('?') < 0 ? '?' : '&') + query, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) {
                if (!r.ok) throw new Error(r.status === 403 ? 'You do not have permission to make this picture.' : 'The server could not make the picture data.');
                return r.json();
            })
            .then(function (d) {
                if (!d || !d.ok || !d.spec) throw new Error((d && d.error) || 'The picture data was empty.');
                return d.spec;
            });
    }

    function run(key, query, notify) {
        if (sentKeys[key]) return Promise.resolve(false);
        sentKeys[key] = true;
        inFlight++;
        notify('Result saved. Sending the picture to Discord... keep this tab open.');
        return fetchSpec(query)
            .then(function (spec) {
                return root.LeagueResultImage.render(spec).then(function (c) { return root.LeagueResultImage.sendCanvas(c, spec); });
            })
            .then(function () {
                inFlight--;
                notify('Picture sent to Discord.');
                return true;
            }, function (e) {
                inFlight--;
                delete sentKeys[key];   // a later try (same game) is allowed after a failure
                notify('Result saved, but the picture was not sent to Discord: ' + ((e && e.message) || 'unknown error') + ' Use Send to Discord in History.');
                return false;
            });
    }

    function gameResult(matchId, notify) {
        if (!enabled('game') || !matchId) return Promise.resolve(false);
        return run('result:' + matchId, 'kind=result&matchId=' + encodeURIComponent(matchId), notify || function () {});
    }

    function clashProgress(eventId, teamId, gameId, notify) {
        if (!enabled('clash') || !eventId) return Promise.resolve(false);
        var q = 'kind=clash&eventId=' + encodeURIComponent(eventId) + (teamId !== null && teamId !== undefined ? '&teamId=' + encodeURIComponent(teamId) : '');
        return run('clash:' + (gameId || (eventId + ':' + teamId)), q, notify || function () {});
    }

    root.LeagueAutoSend = { enabled: enabled, gameResult: gameResult, clashProgress: clashProgress, pending: function () { return inFlight; } };
    if (typeof module !== 'undefined' && module.exports) module.exports = root.LeagueAutoSend;
})(typeof window !== 'undefined' ? window : globalThis);
