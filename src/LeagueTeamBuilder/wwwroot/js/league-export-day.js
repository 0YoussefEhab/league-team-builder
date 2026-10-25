/* League "Export matches" dialog on History (Results/export step 12f, spec section 18.4-18.6).
   Three buttons above the filters (data-lgx-open="today" | "yesterday" | "pick") open one dialog: a day (Today, Yesterday or a date),
   the day's finished custom games as a checklist (all ticked at first, tags "Added later" and "Missing KDA"), a note with the 70% rule,
   the picture type (Session image / Each game image) and an Export button. The server decides the 70% rule and the default type
   (GET /League/ExportData, see Services/LeagueExportDayRules.cs); the pictures are drawn in the browser with league-result-image.js,
   one under the other, each with Download PNG and Copy image, plus Download all (several PNG downloads in a row, not a zip).
   Config: the JSON in #lgh-data = { exportUrl, todayKey, yesterdayKey }. Needs league-result-image.js (window.LeagueResultImage). */
(function () {
    'use strict';

    var cfgEl = document.getElementById('lgh-data');
    var cfg = {};
    try { cfg = cfgEl ? JSON.parse(cfgEl.textContent) : {}; } catch (e) { cfg = {}; }
    if (!cfg.exportUrl) return;

    var dlg = null, els = {};
    var state = null;   // { day, rows, ticked: {id:true}, note, serverMode, userMode, loading, token, pictures }

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function ensureDialog() {
        if (dlg) return;
        dlg = document.createElement('dialog');
        dlg.className = 'lri-dlg lgx-dlg';
        dlg.setAttribute('aria-labelledby', 'lgx-title');
        dlg.innerHTML = '<div class="lri-dlg-in">' +
            '<h3 id="lgx-title">Export matches</h3>' +
            '<p class="lri-hint">Pictures of a day\'s finished custom games for Discord. They are drawn in your browser; nothing is sent anywhere.</p>' +
            '<div class="lgx-days">' +
            '<button type="button" class="lgx-chip" data-lgx-day="today">Today</button>' +
            '<button type="button" class="lgx-chip" data-lgx-day="yesterday">Yesterday</button>' +
            '<label class="lgx-pick">Pick a day <input type="date" class="lg-input" data-lgx-date></label>' +
            '</div>' +
            '<p class="lgx-note" role="status" aria-live="polite" data-lgx-note></p>' +
            '<div class="lgx-list" data-lgx-list></div>' +
            '<div class="lgx-modes" data-lgx-modes hidden>' +
            '<button type="button" class="lgx-chip" data-lgx-mode="session">Session image</button>' +
            '<button type="button" class="lgx-chip" data-lgx-mode="games">Each game image</button>' +
            '</div>' +
            '<p class="lri-msg" role="status" aria-live="polite" data-lgx-msg></p>' +
            '<div class="lri-acts"><button type="button" class="lg-btn lg-btn-ghost" data-lgx-close>Close</button>' +
            '<button type="button" class="lg-btn lg-btn-ghost" data-lgx-sendall hidden>Send all to Discord</button>' +
            '<button type="button" class="lg-btn lg-btn-ghost" data-lgx-all hidden>Download all</button>' +
            '<button type="button" class="lg-btn" data-lgx-export disabled>Export</button></div>' +
            '<div class="lgx-out" data-lgx-out></div>' +
            '</div>';
        document.body.appendChild(dlg);
        els.date = dlg.querySelector('[data-lgx-date]');
        els.note = dlg.querySelector('[data-lgx-note]');
        els.list = dlg.querySelector('[data-lgx-list]');
        els.modes = dlg.querySelector('[data-lgx-modes]');
        els.msg = dlg.querySelector('[data-lgx-msg]');
        els.all = dlg.querySelector('[data-lgx-all]');
        els.sendAll = dlg.querySelector('[data-lgx-sendall]');
        els.exp = dlg.querySelector('[data-lgx-export]');
        els.out = dlg.querySelector('[data-lgx-out]');
        els.chips = dlg.querySelectorAll('[data-lgx-day]');

        dlg.addEventListener('click', function (e) {
            if (e.target === dlg) { dlg.close(); return; }
            var t = e.target.closest ? e.target : null;
            if (!t) return;
            if (t.closest('[data-lgx-close]')) { dlg.close(); return; }
            var day = t.closest('[data-lgx-day]');
            if (day) { loadDay(day.getAttribute('data-lgx-day') === 'today' ? cfg.todayKey : cfg.yesterdayKey); return; }
            var mode = t.closest('[data-lgx-mode]');
            if (mode) { state.userMode = mode.getAttribute('data-lgx-mode'); paintModes(); return; }
            if (t.closest('[data-lgx-export]')) { runExport(); return; }
            if (t.closest('[data-lgx-all]')) { downloadAll(); return; }
            if (t.closest('[data-lgx-sendall]')) { sendAll(); return; }
        });
        els.date.addEventListener('change', function () { if (els.date.value) loadDay(els.date.value); });
        els.list.addEventListener('change', function (e) {
            var box = e.target;
            if (!box || !box.getAttribute || box.getAttribute('data-lgx-id') == null) return;
            if (box.checked) state.ticked[box.getAttribute('data-lgx-id')] = true; else delete state.ticked[box.getAttribute('data-lgx-id')];
            tickChanged();
        });
        dlg.addEventListener('close', function () { if (state) state.token++; });
    }

    function setMsg(text, isErr) {
        els.msg.textContent = text || '';
        els.msg.classList.toggle('is-err', !!isErr);
    }

    function tickedIds() {
        return state.rows.filter(function (r) { return state.ticked[r.id]; }).map(function (r) { return r.id; });
    }

    function effectiveMode() { return state.userMode || state.serverMode || 'games'; }

    function clearOutput() {
        els.out.innerHTML = '';
        els.all.hidden = true;
        els.sendAll.hidden = true;
        state.pictures = [];
    }

    function paintChips(key) {
        els.chips.forEach(function (c) {
            var k = c.getAttribute('data-lgx-day') === 'today' ? cfg.todayKey : cfg.yesterdayKey;
            c.classList.toggle('is-on', !!key && k === key);
        });
    }

    function paintModes() {
        var show = !!state.day && tickedIds().length > 0;
        els.modes.hidden = !show;
        var mode = effectiveMode();
        els.modes.querySelectorAll('[data-lgx-mode]').forEach(function (b) {
            b.classList.toggle('is-on', b.getAttribute('data-lgx-mode') === mode);
        });
    }

    function paintList() {
        if (!state.day) { els.list.innerHTML = '<p class="lgx-empty">Pick a day above.</p>'; return; }
        if (!state.rows.length) { els.list.innerHTML = '<p class="lgx-empty">No matches on this day</p>'; return; }
        els.list.innerHTML = state.rows.map(function (r) {
            var tags = (r.addedLater ? '<em>Added later</em>' : '') + (r.missingKda ? '<em class="lgx-warn">Missing KDA</em>' : '');
            return '<label class="lgx-row"><input type="checkbox" data-lgx-id="' + r.id + '"' + (state.ticked[r.id] ? ' checked' : '') + '>' +
                '<span class="lgx-t"><b>Game ' + r.number + ' · ' + esc(r.modeName) + '</b><small>' + esc(r.timeText) + ' · ' + esc(r.winnerText) + '</small></span>' +
                '<span class="lgx-tags">' + tags + '</span></label>';
        }).join('');
    }

    function paintAll() {
        paintList();
        els.note.textContent = state.note || '';
        paintModes();
        els.exp.disabled = state.loading || tickedIds().length === 0;
    }

    function fetchJson(url) {
        return fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } }).then(function (r) {
            if (!r.ok) throw new Error(r.status === 403 ? 'You do not have permission to export matches.' : 'The server could not answer. Try again.');
            return r.json();
        });
    }

    function urlFor(day, ids, mode) {
        var u = cfg.exportUrl + (cfg.exportUrl.indexOf('?') < 0 ? '?' : '&') + 'day=' + encodeURIComponent(day);
        if (ids && ids.length) u += '&ids=' + ids.join(',');
        if (mode) u += '&mode=' + mode;
        return u;
    }

    // A new day: ask for its checklist (the server also sends the overlap of all its games, which are all ticked at first).
    function loadDay(key) {
        if (!key) return;
        state.day = key;
        state.rows = [];
        state.ticked = {};
        state.note = 'Loading…';
        state.userMode = null;
        state.serverMode = null;
        state.loading = true;
        clearOutput();
        setMsg('', false);
        els.date.value = key;
        paintChips(key);
        paintAll();
        var mine = ++state.token;
        fetchJson(urlFor(key)).then(function (d) {
            if (mine !== state.token) return;
            state.loading = false;
            if (!d || !d.ok) { state.note = ''; paintAll(); setMsg((d && d.error) || 'The day could not be loaded.', true); return; }
            state.rows = d.rows || [];
            state.rows.forEach(function (r) { state.ticked[r.id] = true; });
            state.note = d.message || '';
            state.serverMode = d.defaultMode;
            paintAll();
        }).catch(function (e) {
            if (mine !== state.token) return;
            state.loading = false;
            state.note = '';
            paintAll();
            setMsg(e.message, true);
        });
    }

    // A box was ticked or unticked: the server works out the overlap again (the dialog never decides it itself).
    function tickChanged() {
        clearOutput();
        setMsg('', false);
        var ids = tickedIds();
        if (!ids.length) { state.note = 'Tick at least one match.'; paintAll(); return; }
        var mine = ++state.token;
        fetchJson(urlFor(state.day, ids)).then(function (d) {
            if (mine !== state.token) return;
            if (!d || !d.ok) { setMsg((d && d.error) || 'The matches could not be checked.', true); return; }
            state.note = d.message || '';
            state.serverMode = d.defaultMode;
            paintAll();
        }).catch(function (e) { if (mine === state.token) setMsg(e.message, true); });
        state.note = 'Checking the players…';
        paintAll();
    }

    function captionFor(spec, i, total) {
        if (spec.kind === 'session') {
            return 'Session picture' + (total > 1 ? ' ' + (i + 1) + ' of ' + total : '') + ' · ' + LeagueResultImage.rangeText(spec);
        }
        return (spec.title || 'Game') + ' · ' + spec.date;
    }

    function addPicture(spec, canvas, i, total) {
        var box = document.createElement('div');
        box.className = 'lgx-res';
        var cap = document.createElement('div');
        cap.className = 'lgx-cap';
        var label = document.createElement('b');
        label.textContent = captionFor(spec, i, total);
        var info = document.createElement('span');
        info.className = 'lri-msg';
        var acts = document.createElement('div');
        acts.className = 'lri-acts';
        var copy = document.createElement('button');
        copy.type = 'button'; copy.className = 'lg-btn lg-btn-ghost'; copy.textContent = 'Copy image';
        var dl = document.createElement('button');
        dl.type = 'button'; dl.className = 'lg-btn'; dl.textContent = 'Download PNG';
        copy.addEventListener('click', function () {
            LeagueResultImage.copyCanvas(canvas).then(function () { info.classList.remove('is-err'); info.textContent = 'Copied. Paste it into Discord.'; })
                .catch(function (e) { info.classList.add('is-err'); info.textContent = e.message; });
        });
        dl.addEventListener('click', function () {
            LeagueResultImage.saveCanvas(canvas, spec).then(function () { info.classList.remove('is-err'); info.textContent = 'Downloaded.'; })
                .catch(function (e) { info.classList.add('is-err'); info.textContent = e.message; });
        });
        acts.appendChild(info); acts.appendChild(copy);
        if (LeagueResultImage.sendUrl()) {
            var snd = document.createElement('button');
            snd.type = 'button'; snd.className = 'lg-btn lg-btn-ghost'; snd.textContent = 'Send to Discord';
            snd.addEventListener('click', function () {
                snd.disabled = true;
                info.classList.remove('is-err'); info.textContent = 'Sending…';
                LeagueResultImage.sendCanvas(canvas, spec).then(function () { info.textContent = 'Sent to Discord.'; })
                    .catch(function (e) { info.classList.add('is-err'); info.textContent = e.message; })
                    .then(function () { snd.disabled = false; });
            });
            acts.appendChild(snd);
        }
        acts.appendChild(dl);
        cap.appendChild(label); cap.appendChild(acts);
        box.appendChild(cap);
        box.appendChild(canvas);
        els.out.appendChild(box);
        state.pictures.push({ spec: spec, canvas: canvas });
    }

    // Draw the specs one after the other (html2canvas is heavy; one at a time keeps the page responsive).
    function drawAll(specs, mine) {
        var total = specs.length;
        var chain = Promise.resolve();
        specs.forEach(function (spec, i) {
            chain = chain.then(function () {
                if (mine !== state.token) return null;
                setMsg('Drawing picture ' + (i + 1) + ' of ' + total + '…', false);
                return LeagueResultImage.render(spec).then(function (c) {
                    if (mine !== state.token) return;
                    addPicture(spec, c, i, total);
                });
            });
        });
        return chain;
    }

    function runExport() {
        if (!state || state.loading) return;
        var ids = tickedIds();
        if (!ids.length) return;
        clearOutput();
        state.loading = true;
        paintAll();
        setMsg('Getting the matches…', false);
        var mine = ++state.token;
        var mode = effectiveMode();
        fetchJson(urlFor(state.day, ids, mode)).then(function (d) {
            if (mine !== state.token) return null;
            if (!d || !d.ok) throw new Error((d && d.error) || 'The pictures could not be made.');
            var specs = mode === 'session' ? (d.sessions || []) : (d.games || []);
            if (!specs.length) throw new Error('There was nothing to draw.');
            return drawAll(specs, mine);
        }).then(function () {
            if (mine !== state.token) return;
            state.loading = false;
            paintAll();
            var n = state.pictures.length;
            setMsg(n ? (n === 1 ? 'Done. 1 picture.' : 'Done. ' + n + ' pictures.') : '', false);
            els.all.hidden = n < 2;
            els.sendAll.hidden = n < 2 || !LeagueResultImage.sendUrl();
        }).catch(function (e) {
            if (mine !== state.token) return;
            state.loading = false;
            paintAll();
            els.all.hidden = state.pictures.length < 2;
            els.sendAll.hidden = state.pictures.length < 2 || !LeagueResultImage.sendUrl();
            setMsg((e && e.message) || 'The pictures could not be made.', true);
        });
    }

    // Several PNG downloads in a row (not a zip). The browser may ask once to allow several downloads.
    function downloadAll() {
        var list = state.pictures.slice();
        var chain = Promise.resolve();
        list.forEach(function (p) {
            chain = chain.then(function () {
                return LeagueResultImage.saveCanvas(p.canvas, p.spec).then(function () {
                    return new Promise(function (r) { setTimeout(r, 400); });
                });
            });
        });
        chain.then(function () { setMsg('Downloaded ' + list.length + ' pictures.', false); })
            .catch(function (e) { setMsg(e.message, true); });
    }

    // One picture after the other with a pause (Discord allows only a few webhook posts every couple of seconds). Stops at the first failure.
    function sendAll() {
        var list = state.pictures.slice();
        var done = 0;
        els.sendAll.disabled = true;
        var chain = Promise.resolve();
        list.forEach(function (p, i) {
            chain = chain.then(function () {
                setMsg('Sending picture ' + (i + 1) + ' of ' + list.length + '…', false);
                return LeagueResultImage.sendCanvas(p.canvas, p.spec).then(function () {
                    done++;
                    return new Promise(function (r) { setTimeout(r, 1200); });
                });
            });
        });
        chain.then(function () { setMsg('Sent ' + done + ' pictures to Discord.', false); })
            .catch(function (e) { setMsg('Sent ' + done + ' of ' + list.length + '. ' + e.message, true); })
            .then(function () { els.sendAll.disabled = false; });
    }

    function open(which) {
        ensureDialog();
        state = { day: null, rows: [], ticked: {}, note: '', serverMode: null, userMode: null, loading: false, token: (state ? state.token : 0) + 1, pictures: [] };
        clearOutput();
        setMsg('', false);
        els.date.value = '';
        paintChips(null);
        paintAll();
        if (!dlg.open) dlg.showModal();
        if (which === 'today') loadDay(cfg.todayKey);
        else if (which === 'yesterday') loadDay(cfg.yesterdayKey);
        else els.date.focus();
    }

    document.addEventListener('click', function (e) {
        var b = e.target.closest && e.target.closest('[data-lgx-open]');
        if (!b || b.disabled) return;
        if (typeof window.LeagueResultImage === 'undefined') return;
        open(b.getAttribute('data-lgx-open'));
    });
})();
