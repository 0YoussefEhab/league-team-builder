/* League Clash page (Step 6d). The server builds and checks the team (POST /LeagueClash/Build); this file only renders and
   does the by-hand edits: mark someone busy for this session, swap two slots, put a bench player into a slot, empty a slot.
   The busy toggles on a member are never saved: the member's own answer is not changed. Step 8i: the Busy button on a GUEST row
   (only for LeagueClash > SetGuestBusy) saves the guest's answer at once (POST /LeagueClash/SetGuestBusy). Every edit to the team is re-checked by
   the server (strength, lane fit). Everything is built with DOM calls and textContent, so a player name can never inject
   markup (same rule as league-builder.js).
   Step 6e3 adds the Games tile: Save game (POST /LeagueMatch/Submit with the clash id), Start and Finish (win or loss).
   Those three use the LeagueMatch endpoints; the server decides everything (game number, cap, one unfinished game).
   Step 6e4b adds End clash (POST /LeagueClash/EndClash, answers { ok, error, message }): a button under the games list,
   a confirm dialog, then the page shows the clash as Done. The server refuses it while a game is unfinished.
   Step 6e4c adds the 4-game hint on the Games tile. It is only a message: the cap stays hard on the server.
   Step 6e4e adds Next game (POST /LeagueClash/NextGame, answers { ok, error, nextNumber, fromGame, team }): it loads the
   last finished game's team on screen. Nothing is saved until Save as game N.
   Step 6e6b adds Export for Discord: the picture is drawn in the browser by league-export.js from exportSpec() (the team on
   screen right now), shown in a dialog with Copy image and Download PNG. E4 (PLAN-leaderboard-export.md): the dialog also has a
   With the map / The list choice and Send to Discord (league-send.js); nothing is saved on the server.
   Step 11a: the page no longer builds a team when it opens. It starts with five empty slots (like the Custom game page); you pick
   the players by hand and the Set best team button does the old auto build. Marking someone busy no longer rebuilds: it takes
   that player off the team (with a message). Changing Pick by only rechecks the strength of the team on screen.
   Step 11d: a clash has teams (the card row under the clash drop-down). Each team keeps its own lineup in the browser while you
   switch (since 11h it is also kept in the browser). Teams are created, renamed and deleted with
   POST /LeagueClash/CreateTeam, RenameTeam, DeleteTeam (11c). The Games card, Save as game N and Next game work on the team on
   screen; the busy list and Pick by are shared by all teams. The series record and the End clash rule still count every team.
   Step 11g: one team per player. A player who is in another team's lineup (on this page) or in another team's unfinished game is
   off the bench here, marked "On <team>" in Who can play, and sent to the server as busy for Set best team and Next game; the
   server also refuses to save a game with such a player.
   Step 11h: auto save. A moment after every change the lineup of each team, the busy toggles that differ from the saved answers,
   the team on screen and Pick by are kept in this browser (localStorage, one entry per clash). They are put back when the page opens
   (with a note) if the clash is not Done and the entry is under two days old. A team's lineup that was just saved as a game is not
   kept. If the browser blocks storage nothing is kept and everything else works as before. Nothing is sent to the server. */
