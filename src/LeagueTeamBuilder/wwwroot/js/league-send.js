/* League "Send to Discord" sender (PLAN-leaderboard-export.md, Part 2, step E1).
   One place that posts a drawn 1280 x 720 PNG to the League Discord channel: POST /LeagueExport/Send (multipart, field "file")
   with the anti-forgery token. league-result-image.js (History, match page, Clash and Builder pictures) and the Export for Discord
   dialogs of the Builder and Clash use it, so nobody copies this code.
   The page carries <div id="lri-send" data-url="..."> only when the webhook is set and the member has LeagueExport > Send
   (Views/Shared/_LeagueSendConfig.cshtml), so no element = no button.
   Exposes window.LeagueSend = { url, canSend, sendBlob, sendCanvas }. Every send resolves with nothing or rejects with a message
   the member can read. */
(function (root) {
    'use strict';

    function url() {
        var el = document.getElementById('lri-send');
        return el ? el.getAttribute('data-url') : null;
    }

    function canSend() { return !!url(); }

    function canvasBlob(c) {
        return new Promise(function (resolve, reject) {
            try {
                c.toBlob(function (b) { b ? resolve(b) : reject(new Error('The picture could not be made.')); }, 'image/png');
            } catch (e) {
                // a canvas that used a picture from another site without permission refuses to export
                reject(new Error('The picture could not be made.'));
            }
        });
    }

    function sendBlob(blob, fileName) {
        var target = url();
        if (!target) return Promise.reject(new Error('Sending to Discord is not set up.'));
        var fd = new FormData();
        fd.append('file', blob, fileName || 'hfe-league.png');
        var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
        return fetch(target, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'RequestVerificationToken': tokenEl ? tokenEl.value : '', 'Accept': 'application/json' },
            body: fd
        }).then(function (r) {
            if (!r.ok) throw new Error(r.status === 403 ? 'You do not have permission to send pictures to Discord.' : 'The server could not send the picture.');
            return r.json();
        }).then(function (d) {
            if (!d || !d.ok) throw new Error((d && d.error) || 'Discord did not accept the picture.');
        });
    }

    function sendCanvas(c, fileName) {
        if (!url()) return Promise.reject(new Error('Sending to Discord is not set up.'));
        return canvasBlob(c).then(function (blob) { return sendBlob(blob, fileName); });
    }

    var api = { url: url, canSend: canSend, sendBlob: sendBlob, sendCanvas: sendCanvas };
    root.LeagueSend = api;
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : (typeof globalThis !== 'undefined' ? globalThis : this));