(function () {
    'use strict';

    var dataEl = document.getElementById('lgcb-data');
    var app = document.getElementById('lgcb-app');
    var dlg = document.getElementById('lgcb-dlg');
    var toastEl = document.getElementById('lgb-toast');
    if (!dataEl || !app) return;

    var cfg;
    try { cfg = JSON.parse(dataEl.textContent); } catch (e) { return; }

    var eventSel = document.querySelector('[data-lgcb-event]');
    if (eventSel) eventSel.addEventListener('change', function () { if (eventSel.form) eventSel.form.submit(); });

    var LANES = cfg.lanes || [];
    var ARAM = cfg.mode === 'aram';
    var P = {};
    (cfg.players || []).forEach(function (p) { P[p.id] = p; });

    // The busy set the page starts from: players who answered Busy (a member's own answer, or a guest's answer saved by a moderator).
    function answeredBusy() {
        return (cfg.answeredBusy || []).filter(function (id) { return P[id]; });
    }
    var answeredBusySet = new Set(answeredBusy());
    var answeredFreeSet = new Set(cfg.answeredFree || []);

    // Step 11a: the team the page starts with: five empty slots (slot i is lane i in Normal, no lane in ARAM).
    function emptyResult() {
        var slots = [];
        for (var i = 0; i < 5; i++) slots.push({ lane: ARAM ? null : i, playerId: null, manual: false });
        return { ok: true, mode: cfg.mode, isEmpty: true, strength: 0, slots: slots, warnings: [] };
    }

    var state = {
        busy: new Set(answeredBusy()),
        system: 'rank',          // 'rank' | 'tier'
        result: emptyResult(),   // team on screen: starts empty (11a); later the last server response (slots, strength, ...)
        noneFree: false,         // 11a: the last Set best team found nobody free
        log: [],                 // log of the last build
        noFit: [],               // no-fit entries of the last build, cleared by a hand edit
        warnings: [],
        sel: null,               // { kind: 'slot', i } or { kind: 'bench', id }
        edited: false,           // true after a hand edit: a rebuild would lose it
        fromGame: 0,             // 6e4e: the game number the team on screen was copied from, 0 when it was built
        loading: false,
        error: '',
        filter: '',
        games: (cfg.games || []).slice(),   // saved games of this clash: { id, number, status, winner }
        eventStatus: cfg.eventStatus || 'open',
        acting: false,                      // a Save / Start / Finish / End call is running
        teams: (cfg.teams || []).map(function (t) { return { id: t.id, name: t.name }; }),   // 11d: the teams of this clash
        activeTeam: cfg.teams && cfg.teams.length ? cfg.teams[0].id : null
    };
    var teamStore = {};          // 11d: team id -> the lineup of a team that is not on screen right now
    var teamsMount = document.getElementById('lgcb-teams');
    var teamFocus = null;
    var seq = 0;
    var toastTimer = null;
    var focusKey = null;

    /* ---------- small helpers ---------- */

    function h(tag, attrs, kids) {
        var el = document.createElement(tag);
        if (attrs) {
            Object.keys(attrs).forEach(function (k) {
                var v = attrs[k];
                if (v === null || v === undefined || v === false) return;
                if (k === 'class') el.className = v;
                else if (k === 'text') el.textContent = v;
                else if (k.slice(0, 2) === 'on') el.addEventListener(k.slice(2), v);
                else if (v === true) el.setAttribute(k, '');
                else el.setAttribute(k, v);
            });
        }
        (kids || []).forEach(function (c) {
            if (c === null || c === undefined || c === false) return;
            el.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
        });
        return el;
    }


    // Step 8g: the two badges as small icons (the #1 rank crown and the MVP star). Same look as the _LeagueBadges partial.
    function badgeEl(kind) {
        var NS = 'http://www.w3.org/2000/svg';
        var svg = document.createElementNS(NS, 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('fill', 'currentColor');
        svg.setAttribute('aria-hidden', 'true');
        var path = document.createElementNS(NS, 'path');
        if (kind === 'rank') {
            path.setAttribute('d', 'M3 18l-1-11 5 4 5-8 5 8 5-4-1 11z');
            svg.appendChild(path);
            var rect = document.createElementNS(NS, 'rect');
            rect.setAttribute('x', '3'); rect.setAttribute('y', '19.5'); rect.setAttribute('width', '18');
            rect.setAttribute('height', '2'); rect.setAttribute('rx', '1');
            svg.appendChild(rect);
        } else if (kind === 'clash') {
            path.setAttribute('d', 'M7 3h10v5a5 5 0 0 1-10 0zM5 4H2v2a4 4 0 0 0 4 4zM19 4h3v2a4 4 0 0 1-4 4zM11 13h2v4h3v2H8v-2h3z');
            svg.appendChild(path);
        } else {
            path.setAttribute('d', 'M12 2l2.9 6.6 7.1.6-5.4 4.7 1.6 7-6.2-3.7L5.8 21l1.6-7L2 9.2l7.1-.6z');
            svg.appendChild(path);
        }
        var text = kind === 'rank' ? '#1 ranked player in the server' : kind === 'clash' ? 'Won the latest Clash' : 'Most MVPs in the server';
        return h('span', { class: 'lg-bd lg-bd-icon lg-bd-' + kind, title: text, role: 'img', 'aria-label': text }, [svg]);
    }

    function cssUrl(u) {
        return 'url("' + String(u).replace(/["\\\n\r]/g, encodeURIComponent) + '")';
    }

    function ini(name) {
        var parts = String(name || '').trim().split(/\s+/).filter(Boolean);
        if (!parts.length) return '?';
        var a = Array.from(parts[0]);
        var b = parts.length > 1 ? Array.from(parts[1]) : [];
        return (a[0] + (b.length ? b[0] : (a[1] || ''))).toUpperCase();
    }

    function laneLabel(i) { return LANES[i] || ('Lane ' + (i + 1)); }

    function toast(msg, ms) {
        if (!toastEl) return;
        toastEl.textContent = msg;
        toastEl.classList.add('is-on');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { toastEl.classList.remove('is-on'); }, ms || 2400);
    }

    function rankIcon(key) {
        return h('img', { class: 'lgb-ri', alt: '', height: '18', src: (cfg.iconBase || '') + key + '.png' });
    }

    function avatar(p, cls) {
        var el = h('span', { class: cls || 'lgb-av', 'aria-hidden': 'true' });
        if (p.avatar) el.style.backgroundImage = cssUrl(p.avatar);
        else el.textContent = ini(p.name);
        return el;
    }

    // Step 11g: player id -> name of the other team they are on (in its lineup on this page, or in its unfinished game).
    function takenMap() {
        var map = {};
        Object.keys(teamStore).forEach(function (tid) {
            var id = Number(tid);
            if (id === state.activeTeam) return;
            var r = teamStore[tid].result;
            if (r && r.slots) r.slots.forEach(function (sl) { if (sl.playerId && !map[sl.playerId]) map[sl.playerId] = teamNameOf(id); });
        });
        state.games.forEach(function (g) {
            if (g.status === 'done' || gameTeam(g) === state.activeTeam) return;
            (g.playerIds || []).forEach(function (pid) { if (!map[pid]) map[pid] = teamNameOf(gameTeam(g)); });
        });
        return map;
    }

    // What the server is told is busy: the busy set plus everybody who is on another team.
    function busyList() {
        var set = new Set(state.busy);
        Object.keys(takenMap()).forEach(function (id) { set.add(Number(id)); });
        return Array.from(set);
    }

    function onTeam(id) {
        return !!(state.result && state.result.slots.some(function (s) { return s.playerId === id; }));
    }

    /* ---------- server calls ---------- */

    function post(body) { return postTo(cfg.buildUrl, body); }

    function postTo(url, body) {
        var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
        return fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': tokenEl ? tokenEl.value : '',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify(body)
        }).then(function (res) {
            var type = res.headers.get('content-type') || '';
            if (res.status === 400 && type.indexOf('json') < 0) {
                throw new Error('This page has expired. Reload it and try again.');
            }
            if (!res.ok || type.indexOf('json') < 0) {
                throw new Error('The server did not accept that. You may not have permission, or you were signed out. Reload the page.');
            }
            return res.json();
        });
    }

    function failMessage(err) {
        return (err && err.message) ? err.message : 'Something went wrong. Reload the page and try again.';
    }

    function build() {
        var mine = ++seq;
        state.loading = true;
        render();
        post({ eventId: cfg.eventId, op: 'build', rankSystem: state.system, busy: busyList() })
            .then(function (r) {
                if (mine !== seq) return;
                state.loading = false;
                if (!r.ok) { state.error = r.error || 'The team could not be built.'; render(); return; }
                state.error = '';
                state.result = r;
                state.noneFree = !!r.isEmpty;
                state.log = r.log || [];
                state.noFit = r.noFit || [];
                state.warnings = [];
                state.sel = null;
                state.edited = false;
                state.fromGame = 0;
                delete draftSkip[state.activeTeam];
                render();
                if (state.noFit.length) showNoFit();
            })
            .catch(function (err) {
                if (mine !== seq) return;
                state.loading = false;
                state.error = failMessage(err);
                render();
            });
    }

    // Recheck the team after a hand edit. The new slots are shown at once; if the server refuses, the old ones come back.
    // keep (11a) = true when the slots did not change by hand (Pick by changed): the edited flag stays as it was.
    function evaluate(newSlots, keep) {
        var before = state.result;
        var mine = ++seq;
        state.result = Object.assign({}, before, { slots: newSlots });
        if (!keep) state.edited = true;
        state.noneFree = false;
        state.noFit = [];
        state.sel = null;
        state.loading = true;
        render();
        post({
            eventId: cfg.eventId, op: 'evaluate', rankSystem: state.system, busy: busyList(),
            slots: newSlots.map(function (s) { return { lane: s.lane, playerId: s.playerId, manual: !!s.manual }; })
        }).then(function (r) {
            if (mine !== seq) return;
            state.loading = false;
            if (!r.ok) {
                state.result = before;
                state.error = r.error || 'That change was refused.';
                render();
                return;
            }
            state.error = '';
            state.result = Object.assign({}, before, { slots: r.slots, strength: r.strength, isEmpty: r.isEmpty });
            state.warnings = r.warnings || [];
            render();
        }).catch(function (err) {
            if (mine !== seq) return;
            state.loading = false;
            state.result = before;
            state.error = failMessage(err);
            render();
        });
    }

    /* ---------- games (6e3) ---------- */

    // 11d: a game belongs to a team; a game with no team (should not exist after the backfill) counts for the first team.
    function gameTeam(g) {
        if (g.teamId !== null && g.teamId !== undefined) return g.teamId;
        return state.teams.length ? state.teams[0].id : null;
    }

    function gamesOf(teamId) { return state.games.filter(function (g) { return gameTeam(g) === teamId; }); }

    function myGames() { return gamesOf(state.activeTeam); }

    function teamNameOf(id) {
        for (var i = 0; i < state.teams.length; i++) if (state.teams[i].id === id) return state.teams[i].name;
        return 'a team';
    }

    function activeTeamName() {
        for (var i = 0; i < state.teams.length; i++) if (state.teams[i].id === state.activeTeam) return state.teams[i].name;
        return ARAM ? 'ARAM team' : 'Clash team';
    }

    // The unfinished game of the team on screen: the one that blocks Save game and Next game.
    function unfinishedGame() {
        var mine = myGames();
        for (var i = 0; i < mine.length; i++) if (mine[i].status !== 'done') return mine[i];
        return null;
    }

    // Any team's unfinished game: End clash needs none.
    function anyUnfinishedGame() {
        for (var i = 0; i < state.games.length; i++) if (state.games[i].status !== 'done') return state.games[i];
        return null;
    }

    function teamHasPlayer() {
        return !!(state.result && state.result.slots.some(function (s) { return s.playerId; }));
    }

    // Why Save game is off, or '' when it can be pressed. The server checks all of this again.
    function saveBlock() {
        if (state.eventStatus === 'done') return 'This clash has ended.';
        if (!cfg.canSubmit) return 'You do not have permission to save games.';
        var open = unfinishedGame();
        if (open) return 'Finish game ' + open.number + ' before saving the next one.';
        if (capReached()) return 'A team has at most ' + (cfg.maxGames || 4) + ' games. End the clash when you are done.';
        if (!teamHasPlayer()) return 'Put at least one player on the team first.';
        return '';
    }

    function capReached() { return myGames().length >= (cfg.maxGames || 4); }

    // The soft message (6e4c) once the clash has its last game. The server refuses a fifth game on its own.
    function capHint() {
        var max = cfg.maxGames || 4;
        if (!capReached() || state.eventStatus === 'done') return '';
        var text = (state.teams.length > 1 ? activeTeamName() + ' has ' : 'This clash has ') + max + ' games, the most a team can have. ';
        if (unfinishedGame()) return text + 'Finish game ' + unfinishedGame().number + ', then end the clash.';
        return text + (cfg.canEnd ? 'Press End clash when you are done.' : 'Ask someone who can end clashes to end it.');
    }

    function nextNumber() {
        var max = 0;
        myGames().forEach(function (g) { if (g.number > max) max = g.number; });
        return max + 1;
    }

    /* ---------- Discord picture data (Step 6e6a; the button that uses it is 6e6b) ---------- */

    // The Map picture needs player places in the settings (Arena has none) and the drawMap of league-export.js.
    function hasMapPicture() {
        return !!(window.LeagueExport && typeof window.LeagueExport.drawMap === 'function' &&
            cfg.mode !== 'arena' && cfg.spots && cfg.spots.length);
    }

    // The spec league-export.js draws: { title, subtitle, mapUrl, teams }. One team (the team on screen, 11f), no Opponents.
    // Lane modes keep an "Open slot" row; ARAM just lists who is in. Returns null when nobody is on the team.
    // kind 'map' adds the places (mapAspect, spots) and per row slot, avatar and guest; with no kind it is the list picture.
    // On the map the team sits on the places of team 0 (the first team of the mode's settings); the other team's places are not drawn.
    function exportSpec(kind) {
        var r = state.result;
        if (!r || !r.slots || !r.slots.some(function (s) { return s.playerId; })) return null;
        var onMap = kind === 'map';
        var rows = [];
        r.slots.forEach(function (s, si) {
            var p = s.playerId ? P[s.playerId] : null;
            var hasLane = s.lane !== null && s.lane !== undefined;
            if (!p && !hasLane) return;
            rows.push({
                // On the map the "#n" is the slot number, like the pin on the Builder page; in the list it is the place in the list.
                tag: hasLane ? laneLabel(s.lane) : '#' + (onMap ? si + 1 : rows.length + 1),
                name: p ? p.name : null,
                sub: p ? p.curLabel : '',
                slot: si,
                avatar: p && p.avatar ? p.avatar : null,
                guest: !!(p && p.isGuest)
            });
        });
        var n = nextNumber();
        var max = cfg.maxGames || 4;
        var parts = [];
        if (n <= max) parts.push('Game ' + n + ' of ' + max);
        parts.push(new Date().toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' }));
        parts.push('HFECloud');
        var title = cfg.eventTitle ? 'Clash \u00b7 ' + cfg.eventTitle : 'Clash' + (cfg.modeName ? ' \u00b7 ' + cfg.modeName : '');
        var spec = { title: title, subtitle: parts.join(' \u00b7 '), mapUrl: cfg.mapUrl || null, teams: [{ name: activeTeamName(), rows: rows }] };
        if (onMap) {
            spec.mapAspect = cfg.mapAspect > 0 ? cfg.mapAspect : (16 / 9);
            spec.spots = (cfg.spots || []).filter(function (sp) { return sp.team === 0; })
                .map(function (sp) { return { team: sp.team, slot: sp.slot, x: sp.x, y: sp.y }; });
        }
        return spec;
    }

    function showExport() {
        if (!dlg || !window.LeagueExport) return;
        if (!exportSpec('list')) { toast('Build a team first, there is nobody to draw.'); return; }
        var mapOk = hasMapPicture();
        var kind = mapOk ? 'map' : 'list';     // the map is the first choice when the mode has places
        var canSend = !!(window.LeagueSend && window.LeagueSend.canSend && window.LeagueSend.canSend());
        var drawSeq = 0;                       // a late answer from an older picture is dropped
        var ready = false, sending = false, sent = false;
        dlg.textContent = '';
        dlg.oncancel = null;

        function blankCanvas() {
            var c = h('canvas', { class: 'lgb-cv', 'aria-label': 'Preview of the team image' });
            c.width = window.LeagueExport.WIDTH || 1280;     // keeps the 16:9 box while the picture is drawn
            c.height = window.LeagueExport.HEIGHT || 720;
            return c;
        }
        var cv = blankCanvas();
        var note = h('p', { class: 'lgb-dlg-err', role: 'status' });
        var copyBtn = h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Copy image', disabled: true });
        var dlBtn = h('button', { type: 'button', class: 'lg-btn', text: 'Download PNG', disabled: true });
        var sendBtn = canSend ? h('button', { type: 'button', class: 'lg-btn', text: 'Send to Discord', disabled: true }) : null;
        var pickMap, pickList;

        function fileName() { return kind === 'map' ? 'clash-team-map.png' : 'clash-team.png'; }

        function refreshButtons() {
            var on = ready && !sending;
            copyBtn.disabled = !on;
            dlBtn.disabled = !on;
            if (sendBtn) sendBtn.disabled = !on || sent;
            if (pickMap) { pickMap.disabled = sending; pickList.disabled = sending; }
        }

        function withBlob(fn) {
            try {
                cv.toBlob(function (b) { if (b) fn(b); else toast('Could not create the image.'); }, 'image/png');
            } catch (e) {
                toast('Could not create the image.');
            }
        }
        copyBtn.addEventListener('click', function () {
            withBlob(function (b) {
                try {
                    navigator.clipboard.write([new ClipboardItem({ 'image/png': b })]).then(
                        function () { toast('Image copied'); },
                        function () { toast('Copy was blocked here. Use Download.'); });
                } catch (e) {
                    toast('Copying images is not supported here. Use Download.');
                }
            });
        });
        dlBtn.addEventListener('click', function () {
            withBlob(function (b) {
                var url = URL.createObjectURL(b);
                var a = document.createElement('a');
                a.href = url;
                a.download = fileName();
                document.body.appendChild(a);
                a.click();
                a.remove();
                setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
            });
        });
        if (sendBtn) {
            sendBtn.addEventListener('click', function () {
                if (!ready || sending || sent) return;
                sending = true;
                note.textContent = '';
                sendBtn.textContent = 'Sending...';
                refreshButtons();
                window.LeagueSend.sendCanvas(cv, fileName()).then(function () {
                    sending = false;
                    sent = true;                       // one send per picture; pick again or reopen to send another
                    sendBtn.textContent = 'Sent to Discord';
                    toast('Sent to Discord');
                    refreshButtons();
                }).catch(function (e) {
                    sending = false;
                    sendBtn.textContent = 'Send to Discord';
                    note.textContent = (e && e.message) || 'The picture could not be sent.';
                    refreshButtons();
                });
            });
        }

        function render() {
            var my = ++drawSeq;
            ready = false;
            sent = false;
            note.textContent = '';
            if (sendBtn) sendBtn.textContent = 'Send to Discord';
            refreshButtons();
            if (pickMap) {
                pickMap.classList.toggle('is-on', kind === 'map');
                pickList.classList.toggle('is-on', kind === 'list');
                pickMap.setAttribute('aria-pressed', String(kind === 'map'));
                pickList.setAttribute('aria-pressed', String(kind === 'list'));
            }
            var next = blankCanvas();                 // a fresh canvas each time, so two drawings never mix
            var spec = exportSpec(kind);
            var job = kind === 'map' ? window.LeagueExport.drawMap(next, spec) : window.LeagueExport.draw(next, spec);
            job.then(function (info) {
                if (my !== drawSeq) return;
                cv.replaceWith(next);
                cv = next;
                ready = true;
                var msg = [];
                if (spec.mapUrl && info && !info.mapUsed) msg.push('The map image could not be used in the picture, so a plain background was drawn instead.');
                if (info && info.photosTotal > info.photosUsed) {
                    var miss = info.photosTotal - info.photosUsed;
                    msg.push(miss + ' of ' + info.photosTotal + ' player photo' + (info.photosTotal === 1 ? '' : 's') + ' could not be loaded, so initials are drawn instead.');
                }
                note.textContent = msg.join(' ');
                refreshButtons();
            }).catch(function () {
                if (my !== drawSeq) return;
                note.textContent = 'The picture could not be drawn.';
                refreshButtons();
            });
        }

        dlg.appendChild(h('h3', { id: 'lgcb-dlg-title', text: 'Export for Discord' }));
        dlg.appendChild(h('p', { class: 'lg-sub', text: '1280 \u00d7 720 PNG of the team as it is on the page now. Copy it and paste it into a message, download it' + (canSend ? ', or send it to the League Discord channel.' : '.') }));
        if (mapOk) {
            pickMap = h('button', { type: 'button', class: 'lg-btn lg-btn-ghost lgb-seg-b', text: 'With the map', onclick: function () { if (kind !== 'map') { kind = 'map'; render(); } } });
            pickList = h('button', { type: 'button', class: 'lg-btn lg-btn-ghost lgb-seg-b', text: 'The list', onclick: function () { if (kind !== 'list') { kind = 'list'; render(); } } });
            dlg.appendChild(h('div', { class: 'lgb-seg', role: 'group', 'aria-label': 'Which picture' }, [pickMap, pickList]));
        }
        dlg.appendChild(cv);
        dlg.appendChild(note);
        dlg.appendChild(h('div', { class: 'lgb-dlg-acts' }, [h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Close', onclick: closeDlg }), copyBtn, dlBtn, sendBtn]));
        openDlg();
        render();
    }

    // 11d: the match the server sends back has no team, so a known game keeps its team and a new one gets teamId (the team saved from).
    function upsertGame(m, teamId, playerIds) {
        var g = {
            id: m.id, number: m.gameNumber || 0, status: m.status,
            winner: m.status === 'done' && m.winner !== null && m.winner !== undefined ? m.winner : null,
            teamId: teamId === undefined ? null : teamId,
            playerIds: playerIds || []
        };
        var at = -1;
        state.games.forEach(function (x, i) { if (x.id === g.id) { at = i; if (teamId === undefined) g.teamId = x.teamId; if (!playerIds) g.playerIds = x.playerIds || []; } });
        if (at >= 0) state.games[at] = g; else state.games.push(g);
        state.games.sort(function (a, b) { return (a.number - b.number) || (a.id - b.id); });
    }

    // 11e: the record of a list of games; recordText() is the whole clash (every team), teamRecord(id) one team.
    function recordTextOf(list) {
        var w = 0, l = 0;
        list.forEach(function (g) {
            if (g.status !== 'done' || g.winner === null) return;
            if (g.winner === 0) w++; else l++;
        });
        return (w + l) === 0 ? 'No games finished yet' : (w + 'W ' + l + 'L');
    }

    function recordText() { return recordTextOf(state.games); }

    function teamRecord(id) { return recordTextOf(gamesOf(id)); }

    // The tiles above the board are rendered by the server; keep them in step with what just happened.
    function updateHeader() {
        var many = state.teams.length > 1;
        var rec = document.querySelector('[data-lgcb-record]');
        if (rec) {
            rec.textContent = recordText();
            if (rec.previousElementSibling) rec.previousElementSibling.textContent = many ? 'Series record · all teams' : 'Series record';
        }
        var cnt = document.querySelector('[data-lgcb-games]');
        if (cnt) {
            cnt.textContent = String(state.games.length);
            if (cnt.previousElementSibling) cnt.previousElementSibling.textContent = many ? 'Games saved · all teams' : 'Games saved';
        }
        var st = document.querySelector('[data-lgcb-status]');
        if (st && state.eventStatus === 'inprogress') {
            st.textContent = 'In progress';
            st.className = 'lgc-badge lgc-st-live';
        }
        if (st && state.eventStatus === 'done') {
            st.textContent = 'Done';
            st.className = 'lgc-badge lgc-st-done';
        }
    }

    // One call to a LeagueMatch endpoint. onOk gets the match the server sent back.
    function act(url, body, onOk, onFail) {
        state.acting = true;
        state.error = '';
        render();
        postTo(url, body).then(function (r) {
            state.acting = false;
            if (!r.ok || !r.match) {
                var msg = r.error || 'That was not accepted. Reload the page and try again.';
                if (onFail) onFail(msg); else state.error = msg;
                render();
                return;
            }
            onOk(r.match);
            updateHeader();
            render();
        }).catch(function (err) {
            state.acting = false;
            var msg = failMessage(err);
            if (onFail) onFail(msg); else state.error = msg;
            render();
        });
    }

    function saveGame() {
        var why = saveBlock();
        if (why || state.loading || state.acting) { if (why) toast(why); return; }
        var log = state.log.map(function (l) { return l.text; });
        if (state.edited) log.push('The team was edited by hand after the build.');
        act(cfg.submitUrl, {
            clashEventId: cfg.eventId,
            clashTeamId: state.activeTeam,
            clashSlots: state.result.slots.map(function (s) { return { lane: s.lane, playerId: s.playerId }; }),
            clashLog: log
        }, function (m) {
            draftSkip[state.activeTeam] = lineupSig(state.result.slots);
            upsertGame(m, state.activeTeam, state.result.slots.filter(function (sl) { return sl.playerId; }).map(function (sl) { return sl.playerId; }));
            toast('Game ' + (m.gameNumber || '') + ' saved.');
        });
    }

    function startGame(g) {
        if (state.acting) return;
        act(cfg.startUrl, { id: g.id }, function (m) {
            upsertGame(m);
            state.eventStatus = 'inprogress';
            toast('Game ' + g.number + ' started.');
        });
    }

    // Win / Loss only: the opponents are not on the page, so a win is team 0 and a loss is team 1.
    function finishDlg(g) {
        if (!dlg || state.acting) return;
        var choice = null;
        var errEl = h('div', { class: 'lg-alert lg-err', role: 'alert', hidden: true });
        var saveBtn;
        var cancelBtn;

        function showErr(msg) { errEl.textContent = msg; errEl.hidden = false; }

        function pick(value, title, sub) {
            return h('label', { class: 'lgb-rc' }, [
                h('input', { type: 'radio', name: 'lgcb-result', value: String(value), onchange: function () { choice = value; errEl.hidden = true; } }),
                h('span', { class: 'lgb-rc-b' }, [h('b', { text: title }), h('em', { text: sub })])
            ]);
        }

        // Results/export step 1: "Played on" (Cairo time) for someone with LeagueMatch > EditDate. Only a changed value is sent.
        var played = null;
        var playedStart = '';
        if (cfg.canEditDate && window.LeagueTime) {
            playedStart = window.LeagueTime.inputValue(new Date());
            played = h('input', { type: 'datetime-local', id: 'lgcb-played', class: 'lg-input', value: playedStart, max: playedStart });
        }

        dlg.textContent = '';
        dlg.oncancel = null;
        dlg.appendChild(h('h3', { id: 'lgcb-dlg-title', text: 'Finish game ' + g.number }));
        dlg.appendChild(h('p', { text: 'How did the game end?' }));
        dlg.appendChild(h('div', { class: 'lgb-radios' }, [
            pick(0, 'Win', teamNameOf(gameTeam(g)) + ' won.'),
            pick(1, 'Loss', teamNameOf(gameTeam(g)) + ' lost.')
        ]));
        if (played) {
            dlg.appendChild(h('label', { class: 'lg-lbl', for: 'lgcb-played', text: 'Played on (Cairo time)' }));
            dlg.appendChild(played);
            dlg.appendChild(h('p', { class: 'lg-sub', text: 'Entering an old game? Set the real date and time. Today\'s and yesterday\'s exports use this date.' }));
        }
        dlg.appendChild(errEl);
        cancelBtn = h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Cancel', onclick: closeDlg });
        saveBtn = h('button', {
            type: 'button', class: 'lg-btn', text: 'Save result',
            onclick: function () {
                if (choice === null) { showErr('Choose win or loss.'); return; }
                saveBtn.disabled = true;
                cancelBtn.disabled = true;
                act(cfg.finishUrl, {
                    id: g.id, winner: choice,
                    playedOn: played && played.value && played.value !== playedStart ? played.value : null
                }, function (m) {
                    closeDlg();
                    upsertGame(m);
                    toast('Game ' + g.number + ' saved as a ' + (choice === 0 ? 'win' : 'loss') + '.');
                    // Step 12h2: post this team's Clash progress picture to Discord when the owner switched auto-send on.
                    if (window.LeagueAutoSend) {
                        window.LeagueAutoSend.clashProgress(cfg.eventId, gameTeam(g), g.id, function (t) { toast(t, 7000); });
                    }
                }, function (msg) {
                    saveBtn.disabled = false;
                    cancelBtn.disabled = false;
                    showErr(msg);
                });
            }
        });
        dlg.appendChild(h('div', { class: 'lgb-dlg-acts' }, [cancelBtn, saveBtn]));
        openDlg();
    }

    /* ---------- end clash (6e4b) ---------- */

    // Why End clash is off, or '' when it can be pressed. The button is only drawn for a running clash, by someone with
    // LeagueClash > EndClash. The server checks all of this again (6e4a).
    function endBlock() {
        var open = anyUnfinishedGame();
        if (open) return 'Finish game ' + open.number + (state.teams.length > 1 ? ' of ' + teamNameOf(gameTeam(open)) : '') + ' before ending the clash.';
        return '';
    }

    function endDlg() {
        if (!dlg || state.acting) return;
        var errEl = h('div', { class: 'lg-alert lg-err', role: 'alert', hidden: true });
        var yesBtn;
        var cancelBtn;

        dlg.textContent = '';
        dlg.oncancel = null;
        dlg.appendChild(h('h3', { id: 'lgcb-dlg-title', text: 'End this clash?' }));
        dlg.appendChild(h('p', { text: 'Final record: ' + recordText() + ' (' + state.games.length + ' ' + (state.games.length === 1 ? 'game' : 'games') + ').' }));
        if (state.teams.length > 1) {
            var per = h('ul', { class: 'lgcb-log' });
            state.teams.forEach(function (t) {
                var n = gamesOf(t.id).length;
                per.appendChild(h('li', { text: t.name + ': ' + teamRecord(t.id) + ' (' + n + ' ' + (n === 1 ? 'game' : 'games') + ')' }));
            });
            dlg.appendChild(per);
        }
        dlg.appendChild(h('p', { text: 'The clash is marked Done and leaves this page. Nobody is notified and no game is deleted; the games stay in History.' }));
        dlg.appendChild(errEl);
        cancelBtn = h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Keep it open', onclick: closeDlg });
        yesBtn = h('button', {
            type: 'button', class: 'lg-btn', text: 'End clash',
            onclick: function () {
                yesBtn.disabled = true;
                cancelBtn.disabled = true;
                errEl.hidden = true;
                endClash(function (msg) {
                    yesBtn.disabled = false;
                    cancelBtn.disabled = false;
                    errEl.textContent = msg;
                    errEl.hidden = false;
                });
            }
        });
        dlg.appendChild(h('div', { class: 'lgb-dlg-acts' }, [cancelBtn, yesBtn]));
        openDlg();
    }

    // This endpoint answers { ok, error, message } with no match in it, so it does not go through act().
    function endClash(onFail) {
        if (state.acting) return;
        state.acting = true;
        render();
        postTo(cfg.endUrl, { eventId: cfg.eventId }).then(function (r) {
            state.acting = false;
            if (!r || !r.ok) {
                onFail((r && r.error) || 'That was not accepted. Reload the page and try again.');
                render();
                return;
            }
            closeDlg();
            state.eventStatus = 'done';
            updateHeader();
            render();
            toast(r.message || 'The clash has ended.');
        }).catch(function (err) {
            state.acting = false;
            onFail(failMessage(err));
            render();
        });
    }

    /* ---------- next game (6e4e) ---------- */

    // Why Next game is off, or '' when it can be pressed. The button is only drawn for a running clash, by someone with
    // LeagueClash > NextGame. The server checks all of this again (6e4d).
    function nextBlock() {
        var open = unfinishedGame();
        if (open) return 'Finish game ' + open.number + ' first.';
        if (capReached()) return 'A team has at most ' + (cfg.maxGames || 4) + ' games.';
        return '';
    }

    function nextGame() {
        if (state.loading || state.acting || nextBlock()) return;
        if ((state.edited || state.fromGame) && teamHasPlayer()) {
            confirmDlg('Load the last team?',
                'This replaces the team on screen with the team of the last game, so the changes you made by hand are lost.',
                'Load it', loadNext, function () { render(); });
            return;
        }
        loadNext();
    }

    // The server copies the team of the highest numbered game and rechecks it (strength, fit tags). Nothing is saved here.
    function loadNext() {
        var mine = ++seq;
        state.loading = true;
        state.error = '';
        render();
        postTo(cfg.nextUrl, { eventId: cfg.eventId, teamId: state.activeTeam, rankSystem: state.system, busy: busyList() })
            .then(function (r) {
                if (mine !== seq) return;
                state.loading = false;
                if (!r || !r.ok || !r.team) {
                    state.error = (r && r.error) || 'The last team could not be loaded.';
                    render();
                    return;
                }
                state.result = r.team;
                state.log = [{ kind: '', text: 'Team copied from game ' + r.fromGame + ', for game ' + r.nextNumber + '.' }];
                state.noFit = [];
                state.warnings = r.team.warnings || [];
                state.sel = null;
                state.edited = false;
                state.fromGame = r.fromGame;
                delete draftSkip[state.activeTeam];
                // 11g: anyone who is on another team now comes off the copied team (the server rechecks the rest).
                var taken = takenMap();
                var gone = [];
                var rest = cloneSlots();
                rest.forEach(function (sl) {
                    if (sl.playerId && taken[sl.playerId]) { gone.push(P[sl.playerId] ? P[sl.playerId].name : 'A player'); sl.playerId = null; }
                });
                if (gone.length) {
                    toast(gone.join(', ') + (gone.length === 1 ? ' is' : ' are') + ' on another team, so ' + (gone.length === 1 ? 'was' : 'were') + ' left out of game ' + r.nextNumber + '.');
                    evaluate(rest, true);
                    return;
                }
                render();
                toast('Team of game ' + r.fromGame + ' loaded. Change it if you want, then save it as game ' + r.nextNumber + '.');
            })
            .catch(function (err) {
                if (mine !== seq) return;
                state.loading = false;
                state.error = failMessage(err);
                render();
            });
    }

    /* ---------- changes that rebuild ---------- */

    // fn applies the change to the state; the team is then rebuilt. After a hand edit the user confirms first.
    // Step 11a: this is Set best team. It builds from the free players and replaces the team on screen, so it asks first
    // when there is a team to lose (one picked by hand, or copied from an earlier game).
    function setBest() {
        if (state.loading || state.acting) return;
        if ((state.edited || state.fromGame) && teamHasPlayer()) {
            confirmDlg('Set the best team?',
                state.fromGame && !state.edited
                    ? 'This builds the best team from the free players, so the team copied from game ' + state.fromGame + ' is replaced.'
                    : 'This builds the best team from the free players, so the team you picked is replaced.',
                'Set best team', build, function () { render(); });
            return;
        }
        build();
    }

    // Step 11a: after the busy set changed, anyone busy who is on the team comes off it (the team is rechecked by the server).
    // Nobody is rebuilt or replaced. Returns true when a recheck was started.
    function dropBusyFromTeam() {
        if (!state.result) { render(); return false; }
        var names = [];
        var other = [];
        var taken = takenMap();
        var a = cloneSlots();
        a.forEach(function (s) {
            if (!s.playerId) return;
            if (state.busy.has(s.playerId)) {
                if (P[s.playerId]) names.push(P[s.playerId].name);
                s.playerId = null;
            } else if (taken[s.playerId]) {
                if (P[s.playerId]) other.push(P[s.playerId].name);
                s.playerId = null;
            }
        });
        if (!names.length && !other.length) { render(); return false; }
        var msg = [];
        if (names.length) msg.push(names.join(', ') + (names.length === 1 ? ' is busy, so was' : ' are busy, so were') + ' taken off the team.');
        if (other.length) msg.push(other.join(', ') + (other.length === 1 ? ' is on another team, so was' : ' are on another team, so were') + ' taken off this one.');
        toast(msg.join(' '));
        evaluate(a);
        return true;
    }

    function toggleBusy(id) {
        if (state.busy.has(id)) state.busy.delete(id); else state.busy.add(id);
        dropBusyFromTeam();
    }

    function resetBusy() {
        state.busy = new Set(answeredBusy());
        dropBusyFromTeam();
    }

    // Step 8i: save a guest's answer. Step 11a: a guest who is now busy comes off the team; nothing is rebuilt.
    function saveGuestBusy(p) {
        if (state.loading || state.acting || !cfg.guestBusyUrl) return;
        var want = !answeredBusySet.has(p.id);
        state.acting = true;
        state.error = '';
        render();
        postTo(cfg.guestBusyUrl, { eventId: cfg.eventId, profileId: p.id, busy: want }).then(function (r) {
            state.acting = false;
            if (!r || !r.ok) {
                state.error = (r && r.error) || 'That was not accepted. Reload the page and try again.';
                render();
                return;
            }
            if (want) { answeredBusySet.add(p.id); answeredFreeSet.delete(p.id); state.busy.add(p.id); }
            else { answeredBusySet.delete(p.id); answeredFreeSet.add(p.id); state.busy.delete(p.id); }
            toast(r.message || 'Saved.');
            dropBusyFromTeam();
        }).catch(function (err) {
            state.acting = false;
            state.error = failMessage(err);
            render();
        });
    }

    function setSystem(sys) {
        if (sys === state.system) return;
        state.system = sys;
        // 11a: only the strength of the team on screen changes; nobody is moved.
        if (teamHasPlayer()) evaluate(cloneSlots(), true); else render();
    }

    /* ---------- hand edits ---------- */

    function cloneSlots() {
        return state.result.slots.map(function (s) {
            return { lane: s.lane, playerId: s.playerId, manual: false };
        });
    }

    function onSlot(i) {
        var sel = state.sel;
        if (!sel) { state.sel = { kind: 'slot', i: i }; focusKey = 's' + i; render(); return; }
        if (sel.kind === 'slot') {
            if (sel.i === i) { state.sel = null; focusKey = 's' + i; render(); return; }
            var a = cloneSlots();
            var tmp = a[sel.i].playerId;
            a[sel.i].playerId = a[i].playerId;
            a[i].playerId = tmp;
            focusKey = 's' + i;
            evaluate(a);
            return;
        }
        // a bench player is selected: it takes this slot (whoever was there goes back to the bench)
        var b = cloneSlots();
        b[i].playerId = sel.id;
        focusKey = 's' + i;
        evaluate(b);
    }

    function onBench(id) {
        var sel = state.sel;
        if (sel && sel.kind === 'slot') {
            var a = cloneSlots();
            a[sel.i].playerId = id;
            focusKey = 's' + sel.i;
            evaluate(a);
            return;
        }
        if (sel && sel.kind === 'bench' && sel.id === id) { state.sel = null; } else { state.sel = { kind: 'bench', id: id }; }
        focusKey = 'b' + id;
        render();
    }

    function removeAt(i) {
        var a = cloneSlots();
        a[i].playerId = null;
        focusKey = 's' + i;
        evaluate(a);
    }

    /* ---------- dialogs ---------- */

    function openDlg() {
        if (!dlg) return;
        if (typeof dlg.showModal === 'function') { if (!dlg.open) dlg.showModal(); }
        else dlg.setAttribute('open', '');
    }

    function closeDlg() {
        if (dlg && dlg.open && typeof dlg.close === 'function') dlg.close();
        else if (dlg) dlg.removeAttribute('open');
    }

    function confirmDlg(title, text, yesLabel, onYes, onNo) {
        if (!dlg) { onYes(); return; }
        dlg.textContent = '';
        dlg.appendChild(h('h3', { id: 'lgcb-dlg-title', text: title }));
        dlg.appendChild(h('p', { text: text }));
        dlg.appendChild(h('div', { class: 'lgb-dlg-acts' }, [
            h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Keep my changes', onclick: function () { closeDlg(); if (onNo) onNo(); } }),
            h('button', { type: 'button', class: 'lg-btn', text: yesLabel, onclick: function () { closeDlg(); onYes(); } })
        ]));
        // Esc counts as "keep my changes" too, so the radios and buttons go back to what the state says.
        dlg.oncancel = function () { if (onNo) onNo(); };
        openDlg();
    }

    // Prototype showNoFit: where nobody could cover a busy player's lane cleanly.
    function showNoFit() {
        if (!dlg || !state.noFit.length) return;
        dlg.textContent = '';
        dlg.oncancel = null;
        dlg.appendChild(h('h3', { id: 'lgcb-dlg-title', text: 'Nobody fits cleanly' }));
        dlg.appendChild(h('p', { text: 'These lanes could not be covered by someone who plays them as a main or second lane. The second option was put there. Edit these slots by hand if you know better.' }));
        state.noFit.forEach(function (n) {
            var busy = P[n.busyPlayerId];
            var repl = P[n.replacementPlayerId];
            dlg.appendChild(h('div', { class: 'lgb-opt lgb-opt-static' }, [
                h('b', { text: laneLabel(n.lane) }),
                h('small', { text: (busy ? busy.name : 'Someone') + ' is busy. ' + (repl ? repl.name : 'A player') + ' (second option) takes ' + laneLabel(n.lane) + '.' })
            ]));
        });
        dlg.appendChild(h('div', { class: 'lgb-dlg-acts' }, [
            h('button', { type: 'button', class: 'lg-btn', text: 'Edit by hand', onclick: closeDlg })
        ]));
        openDlg();
    }


    /* ---------- teams (11d) ---------- */

    function lineupCount(result) {
        return result && result.slots ? result.slots.filter(function (s) { return s.playerId; }).length : 0;
    }

    function stashTeam() {
        if (state.activeTeam === null) return;
        teamStore[state.activeTeam] = {
            result: state.result, noneFree: state.noneFree, log: state.log, noFit: state.noFit,
            warnings: state.warnings, sel: null, edited: state.edited, fromGame: state.fromGame
        };
    }

    // Put a team's own lineup on screen (an empty one the first time). Players who were marked busy meanwhile come off it.
    function showTeam(id) {
        var s = teamStore[id];
        state.activeTeam = id;
        ++seq;                                 // an answer still on its way for the other team is dropped
        state.loading = false;
        state.error = '';
        state.sel = null;
        if (s) {
            state.result = s.result; state.noneFree = s.noneFree; state.log = s.log; state.noFit = s.noFit;
            state.warnings = s.warnings; state.edited = s.edited; state.fromGame = s.fromGame;
        } else {
            state.result = emptyResult(); state.noneFree = false; state.log = []; state.noFit = [];
            state.warnings = []; state.edited = false; state.fromGame = 0;
        }
        delete teamStore[id];
        var dropped = dropBusyFromTeam();
        // 11h: a lineup restored from the draft has no strength yet; the server checks it the first time it is shown.
        if (s && s.recheck && !dropped && lineupCount(state.result)) evaluate(cloneSlots(), true);
    }

    function switchTeam(id) {
        if (id === state.activeTeam || state.loading || state.acting) return;
        stashTeam();
        teamFocus = id;
        showTeam(id);
    }

    function lineupOf(id) { return id === state.activeTeam ? state.result : (teamStore[id] ? teamStore[id].result : null); }

    function canEditTeams() { return state.eventStatus !== 'done'; }

    function teamCard(t) {
        var on = t.id === state.activeTeam;
        var games = gamesOf(t.id).length;
        var picked = lineupCount(lineupOf(t.id));
        return h('button', {
            type: 'button', class: 'lgcb-team' + (on ? ' is-sel' : ''), 'aria-pressed': String(on), 'data-team': String(t.id),
            disabled: !on && (state.loading || state.acting),
            title: on ? 'The team on screen.' : 'Show ' + t.name + '.',
            onclick: function () { switchTeam(t.id); }
        }, [
            h('b', { text: t.name }),
            h('small', { text: picked + ' of 5 picked · ' + games + (games === 1 ? ' game' : ' games') }),
            games ? h('small', { class: 'lgcb-team-rec', text: teamRecord(t.id) }) : null
        ]);
    }

    function renderTeams() {
        if (!teamsMount) return;
        teamsMount.textContent = '';
        if (!state.teams.length) return;
        var row = h('div', { class: 'lgcb-teamrow', role: 'group', 'aria-label': 'Teams of this clash' });
        state.teams.forEach(function (t) { row.appendChild(teamCard(t)); });
        var max = cfg.maxTeams || 4;
        if (cfg.canCreateTeam && canEditTeams()) {
            var full = state.teams.length >= max;
            row.appendChild(h('button', {
                type: 'button', class: 'lg-btn lg-btn-ghost lgcb-newteam', text: '+ New team', disabled: full || state.loading || state.acting,
                title: full ? 'A clash has at most ' + max + ' teams.' : 'Add another team to this clash.',
                onclick: newTeamDlg
            }));
        }
        teamsMount.appendChild(row);

        var tools = [];
        if (cfg.canRenameTeam && canEditTeams()) {
            tools.push(h('button', {
                type: 'button', class: 'lg-btn lg-btn-ghost lgb-small', text: 'Rename ' + activeTeamName(),
                disabled: state.loading || state.acting, onclick: renameTeamDlg
            }));
        }
        if (cfg.canDeleteTeam && canEditTeams()) {
            var why = deleteBlock();
            tools.push(h('button', {
                type: 'button', class: 'lg-btn lg-btn-ghost lgb-small', text: 'Delete ' + activeTeamName(),
                disabled: !!why || state.loading || state.acting, title: why || 'Delete this team.', onclick: deleteTeamDlg
            }));
        }
        if (tools.length) teamsMount.appendChild(h('div', { class: 'lgcb-teamtools' }, tools));

        if (teamFocus !== null) {
            var el = teamsMount.querySelector('[data-team="' + teamFocus + '"]');
            if (el) el.focus();
            teamFocus = null;
        }
    }

    function deleteBlock() {
        if (state.teams.length <= 1) return 'A clash needs at least one team.';
        if (myGames().length) return 'A team with games cannot be deleted.';
        return '';
    }

    // One name field in the shared dialog. onSend(name, fail, done) does the call; fail(msg) shows the error and re-enables.
    function teamNameDlg(title, text, initial, yesLabel, onSend) {
        if (!dlg) return;
        var errEl = h('div', { class: 'lg-alert lg-err', role: 'alert', hidden: true });
        var input = h('input', { type: 'text', class: 'lg-input', maxlength: String(cfg.maxTeamName || 30), value: initial, 'aria-label': 'Team name', autocomplete: 'off' });
        var cancelBtn = h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Cancel', onclick: closeDlg });
        var yesBtn = h('button', { type: 'submit', class: 'lg-btn', text: yesLabel });
        var form = h('form', { method: 'dialog', novalidate: true }, [
            h('p', { text: text }),
            input, errEl,
            h('div', { class: 'lgb-dlg-acts' }, [cancelBtn, yesBtn])
        ]);
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            var name = input.value.trim();
            if (!name) { errEl.textContent = 'Give the team a name.'; errEl.hidden = false; return; }
            yesBtn.disabled = true; cancelBtn.disabled = true; errEl.hidden = true;
            onSend(name, function (msg) {
                yesBtn.disabled = false; cancelBtn.disabled = false;
                errEl.textContent = msg; errEl.hidden = false;
            });
        });
        dlg.textContent = '';
        dlg.oncancel = null;
        dlg.appendChild(h('h3', { id: 'lgcb-dlg-title', text: title }));
        dlg.appendChild(form);
        openDlg();
        input.focus();
        input.select();
    }

    function sendTeam(url, body, fail, onOk) {
        postTo(url, body).then(function (r) {
            if (!r || !r.ok) { fail((r && r.error) || 'That was not accepted. Reload the page and try again.'); return; }
            onOk(r);
        }).catch(function (err) { fail(failMessage(err)); });
    }

    function newTeamDlg() {
        if (state.loading || state.acting) return;
        teamNameDlg('New team', 'Give the new team a name. It starts with an empty lineup.', '', 'Create team', function (name, fail) {
            sendTeam(cfg.createTeamUrl, { eventId: cfg.eventId, name: name }, fail, function (r) {
                if (!r.team) { fail('The team was created but the server did not say which. Reload the page.'); return; }
                closeDlg();
                state.teams.push({ id: r.team.id, name: r.team.name });
                updateHeader();
                stashTeam();
                teamFocus = r.team.id;
                showTeam(r.team.id);
                toast(r.message || ('Team ' + r.team.name + ' created.'));
            });
        });
    }

    function renameTeamDlg() {
        if (state.loading || state.acting || state.activeTeam === null) return;
        var id = state.activeTeam;
        teamNameDlg('Rename team', 'New name for ' + activeTeamName() + '.', activeTeamName(), 'Save name', function (name, fail) {
            sendTeam(cfg.renameTeamUrl, { teamId: id, name: name }, fail, function (r) {
                closeDlg();
                var shown = r.team ? r.team.name : name;
                state.teams.forEach(function (t) { if (t.id === id) t.name = shown; });
                teamFocus = id;
                render();
                toast(r.message || 'Team renamed.');
            });
        });
    }

    function deleteTeamDlg() {
        if (state.loading || state.acting || deleteBlock()) return;
        var id = state.activeTeam;
        var name = activeTeamName();
        var errEl = h('div', { class: 'lg-alert lg-err', role: 'alert', hidden: true });
        var cancelBtn = h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Keep it', onclick: closeDlg });
        var yesBtn = h('button', {
            type: 'button', class: 'lg-btn', text: 'Delete team',
            onclick: function () {
                yesBtn.disabled = true; cancelBtn.disabled = true; errEl.hidden = true;
                sendTeam(cfg.deleteTeamUrl, { teamId: id }, function (msg) {
                    yesBtn.disabled = false; cancelBtn.disabled = false;
                    errEl.textContent = msg; errEl.hidden = false;
                }, function (r) {
                    closeDlg();
                    state.teams = state.teams.filter(function (t) { return t.id !== id; });
                    delete teamStore[id];
                    updateHeader();
                    var next = state.teams[0].id;
                    teamFocus = next;
                    showTeam(next);
                    toast(r.message || ('Team ' + name + ' deleted.'));
                });
            }
        });
        dlg.textContent = '';
        dlg.oncancel = null;
        dlg.appendChild(h('h3', { id: 'lgcb-dlg-title', text: 'Delete ' + name + '?' }));
        dlg.appendChild(h('p', { text: 'The team has no games. Its lineup on this page is lost.' }));
        dlg.appendChild(errEl);
        dlg.appendChild(h('div', { class: 'lgb-dlg-acts' }, [cancelBtn, yesBtn]));
        openDlg();
    }


    /* ---------- auto save in this browser (11h) ---------- */

    var DRAFT_PREFIX = 'lgcb-draft-v1:';
    var DRAFT_KEY = DRAFT_PREFIX + cfg.eventId;
    var DRAFT_MAX_AGE = 2 * 24 * 3600 * 1000;      // a draft older than two days is ignored
    var draftTimer = null;
    var draftSkip = {};                              // team id -> lineup that was just saved as a game (not kept as a draft)

    function lineupSig(slots) {
        return (slots || []).map(function (sl) { return sl.playerId || 0; }).join(',');
    }

    function store(fn) { try { return fn(window.localStorage); } catch (e) { return null; } }

    function scheduleDraft() {
        clearTimeout(draftTimer);
        draftTimer = setTimeout(persistDraft, 400);
    }

    function persistDraft() {
        if (state.eventStatus === 'done') { store(function (ls) { ls.removeItem(DRAFT_KEY); }); return; }
        var teams = {};
        var any = false;
        state.teams.forEach(function (t) {
            var r = t.id === state.activeTeam ? state.result : (teamStore[t.id] ? teamStore[t.id].result : null);
            if (!r || !r.slots || !lineupCount(r)) return;
            if (draftSkip[t.id] !== undefined && draftSkip[t.id] === lineupSig(r.slots)) return;
            teams[t.id] = r.slots.map(function (sl) { return { lane: sl.lane, playerId: sl.playerId || null }; });
            any = true;
        });
        var added = [];
        var removed = [];
        state.busy.forEach(function (id) { if (!answeredBusySet.has(id)) added.push(id); });
        answeredBusySet.forEach(function (id) { if (!state.busy.has(id)) removed.push(id); });
        if (!any && !added.length && !removed.length && state.system === 'rank') { store(function (ls) { ls.removeItem(DRAFT_KEY); }); return; }
        store(function (ls) {
            ls.setItem(DRAFT_KEY, JSON.stringify({
                v: 1, savedAt: Date.now(), active: state.activeTeam, system: state.system,
                busyAdded: added, busyRemoved: removed, teams: teams
            }));
        });
    }

    // Other clashes' drafts older than a week go.
    function pruneDrafts() {
        store(function (ls) {
            var dead = [];
            for (var i = 0; i < ls.length; i++) {
                var k = ls.key(i);
                if (!k || k.indexOf(DRAFT_PREFIX) !== 0) continue;
                try {
                    var d = JSON.parse(ls.getItem(k));
                    if (!d || !d.savedAt || Date.now() - d.savedAt > 7 * 24 * 3600 * 1000) dead.push(k);
                } catch (e) { dead.push(k); }
            }
            dead.forEach(function (k) { ls.removeItem(k); });
        });
    }

    // Put the saved lineups back. Returns true when something was restored. The players are rechecked by the server on the first look.
    function restoreDraft() {
        pruneDrafts();
        if (state.eventStatus === 'done') return false;
        var raw = store(function (ls) { return ls.getItem(DRAFT_KEY); });
        if (!raw) return false;
        var d;
        try { d = JSON.parse(raw); } catch (e) { return false; }
        if (!d || d.v !== 1 || !d.savedAt || Date.now() - d.savedAt > DRAFT_MAX_AGE || !d.teams) return false;

        var used = {};
        var restored = 0;
        var firstWithPlayers = null;
        state.teams.forEach(function (t) {
            var saved = d.teams[t.id];
            if (!saved || !saved.length) return;
            var slots = emptyResult().slots.map(function (blank, i) {
                var src = saved[i];
                var pid = src && src.playerId && P[src.playerId] && !used[src.playerId] ? src.playerId : null;
                if (pid) used[pid] = true;
                return { lane: blank.lane, playerId: pid, manual: false };
            });
            var n = slots.filter(function (sl) { return sl.playerId; }).length;
            if (!n) return;
            restored += n;
            if (firstWithPlayers === null) firstWithPlayers = t.id;
            teamStore[t.id] = {
                result: { ok: true, mode: cfg.mode, isEmpty: false, strength: 0, slots: slots, warnings: [] },
                noneFree: false, log: [{ kind: '', text: 'Lineup restored from the draft kept in this browser.' }], noFit: [],
                warnings: [], sel: null, edited: true, fromGame: 0, recheck: true
            };
        });

        (d.busyAdded || []).forEach(function (id) { if (P[id] && !P[id].isGuest) state.busy.add(id); });
        (d.busyRemoved || []).forEach(function (id) { if (P[id] && !P[id].isGuest) state.busy.delete(id); });
        if (d.system === 'tier' && cfg.hasTiers) state.system = 'tier';

        if (!restored && !(d.busyAdded || []).length && !(d.busyRemoved || []).length) return false;
        var want = state.teams.some(function (t) { return t.id === d.active; }) ? d.active : (firstWithPlayers !== null ? firstWithPlayers : state.activeTeam);
        if (want !== state.activeTeam) {
            // the saved team on screen becomes the active one; the first team (blank so far) is simply not stored
            state.activeTeam = want;
        }
        var mine = teamStore[state.activeTeam];
        if (mine) {
            delete teamStore[state.activeTeam];
            state.result = mine.result; state.log = mine.log; state.edited = true;
            toast('Your unsaved lineup was restored from this browser.');
            if (!dropBusyFromTeam()) evaluate(cloneSlots(), true);       // strength and fit tags come from the server
            return true;
        }
        render();
        toast(restored ? 'Your unsaved lineups were restored from this browser. Open a team card to see its lineup.' : 'Your busy marks were restored from this browser.');
        return true;
    }

    /* ---------- rendering ---------- */

    function render() {
        app.textContent = '';
        app.classList.toggle('is-busy', state.loading);
        renderTeams();
        scheduleDraft();

        if (!(cfg.players || []).length) return;

        var grid = h('div', { class: 'lgb-grid lgcb-grid' }, [
            h('div', { class: 'lgb-left' }, [systemCard(), gamesCard(), rosterCard()]),
            resultCard()
        ]);
        app.appendChild(grid);

        if (focusKey) {
            var el = app.querySelector('[data-key="' + focusKey + '"]');
            if (el) el.focus();
            focusKey = null;
        }
    }

    function radio(value, title, sub, disabled) {
        return h('label', { class: 'lgb-rc' }, [
            h('input', {
                type: 'radio', name: 'lgcb-system', value: value, checked: state.system === value, disabled: disabled,
                onchange: function () { setSystem(value); }
            }),
            h('span', { class: 'lgb-rc-b' }, [h('b', { text: title }), h('em', { text: sub })])
        ]);
    }

    function systemCard() {
        return h('section', { class: 'lg-card' }, [
            h('div', { class: 'lgb-head' }, [h('h2', { class: 'lg-h2', text: 'Pick by' })]),
            h('div', { class: 'lgb-radios' }, [
                radio('rank', 'Rank', 'Current rank (60%) and peak rank (40%).', false),
                radio('tier', 'Custom tiers', cfg.hasTiers ? 'The tiers from Settings; no tier = rank.' : 'Needs at least two tiers in Settings.', !cfg.hasTiers)
            ])
        ]);
    }

    function answerTag(p) {
        var busy = state.busy.has(p.id);
        var onOther = takenMap()[p.id];
        if (onOther && !busy) return h('span', { class: 'lgcb-ans', text: 'On ' + onOther });
        if (p.isGuest) return h('span', { class: 'lgcb-ans' + (busy ? ' is-busy' : ''), text: busy ? 'Guest · busy' : 'Guest · free' });
        var wasBusy = answeredBusySet.has(p.id);
        var text;
        if (busy && wasBusy) text = 'Answered busy';
        else if (busy) text = 'Busy (set here)';
        else if (wasBusy) text = 'Free (set here)';
        else if (answeredFreeSet.has(p.id)) text = 'Can join';
        else text = 'No answer · free';
        return h('span', { class: 'lgcb-ans' + (busy ? ' is-busy' : ''), text: text });
    }

    function rosterRow(p) {
        var busy = state.busy.has(p.id);
        var row = h('li', { class: 'lgcb-row' + (busy ? ' is-busy' : ''), 'data-name': p.name.toLowerCase() }, [
            avatar(p, 'lgb-av lgcb-av'),
            h('div', { class: 'lgcb-who' }, [
                h('b', { text: p.name }),
                h('small', null, [rankIcon(p.curKey), ' ' + p.curLabel + (ARAM ? '' : ' · ' + laneLabel(p.main))])
            ]),
            answerTag(p)
        ]);
        if (!p.isGuest) {
            row.appendChild(h('button', {
                type: 'button', class: 'lg-btn lg-btn-ghost lgb-small lgcb-busybtn', text: 'Busy',
                'aria-pressed': String(busy), 'aria-label': p.name + ', busy', 'data-key': 'r' + p.id,
                disabled: state.loading,
                onclick: function () { focusKey = 'r' + p.id; toggleBusy(p.id); }
            }));
        } else if (cfg.canGuestBusy && cfg.guestBusyUrl) {
            // Step 8i: this one saves the guest's answer for the clash (a moderator only).
            row.appendChild(h('button', {
                type: 'button', class: 'lg-btn lg-btn-ghost lgb-small lgcb-busybtn', text: 'Busy',
                'aria-pressed': String(busy), 'aria-label': p.name + ', busy (saved for this clash)', 'data-key': 'r' + p.id,
                disabled: state.loading || state.acting,
                onclick: function () { focusKey = 'r' + p.id; saveGuestBusy(p); }
            }));
        }
        return row;
    }

    function applyFilter() {
        var q = state.filter.trim().toLowerCase();
        var rows = app.querySelectorAll('.lgcb-row');
        for (var i = 0; i < rows.length; i++) {
            rows[i].hidden = !!q && rows[i].getAttribute('data-name').indexOf(q) < 0;
        }
    }

    function rosterCard() {
        var players = cfg.players || [];
        var busyCount = players.filter(function (p) { return state.busy.has(p.id); }).length;
        var list = h('ul', { class: 'lgcb-list' }, players.map(rosterRow));
        var filter = h('input', {
            type: 'search', class: 'lg-input lgb-filter', placeholder: 'Find a player', 'aria-label': 'Find a player', value: state.filter,
            oninput: function (e) { state.filter = e.target.value; applyFilter(); }
        });
        var card = h('section', { class: 'lg-card' }, [
            h('div', { class: 'lgb-head' }, [
                h('h2', { class: 'lg-h2', text: 'Who can play' }),
                h('span', { class: 'lgb-count', text: busyCount + ' busy' })
            ]),
            h('p', { class: 'lgcb-note', text: 'Pre-filled from the saved answers. Busy on a member is only for this page: it never changes the member’s answer. Busy on a guest is saved for this clash. No answer counts as free.' }),
            h('div', { class: 'lgb-rowtools' }, [
                filter,
                h('button', { type: 'button', class: 'lg-btn lg-btn-ghost lgb-small', text: 'Reset to answers', disabled: state.loading, onclick: resetBusy })
            ]),
            list
        ]);
        setTimeout(applyFilter, 0);
        return card;
    }

    function banner(kind, text, extra) {
        return h('div', { class: 'lg-alert lgb-banner ' + kind, role: kind === 'is-err' ? 'alert' : 'status' }, [
            h('span', { text: text })
        ].concat(extra || []));
    }

    function slotEl(slot, i) {
        var p = slot.playerId ? P[slot.playerId] : null;
        var sel = !!(state.sel && state.sel.kind === 'slot' && state.sel.i === i);
        var el = h('div', { class: 'lgb-slot' + (p ? '' : ' is-empty') + (sel ? ' is-sel' : '') });
        if (slot.lane !== null && slot.lane !== undefined) el.appendChild(h('span', { class: 'lgb-ln', text: laneLabel(slot.lane) }));
        var laneText = (slot.lane !== null && slot.lane !== undefined) ? ', ' + laneLabel(slot.lane) : '';
        var hint = state.sel && state.sel.kind === 'bench'
            ? 'Press to put the selected bench player here.'
            : (state.sel ? 'Press to swap with the selected slot.' : 'Press to select, then press a slot or a bench player.');

        if (!p) {
            el.appendChild(h('button', {
                type: 'button', class: 'lgb-sl', 'data-key': 's' + i, 'aria-pressed': String(sel), disabled: state.loading,
                'aria-label': 'Open slot' + laneText + '. ' + hint,
                onclick: function () { onSlot(i); }
            }, [h('small', { text: 'Open slot' })]));
            return el;
        }

        var sub = [rankIcon(p.curKey), ' ' + p.curLabel];
        el.appendChild(h('button', {
            type: 'button', class: 'lgb-sl', 'data-key': 's' + i, 'aria-pressed': String(sel), disabled: state.loading,
            'aria-label': p.name + laneText + '. ' + (sel ? 'Selected. Press another slot to swap.' : hint),
            onclick: function () { onSlot(i); }
        }, [
            h('b', null, [
                p.name,
                p.rankKing ? badgeEl('rank') : null,
                p.mvpKing ? badgeEl('mvp') : null,
                p.clashKing ? badgeEl('clash') : null,
                slot.fit ? h('span', { class: 'lgb-fit ' + (slot.fitKind || ''), text: slot.fit }) : null,
                slot.manual ? h('span', { class: 'lgb-fit warn', text: 'Edit me' }) : null,
                state.busy.has(p.id) ? h('span', { class: 'lgb-fit warn', text: 'Busy' }) : null
            ]),
            h('small', null, sub)
        ]));
        el.appendChild(h('button', {
            type: 'button', class: 'lg-btn lg-btn-ghost lgb-x', text: '✕', disabled: state.loading,
            'aria-label': 'Take ' + p.name + ' off the team',
            onclick: function () { removeAt(i); }
        }));
        return el;
    }

    function benchEl() {
        var taken = takenMap();
        var players = (cfg.players || []).filter(function (p) { return !state.busy.has(p.id) && !onTeam(p.id) && !taken[p.id]; });
        var elsewhere = (cfg.players || []).filter(function (p) { return !state.busy.has(p.id) && !onTeam(p.id) && taken[p.id]; }).length;
        var box = h('div', { class: 'lgcb-bench' }, [
            h('h3', { class: 'lgcb-sub', text: 'Bench (free and not on a team)' })
        ]);
        if (elsewhere) box.appendChild(h('p', { class: 'lgcb-note', text: elsewhere + (elsewhere === 1 ? ' player is' : ' players are') + ' on another team, so not listed here.' }));
        if (!players.length) {
            box.appendChild(h('p', { class: 'lgcb-note', text: 'Nobody is left on the bench.' }));
            return box;
        }
        box.appendChild(h('p', { class: 'lgcb-note', text: 'To swap by hand: press a slot, then press a bench player. Or press a bench player first, then a slot.' }));
        var chips = h('div', { class: 'lgb-chips lgcb-chips' });
        players.forEach(function (p) {
            var sel = !!(state.sel && state.sel.kind === 'bench' && state.sel.id === p.id);
            chips.appendChild(h('button', {
                type: 'button', class: 'lgcb-chip' + (sel ? ' is-sel' : ''), 'aria-pressed': String(sel), 'data-key': 'b' + p.id,
                disabled: state.loading,
                onclick: function () { onBench(p.id); }
            }, [rankIcon(p.curKey), h('span', { text: p.name }), p.isGuest ? h('em', { text: 'guest' }) : null]));
        });
        box.appendChild(chips);
        return box;
    }

    function logEl() {
        if (!state.log.length) return null;
        var box = h('div', { class: 'lgcb-logbox' }, [
            h('h3', { class: 'lgcb-sub', text: state.fromGame ? 'Where this team came from' : (state.edited ? 'What the builder did (before your changes)' : 'What the builder did') })
        ]);
        var ul = h('ul', { class: 'lgcb-log' });
        state.log.forEach(function (l) {
            ul.appendChild(h('li', { class: l.kind ? 'is-' + l.kind : '', text: l.text }));
        });
        box.appendChild(ul);
        return box;
    }

    function gameBadge(g) {
        if (g.status === 'done') {
            return h('span', { class: 'lgcb-gst ' + (g.winner === 0 ? 'is-win' : 'is-loss'), text: g.winner === 0 ? 'Win' : 'Loss' });
        }
        return h('span', { class: 'lgcb-gst' + (g.status === 'live' ? ' is-live' : ''), text: g.status === 'live' ? 'Live' : 'Saved' });
    }

    function gameRow(g) {
        var acts = h('span', { class: 'lgcb-gacts' });
        if (g.status === 'submitted' && cfg.canStart) {
            acts.appendChild(h('button', {
                type: 'button', class: 'lg-btn lg-btn-ghost lgb-small', text: 'Start', disabled: state.acting || state.loading,
                'aria-label': 'Start game ' + g.number, onclick: function () { startGame(g); }
            }));
        }
        if (g.status === 'live' && cfg.canFinish) {
            acts.appendChild(h('button', {
                type: 'button', class: 'lg-btn lgb-small', text: 'Finish', disabled: state.acting || state.loading,
                'aria-label': 'Finish game ' + g.number, onclick: function () { finishDlg(g); }
            }));
        }
        return h('li', { class: 'lgcb-game' }, [h('b', { text: 'Game ' + g.number }), gameBadge(g), acts]);
    }

    function gamesCard() {
        var max = cfg.maxGames || 4;
        var mine = myGames();
        var card = h('section', { class: 'lg-card', 'aria-label': 'Games' }, [
            h('div', { class: 'lgb-head' }, [
                h('h2', { class: 'lg-h2', text: state.teams.length > 1 ? 'Games · ' + activeTeamName() : 'Games' }),
                h('span', { class: 'lgb-count', text: mine.length + ' of ' + max })
            ])
        ]);
        if (state.teams.length > 1 && mine.length) {
            card.appendChild(h('p', { class: 'lgcb-note', text: activeTeamName() + ' record: ' + teamRecord(state.activeTeam) }));
        }
        if (!mine.length) {
            card.appendChild(h('p', { class: 'lgcb-note', text: 'No game saved yet. Build the team, then press Save game.' }));
        } else {
            card.appendChild(h('ul', { class: 'lgcb-games' }, mine.map(gameRow)));
        }
        var why = saveBlock();
        var hint = capHint();
        if (hint) card.appendChild(h('p', { class: 'lgcb-hint', role: 'status', text: hint }));
        // The hint already says it, so the plain reason is skipped once the cap is reached.
        if (why && mine.length && state.eventStatus !== 'done' && !hint) card.appendChild(h('p', { class: 'lgcb-note', text: why }));
        if (state.eventStatus === 'done') {
            card.appendChild(h('p', { class: 'lgcb-note', text: 'This clash has ended. Reload the page to pick another clash.' }));
        } else {
            var acts = h('div', { class: 'lgcb-end' });
            // Next game (6e4e): not drawn once the cap is reached, the hint above already says it.
            if (cfg.canNext && mine.length && !capReached()) {
                var nb = nextBlock();
                acts.appendChild(h('button', {
                    type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Next game',
                    disabled: !!nb || state.acting || state.loading,
                    title: nb || 'Load the team of the last game, to change it before saving game ' + nextNumber() + '.',
                    onclick: nextGame
                }));
                // End clash shows the same reason under it, so it is not said twice.
                if (nb && !(cfg.canEnd && state.eventStatus === 'inprogress')) acts.appendChild(h('p', { class: 'lgcb-note', text: nb }));
            }
            if (cfg.canEnd && state.eventStatus === 'inprogress') {
                var block = endBlock();
                acts.appendChild(h('button', {
                    type: 'button', class: 'lg-btn lg-btn-ghost', text: 'End clash',
                    disabled: !!block || state.acting || state.loading, title: block || 'Mark this clash as Done.',
                    onclick: endDlg
                }));
                if (block) acts.appendChild(h('p', { class: 'lgcb-note', text: block }));
            }
            if (acts.childNodes.length) card.appendChild(acts);
        }
        // 12d: Progress image (also after the clash has ended). Enabled once one game of this team is finished.
        if (cfg.canProgress && cfg.progressUrl && window.LeagueResultImage) {
            var doneCount = mine.filter(function (g) { return g.status === 'done'; }).length;
            var progress = h('div', { class: 'lgcb-end' }, [h('button', {
                type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Progress image',
                disabled: !doneCount || state.acting || state.loading,
                title: doneCount ? 'Draw the series so far as a picture to paste into Discord.' : 'Finish game 1 first.',
                onclick: showProgressImage
            })]);
            card.appendChild(progress);
        }
        return card;
    }

    // 12d: the server builds the data for the team on screen from its finished games, league-result-image.js draws it.
    function showProgressImage() {
        if (!window.LeagueResultImage || state.activeTeam === null) return;
        var url = cfg.progressUrl + (cfg.progressUrl.indexOf('?') < 0 ? '?' : '&') + 'eventId=' + encodeURIComponent(cfg.eventId) + '&teamId=' + encodeURIComponent(state.activeTeam);
        window.LeagueResultImage.openClash(url);
    }

    function resultCard() {
        var r = state.result;
        var card = h('section', { class: 'lg-card lgb-result' });

        var acts = [];
        if (state.noFit.length) acts.push(h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'No-fit details', onclick: showNoFit }));
        if (cfg.canSubmit) {
            acts.push(h('button', {
                type: 'button', class: 'lg-btn', text: 'Save as game ' + nextNumber(),
                disabled: state.loading || state.acting || !!saveBlock(), title: saveBlock() || 'Save this team as the next game of the series.',
                onclick: saveGame
            }));
        }
        acts.push(h('button', {
            type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Export for Discord',
            disabled: state.loading || state.acting || !window.LeagueExport || !exportSpec(),
            title: window.LeagueExport ? 'Draw the team as a picture to paste into Discord.' : 'The picture script did not load.',
            onclick: showExport
        }));
        acts.push(h('button', {
            type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Set best team', disabled: state.loading || state.acting,
            title: 'Build the best team from the players who are free.',
            onclick: setBest
        }));
        card.appendChild(h('div', { class: 'lgb-head' }, [
            h('h2', { class: 'lg-h2', text: activeTeamName() }),
            h('span', { class: 'lgb-acts' }, [
                r && !r.isEmpty ? h('span', { class: 'lgb-str', title: 'Sum of the players’ strength', text: 'Strength ' + Number(r.strength).toFixed(1) }) : null
            ].concat(acts))
        ]));

        if (state.error) card.appendChild(banner('is-err', state.error));
        state.warnings.forEach(function (w) { card.appendChild(banner('is-warn', w)); });

        if (!r) {
            card.appendChild(h('div', { class: 'lgb-empty', text: state.loading ? 'Building the team…' : 'The team is not built yet.' }));
            return card;
        }
        if (r.isEmpty && state.noneFree) {
            card.appendChild(banner('is-warn', 'Nobody is free to build a team from. Open the slots by hand or mark fewer players busy.'));
        } else if (r.isEmpty && !state.loading) {
            card.appendChild(h('p', { class: 'lgcb-note', text: 'Pick the players for the five slots, or press Set best team.' }));
        }

        var slots = h('div', { class: 'lgcb-slots' });
        r.slots.forEach(function (s, i) { slots.appendChild(slotEl(s, i)); });
        card.appendChild(slots);
        card.appendChild(benchEl());
        var log = logEl();
        if (log) card.appendChild(log);
        return card;
    }

    updateHeader();
    if (!restoreDraft()) render();
})();
