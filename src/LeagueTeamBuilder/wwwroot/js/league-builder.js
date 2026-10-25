/* League team builder page (Step 4c + 4d). The server generates and checks (POST /League/Generate); this file only
   renders, and does the by-hand edits: swap by click or drag, remove with restore. Step 4d adds the match
   lifecycle (submit, start, finish, delete), always through POST /LeagueMatch/*; the server decides what is allowed.
   Step 4e adds "Export for Discord": the picture is drawn in the browser by league-export.js (the list, or the map with the
   players on their places); it is copied, downloaded, or, with the Send permission, posted by league-send.js.
   Everything is built with DOM calls and textContent, so a player name can never inject markup. */
(function () {
    'use strict';

    var dataEl = document.getElementById('lgb-data');
    var app = document.getElementById('lgb-app');
    var dlg = document.getElementById('lgb-dlg');
    var toastEl = document.getElementById('lgb-toast');
    if (!dataEl || !app) return;

    var cfg;
    try { cfg = JSON.parse(dataEl.textContent); } catch (e) { return; }

    var LANES = cfg.lanes || [];
    var MODES = cfg.modes || [];
    var P = {};                       // player id -> player
    (cfg.players || []).forEach(function (p) { P[p.id] = p; });

    var BALANCES = [
        { v: 1, title: 'Full random', sub: 'Shuffle everyone.' },
        { v: 2, title: 'Random, respecting lanes', sub: "Uses each player's lanes and main." },
        { v: 3, title: 'Two teams · rank + lanes', sub: 'Blends current (60%) and peak (40%) rank.' },
        { v: 4, title: 'Two teams · custom tiers', sub: 'Uses the tiers from Settings; no tier = rank.' }
    ];

    var state = {
        mode: MODES.length ? MODES[0].key : 'normal',
        balance: 2,
        avoid: true,
        withOn: true,
        roster: new Set(),
        result: null,     // { mode, balance, avoid, teams, note, conflicts, clash }
        sel: null,        // { t, s } selected slot (click-to-swap)
        removed: [],      // player ids taken out of this game
        ignored: [],      // [[a, b], ...] pairs kept against each other
        error: '',
        busy: false,
        focusKey: null,
        known: {}         // match id -> match, every match this page has seen (open ones from the server, plus any it changed)
    };
    (cfg.matches || []).forEach(function (m) { state.known[m.id] = m; });
    var CAN = cfg.can || {};
    var MURL = cfg.matchUrls || {};
    var matchBusy = false;
    var openEl;

    var refs = {};
    var resultEl;
    var seq = 0;
    var drag = null;
    var toastTimer = null;

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
    function modeByKey(k) { return MODES.filter(function (m) { return m.key === k; })[0] || MODES[0]; }

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

    function teamOf(pid) {
        if (!state.result) return -1;
        var teams = state.result.teams;
        for (var t = 0; t < teams.length; t++) {
            for (var s = 0; s < teams[t].slots.length; s++) {
                if (teams[t].slots[s].playerId === pid) return t;
            }
        }
        return -1;
    }

    /* ---------- server calls ---------- */

    function call(body) { return post(cfg.generateUrl, body); }

    function post(url, body) {
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

    // Body for every action except "generate": the result as it is on screen right now.
    function resultPayload(op, extra) {
        var r = state.result;
        var ids = [];
        var teams = r.teams.map(function (t) {
            return {
                name: t.name,
                slots: t.slots.map(function (s) {
                    if (s.playerId) ids.push(s.playerId);
                    return { lane: s.lane, playerId: s.playerId };
                })
            };
        });
        var body = {
            op: op,
            mode: r.mode,
            balance: r.balance,
            avoidClashes: r.avoid,
            playerIds: ids,
            teams: teams,
            ignored: state.ignored
        };
        if (extra) Object.keys(extra).forEach(function (k) { body[k] = extra[k]; });
        return body;
    }

    function setBusy(b) {
        state.busy = b;
        if (refs.gen) refs.gen.disabled = b;
        if (refs.reroll) refs.reroll.disabled = b || !state.result;
        app.classList.toggle('is-busy', b);
    }

    function generate() {
        if (state.busy) return;
        state.error = '';
        setBusy(true);
        call({
            op: 'generate',
            mode: state.mode,
            balance: state.balance,
            avoidClashes: state.avoid,
            playerIds: Array.from(state.roster)
        }).then(function (resp) {
            setBusy(false);
            state.removed = [];
            state.sel = null;
            state.ignored = [];
            if (!resp.ok) {
                state.result = null;
                state.error = resp.error || 'Could not generate teams.';
                renderResult();
                return;
            }
            state.result = {
                mode: resp.mode,
                balance: resp.balance,
                avoid: state.avoid,
                teams: resp.teams,
                note: resp.note || '',
                conflicts: resp.conflicts || [],
                clash: resp.clash || null
            };
            renderResult();
            toast('Teams generated');
            if (state.result.clash) showClash(state.result.clash);
        }).catch(function (err) {
            setBusy(false);
            state.error = err.message;
            renderResult();
        });
    }

    // After a manual edit: ask the server to recheck clashes and strengths. Only the latest answer counts.
    function refresh() {
        var r = state.result;
        if (!r) return;
        var mine = ++seq;
        call(resultPayload('evaluate')).then(function (resp) {
            if (mine !== seq || state.result !== r || !resp.ok) return;
            var before = JSON.stringify([r.conflicts, r.teams.map(function (t) { return t.strength; })]);
            r.teams.forEach(function (t, i) { if (resp.teams[i]) t.strength = resp.teams[i].strength; });
            r.conflicts = resp.conflicts || [];
            r.clash = resp.clash || null;
            var after = JSON.stringify([r.conflicts, r.teams.map(function (t) { return t.strength; })]);
            if (before !== after) renderResult();
        }).catch(function () { /* the on-screen edit stands; the check just was not refreshed */ });
    }

    // Lane-clash dialog actions that change the teams on the server.
    function clashAction(op, extra, doneMsg) {
        if (state.busy || !state.result) return;
        setBusy(true);
        seq++;
        call(resultPayload(op, extra)).then(function (resp) {
            setBusy(false);
            if (!resp.ok) { toast(resp.error || 'That could not be applied.'); return; }
            applyFull(resp);
            if (doneMsg) toast(doneMsg);
            if (state.result.clash) showClash(state.result.clash); else closeDlg();
        }).catch(function (err) { setBusy(false); toast(err.message); });
    }

    function applyFull(resp) {
        var r = state.result;
        r.teams = resp.teams;
        r.conflicts = resp.conflicts || [];
        r.clash = resp.clash || null;
        state.sel = null;
        renderResult();
    }

    /* ---------- left column: mode, roster, balance ---------- */

    function currentMode() { return modeByKey(state.mode); }

    function updateLeft() {
        var m = currentMode();
        var n = state.roster.size;
        if (refs.count) {
            refs.count.textContent = n + ' / ' + m.cap;
            refs.count.classList.toggle('is-over', n > m.cap);
        }
        if (refs.avoid) {
            var can = state.balance === 2 && state.mode === 'normal';
            refs.avoid.disabled = !can;
            refs.avoid.checked = state.avoid;
        }
    }

    function buildLeft() {
        var left = h('div', { class: 'lgb-left' });

        // 1 - mode
        var modeBox = h('div', { class: 'lgb-radios', role: 'radiogroup', 'aria-label': 'Game mode' });
        MODES.forEach(function (m) {
            var input = h('input', {
                type: 'radio', name: 'lgb-mode', value: m.key, checked: m.key === state.mode,
                onchange: function () { state.mode = m.key; updateLeft(); }
            });
            var meta = m.cap + ' players' + (m.key === 'arena' && m.teams ? ' · ' + m.teams + ' teams' : '');
            modeBox.appendChild(h('label', { class: 'lgb-rc' }, [
                input,
                h('span', { class: 'lgb-rc-b' }, [h('b', { text: m.name }), h('em', { text: meta })])
            ]));
        });
        left.appendChild(h('section', { class: 'lg-card' }, [h('h2', { class: 'lg-h2', text: '1 · Mode' }), modeBox]));

        // 2 - roster
        refs.chips = {};
        refs.count = h('span', { class: 'lgb-count' });
        var chipBox = h('div', { class: 'lgb-chips', role: 'group', 'aria-label': 'Players for this game' });
        (cfg.players || []).forEach(function (p) {
            var input = h('input', {
                type: 'checkbox', value: String(p.id), checked: state.roster.has(p.id),
                onchange: function () {
                    if (input.checked) state.roster.add(p.id); else state.roster.delete(p.id);
                    updateLeft();
                }
            });
            refs.chips[p.id] = { input: input, label: null };
            var label = h('label', { class: 'lgb-chip', 'data-name': p.name.toLowerCase() }, [
                input,
                h('span', null, [
                    rankIcon(p.curKey),
                    p.name + (p.isGuest ? ' · guest' : '')
                ])
            ]);
            refs.chips[p.id].label = label;
            chipBox.appendChild(label);
        });
        var filter = h('input', {
            type: 'search', class: 'lg-input lgb-filter', placeholder: 'Filter players', 'aria-label': 'Filter players',
            oninput: function () {
                var q = filter.value.trim().toLowerCase();
                (cfg.players || []).forEach(function (p) {
                    refs.chips[p.id].label.hidden = q !== '' && p.name.toLowerCase().indexOf(q) < 0;
                });
            }
        });
        var clear = h('button', {
            type: 'button', class: 'lg-btn lg-btn-ghost lgb-small', text: 'Clear',
            onclick: function () {
                state.roster.clear();
                Object.keys(refs.chips).forEach(function (id) { refs.chips[id].input.checked = false; });
                updateLeft();
            }
        });
        var rosterBody = (cfg.players || []).length
            ? [h('div', { class: 'lgb-rowtools' }, [filter, clear]), chipBox]
            : [h('p', { class: 'lg-sub', text: 'No profiles yet.' })];
        if (cfg.guestsUrl) {
            rosterBody.push(h('p', { class: 'lg-sub lgb-guestlink' }, [
                'Need a guest? ', h('a', { href: cfg.guestsUrl, text: 'Add one in Settings > Guests' }), '.'
            ]));
        }
        left.appendChild(h('section', { class: 'lg-card' }, [
            h('div', { class: 'lgb-head' }, [h('h2', { class: 'lg-h2', text: '2 · Roster' }), refs.count])
        ].concat(rosterBody)));

        // 3 - balance
        var balBox = h('div', { class: 'lgb-radios', role: 'radiogroup', 'aria-label': 'Balancing mode' });
        BALANCES.forEach(function (b) {
            var input = h('input', {
                type: 'radio', name: 'lgb-bal', value: String(b.v), checked: state.balance === b.v,
                onchange: function () { state.balance = b.v; updateLeft(); }
            });
            balBox.appendChild(h('label', { class: 'lgb-rc' }, [
                input,
                h('span', { class: 'lgb-rc-b' }, [h('b', { text: 'Mode ' + b.v + ' · ' + b.title }), h('em', { text: b.sub })])
            ]));
        });
        refs.avoid = h('input', {
            type: 'checkbox', checked: state.avoid,
            onchange: function () { state.avoid = refs.avoid.checked; }
        });
        var withBox = h('input', {
            type: 'checkbox', checked: state.withOn,
            onchange: function () { state.withOn = withBox.checked; renderResult(); }
        });
        refs.gen = h('button', {
            type: 'button', class: 'lg-btn lgb-wide-btn', text: 'Generate teams',
            onclick: generate
        });
        left.appendChild(h('section', { class: 'lg-card' }, [
            h('h2', { class: 'lg-h2', text: '3 · Balance' }),
            balBox,
            h('label', { class: 'lgb-toggle' }, [
                refs.avoid,
                h('span', null, [
                    'Avoid same-lane clashes',
                    h('small', { text: 'Normal Draft, mode 2 only. Both main the lane and list fewer than 3 champions there.' })
                ])
            ]),
            h('label', { class: 'lgb-toggle' }, [
                withBox,
                h('span', null, [
                    'Show "Does better with"',
                    h('small', { text: 'Information only. It never changes the teams.' })
                ])
            ]),
            refs.gen
        ]));

        return left;
    }

    /* ---------- result ---------- */

    function banner(kind, text, extra) {
        return h('div', { class: 'lg-alert lgb-banner ' + kind, role: kind === 'is-err' ? 'alert' : 'status' }, [
            h('span', { text: text })
        ].concat(extra || []));
    }

    function renderResult() {
        if (!resultEl) return;
        var r = state.result;
        resultEl.textContent = '';

        refs.reroll = h('button', {
            type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Re-roll', disabled: state.busy || !r, onclick: generate
        });
        var exportBtn = h('button', {
            type: 'button', class: 'lg-btn', text: 'Export for Discord', disabled: !r || !window.LeagueExport, onclick: showExport
        });
        resultEl.appendChild(h('div', { class: 'lgb-head' }, [h('h2', { class: 'lg-h2', text: 'Result' }), h('span', { class: 'lgb-acts' }, [exportBtn, refs.reroll])]));

        if (state.error) resultEl.appendChild(banner('is-err', state.error));
        if (r && r.note) resultEl.appendChild(banner('is-warn', r.note));
        if (r && r.conflicts && r.conflicts.length) {
            var names = r.conflicts.map(function (c) {
                var a = P[c.a], b = P[c.b];
                return (a ? a.name : '?') + ' vs ' + (b ? b.name : '?') + ' (' + laneLabel(c.lane) + ')';
            }).join(', ');
            var n = r.conflicts.length;
            resultEl.appendChild(banner('is-warn',
                n + ' lane clash' + (n > 1 ? 'es' : '') + ' on the board: ' + names + '. Swap players to fix it, or review the options.',
                [r.clash ? h('button', { type: 'button', class: 'lg-btn lg-btn-ghost lgb-small', text: 'Review options', onclick: function () { showClash(r.clash); } }) : null]));
        }

        if (!r) {
            var msg = (cfg.players || []).length
                ? 'Pick a mode, tick the players, choose a balance, then press Generate teams.'
                : 'Create some league profiles first.';
            resultEl.appendChild(h('div', { class: 'lgb-empty', text: msg }));
        } else {
            resultEl.appendChild(board(r));
            resultEl.appendChild(h('p', { class: 'lg-sub', text: 'Press a player, then another slot to swap, or drag one onto another slot. ✕ takes someone out of the game.' }));
            var bar = matchBar(r);
            if (bar) resultEl.appendChild(bar);
            if (state.removed.length) {
                var tags = state.removed.map(function (id) {
                    var p = P[id];
                    return h('span', { class: 'lgb-tag' }, [
                        p ? p.name : '?',
                        h('button', {
                            type: 'button', 'aria-label': 'Put ' + (p ? p.name : 'player') + ' back in the game', text: '↺',
                            onclick: function () { restore(id); }
                        })
                    ]);
                });
                resultEl.appendChild(h('div', { class: 'lgb-removed' }, [h('span', { class: 'lg-lbl', text: 'Removed from this game' }), h('div', { class: 'lgb-chips' }, tags)]));
            }
        }
        restoreFocus();
    }

    function restoreFocus() {
        if (!state.focusKey) return;
        var el = resultEl.querySelector('[data-key="' + state.focusKey + '"]');
        state.focusKey = null;
        if (el) el.focus();
    }

    function board(r) {
        var mode = modeByKey(r.mode);
        var spots = (mode.key !== 'arena' && mode.spots && mode.spots.length) ? mode.spots : [];

        if (spots.length) {
            var asp = mode.mapAspect || (16 / 9);
            var map = h('div', {
                class: 'lgb-map' + (mode.mapUrl ? ' has-img' : ''),
                style: 'aspect-ratio:' + asp + ';width:min(100%,calc(78vh * ' + asp + '))'
            });
            if (mode.mapUrl) map.style.backgroundImage = cssUrl(mode.mapUrl);
            spots.forEach(function (sp) {
                var pin = pinEl(sp, r);
                if (pin) map.appendChild(pin);
            });
            return h('div', null, [map, teamsEl(r, 'lgb-teams lgb-teams-below')]);
        }

        var stage = h('div', { class: 'lgb-stage' + (mode.mapUrl ? ' has-img' : '') + (mode.key === 'arena' ? ' is-arena' : '') });
        if (mode.mapUrl) stage.style.backgroundImage = cssUrl(mode.mapUrl);
        stage.appendChild(teamsEl(r, 'lgb-teams'));
        return stage;
    }

    function teamsEl(r, cls) {
        return h('div', { class: cls }, r.teams.map(function (t, ti) {
            var filled = t.slots.filter(function (s) { return s.playerId; }).length;
            var head = h('header', null, [
                h('b', { text: t.name }),
                (t.strength !== null && t.strength !== undefined)
                    ? h('span', { class: 'lgb-str', title: 'Team strength', text: String(Math.round(t.strength)) })
                    : h('small', { text: filled + '/' + t.slots.length })
            ]);
            return h('div', { class: 'lgb-team t' + (ti % 2) }, [head].concat(t.slots.map(function (s, si) {
                return slotEl(ti, si, s, r);
            })));
        }));
    }

    function slotEl(t, s, slot, r) {
        var p = slot.playerId ? P[slot.playerId] : null;
        var sel = !!(state.sel && state.sel.t === t && state.sel.s === s);
        var key = t + ':' + s;
        var el = h('div', {
            class: 'lgb-slot' + (p ? '' : ' is-empty') + (sel ? ' is-sel' : ''),
            'data-t': String(t), 'data-s': String(s), draggable: p ? 'true' : null
        });
        if (slot.lane !== null && slot.lane !== undefined) el.appendChild(h('span', { class: 'lgb-ln', text: laneLabel(slot.lane) }));

        if (!p) {
            el.appendChild(h('button', {
                type: 'button', class: 'lgb-sl', 'data-key': key,
                'aria-label': 'Open slot' + (slot.lane !== null && slot.lane !== undefined ? ', ' + laneLabel(slot.lane) : '') + (state.sel ? '. Move the selected player here.' : ''),
                onclick: function () { onSlot(t, s); }
            }, [h('small', { text: 'Open slot' })]));
            return el;
        }

        var fit = null;
        if (slot.lane !== null && slot.lane !== undefined) {
            fit = p.main === slot.lane ? ['Main', 'ok'] : (p.lanes.indexOf(slot.lane) >= 0 ? ['Flex', 'warn'] : ['Off-lane', 'warn']);
        }
        var sub = [rankIcon(p.curKey), p.curLabel];
        if (r.balance >= 3) { sub.push(' · peak '); sub.push(rankIcon(p.peakKey)); sub.push(p.peakLabel); }
        var partner = (state.withOn && p.doBetterWithId && state.roster.has(p.doBetterWithId)) ? P[p.doBetterWithId] : null;
        if (partner) sub.push(' · ↔ ' + partner.name + (teamOf(partner.id) === t ? ' (same team)' : ''));

        el.appendChild(h('button', {
            type: 'button', class: 'lgb-sl', 'data-key': key, 'aria-pressed': String(sel),
            'aria-label': p.name + (slot.lane !== null && slot.lane !== undefined ? ', ' + laneLabel(slot.lane) : '') + '. ' +
                (sel ? 'Selected. Press another slot to swap.' : 'Press to select, then press another slot to swap.'),
            onclick: function () { onSlot(t, s); }
        }, [
            h('b', null, [p.name, p.rankKing ? badgeEl('rank') : null, p.mvpKing ? badgeEl('mvp') : null, p.clashKing ? badgeEl('clash') : null,
                fit ? h('span', { class: 'lgb-fit ' + fit[1], text: fit[0] }) : null]),
            h('small', null, sub)
        ]));
        el.appendChild(h('button', {
            type: 'button', class: 'lg-btn lg-btn-ghost lgb-x', text: '✕',
            'aria-label': 'Remove ' + p.name + ' from the game',
            onclick: function () { removeAt(t, s); }
        }));
        return el;
    }

    function pinEl(sp, r) {
        var team = r.teams[sp.team];
        var slot = team && team.slots[sp.slot];
        if (!slot) return null;
        var p = slot.playerId ? P[slot.playerId] : null;
        var sel = !!(state.sel && state.sel.t === sp.team && state.sel.s === sp.slot);
        var lane = (slot.lane !== null && slot.lane !== undefined) ? laneLabel(slot.lane) : '#' + (sp.slot + 1);
        var btn = h('button', {
            type: 'button', class: 'lgb-pinbtn', 'data-key': 'p' + sp.team + ':' + sp.slot,
            'aria-label': p ? p.name + ', ' + lane + '. Press, then press another circle to swap.' : 'Open slot ' + lane + '. Press to move the selected player here.',
            onclick: function () { onSlot(sp.team, sp.slot); }
        }, [p ? avatar(p, 'lgb-av') : h('span', { class: 'lgb-av', text: '+', 'aria-hidden': 'true' })]);
        return h('div', {
            class: 'lgb-pin t' + sp.team + (p ? '' : ' is-empty') + (p && p.isGuest ? ' is-guest' : '') + (sel ? ' is-sel' : ''),
            style: 'left:' + sp.x + '%;top:' + sp.y + '%',
            'data-t': String(sp.team), 'data-s': String(sp.slot), draggable: p ? 'true' : null
        }, [btn, h('span', { class: 'lgb-pn', text: p ? p.name : 'Open' }), h('span', { class: 'lgb-pl', text: lane })]);
    }

    /* ---------- by-hand edits ---------- */

    function slotKey(t, s) {
        // Focus goes back to the same place: the slot button, or the pin when the board has a map.
        var pin = resultEl.querySelector('.lgb-pin[data-t="' + t + '"][data-s="' + s + '"]');
        return pin ? 'p' + t + ':' + s : t + ':' + s;
    }

    function onSlot(t, s) {
        if (state.busy || !state.result) return;
        var slot = state.result.teams[t].slots[s];
        if (!state.sel) {
            if (slot.playerId) { state.sel = { t: t, s: s }; state.focusKey = slotKey(t, s); renderResult(); }
            return;
        }
        if (state.sel.t === t && state.sel.s === s) { state.sel = null; state.focusKey = slotKey(t, s); renderResult(); return; }
        swapSlots(state.sel, { t: t, s: s });
        state.sel = null;
        state.focusKey = slotKey(t, s);
        renderResult();
        toast('Swapped');
        refresh();
    }

    function swapSlots(a, b) {
        var teams = state.result.teams;
        var x = teams[a.t].slots[a.s], y = teams[b.t].slots[b.s];
        var tmp = x.playerId;
        x.playerId = y.playerId;
        y.playerId = tmp;
    }

    function removeAt(t, s) {
        if (state.busy || !state.result) return;
        var slot = state.result.teams[t].slots[s];
        if (!slot.playerId) return;
        var id = slot.playerId;
        state.removed.push(id);
        state.roster.delete(id);
        if (refs.chips[id]) refs.chips[id].input.checked = false;
        slot.playerId = null;
        state.sel = null;
        updateLeft();
        renderResult();
        refresh();
    }

    function restore(id) {
        if (state.busy || !state.result) return;
        var teams = state.result.teams;
        for (var t = 0; t < teams.length; t++) {
            for (var s = 0; s < teams[t].slots.length; s++) {
                if (!teams[t].slots[s].playerId) {
                    teams[t].slots[s].playerId = id;
                    state.roster.add(id);
                    if (refs.chips[id]) refs.chips[id].input.checked = true;
                    state.removed = state.removed.filter(function (x) { return x !== id; });
                    updateLeft();
                    renderResult();
                    refresh();
                    return;
                }
            }
        }
        toast('No open slot left');
    }

    /* ---------- drag and drop (click-to-swap above is the accessible path) ---------- */

    function clearOver() {
        Array.prototype.forEach.call(document.querySelectorAll('.lgb-slot.is-over, .lgb-pin.is-over'), function (x) { x.classList.remove('is-over'); });
    }

    function wireDrag(root) {
        root.addEventListener('dragstart', function (ev) {
            var el = ev.target && ev.target.closest ? ev.target.closest('.lgb-slot[draggable="true"], .lgb-pin[draggable="true"]') : null;
            if (!el || state.busy) return;
            drag = { t: +el.dataset.t, s: +el.dataset.s };
            ev.dataTransfer.setData('text/plain', 'x');
            ev.dataTransfer.effectAllowed = 'move';
        });
        root.addEventListener('dragover', function (ev) {
            if (!drag) return;
            var el = ev.target.closest ? ev.target.closest('.lgb-slot, .lgb-pin') : null;
            if (!el) return;
            ev.preventDefault();
            clearOver();
            el.classList.add('is-over');
        });
        root.addEventListener('drop', function (ev) {
            if (!drag) return;
            var el = ev.target.closest ? ev.target.closest('.lgb-slot, .lgb-pin') : null;
            if (!el) return;
            ev.preventDefault();
            var to = { t: +el.dataset.t, s: +el.dataset.s };
            var from = drag;
            drag = null;
            clearOver();
            if (to.t === from.t && to.s === from.s) return;
            swapSlots(from, to);
            state.sel = null;
            renderResult();
            toast('Swapped');
            refresh();
        });
        root.addEventListener('dragend', function () { drag = null; clearOver(); });
    }

    /* ---------- lane-clash dialog ---------- */

    function closeDlg() {
        if (dlg && dlg.open && typeof dlg.close === 'function') dlg.close();
        else if (dlg) dlg.removeAttribute('open');
    }

    function showClash(c) {
        var r = state.result;
        var a = P[c.a], b = P[c.b];
        if (!dlg || !r || !a || !b) return;
        var lane = laneLabel(c.lane);
        dlg.textContent = '';

        function card(p) {
            var tm = teamOf(p.id);
            return h('div', null, [
                h('b', { text: p.name }),
                h('small', { text: (tm >= 0 ? r.teams[tm].name + ' · ' : '') + 'main ' + lane }),
                h('small', { text: p.mainPool + ' champion' + (p.mainPool === 1 ? '' : 's') + ' listed on ' + lane })
            ]);
        }

        var swapSel = null;
        var swapBlock;
        if (c.swaps && c.swaps.length) {
            swapSel = h('select', { class: 'lg-select', 'aria-label': 'Player and secondary lane' },
                c.swaps.map(function (sw, i) {
                    var p = P[sw.playerId];
                    return h('option', { value: String(i), text: (p ? p.name : '?') + ' → ' + laneLabel(sw.lane) });
                }));
            swapBlock = h('div', { class: 'lgb-opt lgb-opt-static' }, [
                h('b', { text: '2 · Swap one to a secondary lane' }),
                h('small', { text: 'You pick who and which lane.' }),
                h('div', { class: 'lgb-opt-row' }, [
                    swapSel,
                    h('button', {
                        type: 'button', class: 'lg-btn', text: 'Apply swap',
                        onclick: function () {
                            var sw = c.swaps[+swapSel.value];
                            clashAction('clashSwap', { clash: { lane: c.lane, a: c.a, b: c.b }, swap: { playerId: sw.playerId, lane: sw.lane } }, 'Swapped to the secondary lane');
                        }
                    })
                ])
            ]);
        } else {
            swapBlock = h('div', { class: 'lgb-opt lgb-opt-static' }, [
                h('b', { text: '2 · Swap one to a secondary lane' }),
                h('small', { text: 'Neither player lists a secondary lane.' })
            ]);
        }

        var moved = c.movedPlayerId ? P[c.movedPlayerId] : null;
        var sameBtn = h('button', {
            type: 'button', class: 'lgb-opt', disabled: !c.canSameTeam,
            onclick: function () { clashAction('clashSame', { clash: { lane: c.lane, a: c.a, b: c.b } }, 'Moved to the same team'); }
        }, [
            h('b', { text: '1 · Put both on the same team' }),
            h('small', {
                text: c.canSameTeam
                    ? 'One of them plays a secondary lane there' + (moved ? '; ' + moved.name + ' moves across to keep the sizes equal.' : '.')
                    : 'Not offered: neither has a usable secondary lane.'
            })
        ]);

        var keepBtn = h('button', {
            type: 'button', class: 'lgb-opt',
            onclick: function () {
                state.ignored.push([c.a, c.b]);
                closeDlg();
                keepAndNext();
            }
        }, [h('b', { text: '3 · Keep them against each other' }), h('small', { text: 'Ignore the flag for this pair.' })]);

        var stopBtn = h('button', {
            type: 'button', class: 'lgb-opt',
            onclick: function () { closeDlg(); toast('Drag players to fix it yourself'); }
        }, [h('b', { text: "4 · Stop, I'll fix it by hand" }), h('small', { text: 'Close this and swap players on the board.' })]);

        dlg.appendChild(h('div', { class: 'lgb-dlg-in' }, [
            h('h3', { id: 'lgb-dlg-title', text: 'Lane clash: ' + lane }),
            h('p', { class: 'lg-sub', text: 'Both main ' + lane + ' and both list fewer than 3 champions there. Reshuffling alone cannot avoid it. Pick what to do:' }),
            h('div', { class: 'lgb-pair' }, [card(a), card(b)]),
            sameBtn, swapBlock, keepBtn, stopBtn
        ]));

        if (!dlg.open) {
            if (typeof dlg.showModal === 'function') dlg.showModal(); else dlg.setAttribute('open', '');
        }
    }

    // "Keep them against each other": the server forgets this pair and tells us about the next clash, if any.
    function keepAndNext() {
        var r = state.result;
        if (!r) return;
        seq++;
        call(resultPayload('evaluate')).then(function (resp) {
            if (state.result !== r || !resp.ok) return;
            r.teams.forEach(function (t, i) { if (resp.teams[i]) t.strength = resp.teams[i].strength; });
            r.conflicts = resp.conflicts || [];
            r.clash = resp.clash || null;
            renderResult();
            if (r.clash) showClash(r.clash);
        }).catch(function (err) { toast(err.message); });
    }


    /* ---------- matches (Step 4d): submit -> start -> finish, delete ---------- */

    function timeText(iso) {
        var d = iso ? new Date(iso) : null;
        return d && !isNaN(d.getTime()) ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
    }
    function dateTimeText(iso) {
        var d = iso ? new Date(iso) : null;
        return d && !isNaN(d.getTime()) ? d.toLocaleString([], { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '';
    }

    function openList() {
        return Object.keys(state.known).map(function (k) { return state.known[k]; })
            .filter(function (m) { return m.status !== 'done'; })
            .sort(function (a, b) { return a.submittedUtc < b.submittedUtc ? 1 : a.submittedUtc > b.submittedUtc ? -1 : 0; });
    }

    function renderMatchParts() {
        renderResult();
        renderOpen();
    }

    // One server call for the match lifecycle. Resolves to the response, or null when it failed (a toast says why).
    function matchCall(url, body, okMsg) {
        if (matchBusy) return Promise.resolve(null);
        matchBusy = true;
        renderMatchParts();
        return post(url, body).then(function (resp) {
            matchBusy = false;
            if (!resp.ok) {
                toast(resp.error || 'That did not work.');
                renderMatchParts();
                return null;
            }
            if (resp.deleted) delete state.known[body.id];
            else if (resp.match) state.known[resp.match.id] = resp.match;
            if (okMsg) toast(okMsg);
            renderMatchParts();
            return resp;
        }).catch(function (err) {
            matchBusy = false;
            toast(err.message);
            renderMatchParts();
            return null;
        });
    }

    function submitMatch() {
        var r = state.result;
        if (!r || matchBusy) return;
        var p = resultPayload('evaluate');
        matchCall(MURL.submit, { mode: p.mode, balance: p.balance, teams: p.teams }, 'Teams submitted').then(function (resp) {
            if (resp && resp.match && state.result === r) {
                r.matchId = resp.match.id;
                renderResult();
            }
        });
    }

    function startMatch(m) { matchCall(MURL.start, { id: m.id }, 'Match started'); }

    function deleteMatch(m) {
        var msg = m.status === 'done'
            ? 'Delete this finished match? Its result is removed for good.'
            : 'Delete this match? It is removed for good.';
        if (!window.confirm(msg)) return;
        matchCall(MURL['delete'], { id: m.id }, 'Match deleted').then(function (resp) {
            if (resp && state.result && state.result.matchId === m.id) {
                state.result.matchId = null;
                renderResult();
            }
        });
    }

    function winnerText(m) {
        var w = null;
        m.teams.forEach(function (t) { if (t.index === m.winner) w = t; });
        if (!w) return 'Finished.';
        var txt = 'Winner: ' + w.name + '.';
        if (m.kills0 != null || m.kills1 != null) {
            txt += ' Team kills ' + (m.kills0 == null ? '?' : m.kills0) + '–' + (m.kills1 == null ? '?' : m.kills1) + '.';
        }
        return txt;
    }

    function statusText(m) {
        if (m.status === 'submitted') return 'Submitted at ' + timeText(m.submittedUtc) + '. Later changes on the board do not affect this match.';
        if (m.status === 'live') return 'Started at ' + timeText(m.startedUtc) + '. Finish it to save the result.';
        return winnerText(m);
    }

    function matchButtons(m) {
        var out = [];
        if (m.status === 'submitted' && CAN.start) {
            out.push(h('button', { type: 'button', class: 'lg-btn', text: 'Start match', disabled: matchBusy, onclick: function () { startMatch(m); } }));
        }
        if (m.status === 'live' && CAN.finish) {
            out.push(h('button', { type: 'button', class: 'lg-btn', text: 'Finish match', disabled: matchBusy, onclick: function () { showFinish(m); } }));
        }
        if (CAN['delete']) {
            out.push(h('button', { type: 'button', class: 'lg-btn lg-btn-danger lgb-small', text: 'Delete', disabled: matchBusy, onclick: function () { deleteMatch(m); } }));
        }
        return out;
    }

    function pill(m) {
        var label = m.status === 'submitted' ? 'Submitted' : m.status === 'live' ? 'Live' : 'Saved';
        return h('span', { class: 'lgb-pill is-' + m.status, text: label });
    }

    // The bar under the board: Submit teams, then the state of the match made from this result.
    function matchBar(r) {
        var m = r.matchId ? state.known[r.matchId] : null;
        if (!m) {
            if (!CAN.submit) return null;
            return h('div', { class: 'lg-alert lgb-banner' }, [
                h('span', { text: 'Happy with the teams? Submit them, then start the match when everyone is in the lobby.' }),
                h('button', { type: 'button', class: 'lg-btn', text: 'Submit teams', disabled: matchBusy, onclick: submitMatch })
            ]);
        }
        return h('div', { class: 'lg-alert lgb-banner' }, [
            h('span', null, [pill(m), ' ' + statusText(m)]),
            h('span', { class: 'lgb-acts' }, matchButtons(m))
        ]);
    }

    function matchCard(m) {
        var teams = m.teams.map(function (t) {
            var names = t.players.map(function (p) {
                return (p.lane != null ? laneLabel(p.lane) + ' ' : '') + p.name + (p.isGuest ? ' (guest)' : '');
            }).join(', ');
            return h('p', { class: 'lgb-mteam' }, [h('b', { text: t.name + ': ' }), names]);
        });
        return h('div', { class: 'lgb-mcard' }, [
            h('div', { class: 'lgb-mhead' }, [
                h('span', null, [pill(m), ' ' + m.modeName + ' · ' + dateTimeText(m.submittedUtc)]),
                h('span', { class: 'lgb-acts' }, matchButtons(m))
            ]),
            h('p', { class: 'lg-sub', text: statusText(m) })
        ].concat(teams));
    }

    function renderOpen() {
        if (!openEl) return;
        openEl.textContent = '';
        var list = openList();
        openEl.hidden = list.length === 0;
        if (!list.length) return;
        openEl.appendChild(h('h2', { class: 'lg-h2', text: 'Open matches' }));
        list.forEach(function (m) { openEl.appendChild(matchCard(m)); });
    }

    function showFinish(m) {
        if (!dlg || matchBusy) return;
        dlg.textContent = '';
        var two = m.teams.length === 2;

        var win = h('select', { id: 'lgb-win', class: 'lg-select' }, [h('option', { value: '', text: 'Choose the winner' })]
            .concat(m.teams.map(function (t) { return h('option', { value: String(t.index), text: t.name + ' won' }); })));

        function num(id, t) {
            return h('input', { type: 'number', id: id, class: 'lg-input', min: '0', max: '999', inputmode: 'numeric',
                'aria-label': 'Team kills for ' + t.name, placeholder: '0' });
        }
        var k0 = two ? num('lgb-k0', m.teams[0]) : null;
        var k1 = two ? num('lgb-k1', m.teams[1]) : null;

        // Results/export step 1: "Played on" (Cairo time), only for someone with LeagueMatch > EditDate. Left as it is =
        // "now" (the server uses its own clock); only a changed value is sent.
        var played = null;
        var playedStart = '';
        if (CAN.editDate && window.LeagueTime) {
            playedStart = window.LeagueTime.inputValue(new Date());
            played = h('input', { type: 'datetime-local', id: 'lgb-played', class: 'lg-input', value: playedStart, max: playedStart });
        }

        var err = h('p', { class: 'lgb-dlg-err', role: 'alert' });

        function intOrNull(el) {
            var v = el ? el.value.trim() : '';
            return v === '' ? null : parseInt(v, 10);
        }

        var save = h('button', {
            type: 'button', class: 'lg-btn', text: 'Save match',
            onclick: function () {
                err.textContent = '';
                if (win.value === '') { err.textContent = 'Choose which team won.'; win.focus(); return; }
                matchCall(MURL.finish, {
                    id: m.id, winner: parseInt(win.value, 10), kills0: intOrNull(k0), kills1: intOrNull(k1),
                    playedOn: played && played.value && played.value !== playedStart ? played.value : null
                }, 'Match saved').then(function (resp) {
                    if (!resp) return;
                    closeDlg();
                    // Step 12h2: the result is saved; post the Game result picture to Discord when the owner switched auto-send on.
                    if (window.LeagueAutoSend && resp.match) {
                        window.LeagueAutoSend.gameResult(resp.match.id, function (t) { toast(t, 7000); });
                    }
                });
            }
        });

        var kids = [
            h('h3', { id: 'lgb-dlg-title', text: 'Finish match' }),
            h('p', { class: 'lg-sub', text: 'Only the winner is required. Kills, deaths, assists and champions per player can be added later.' }),
            h('label', { class: 'lg-lbl', for: 'lgb-win', text: 'Winner' }),
            win
        ];
        if (two) {
            kids.push(h('span', { class: 'lg-lbl lgb-gap', text: 'Team kills (optional)' }));
            kids.push(h('div', { class: 'lgb-kills' }, [
                h('label', null, [m.teams[0].name, k0]),
                h('label', null, [m.teams[1].name, k1])
            ]));
        }
        if (played) {
            kids.push(h('label', { class: 'lg-lbl lgb-gap', for: 'lgb-played', text: 'Played on (Cairo time)' }));
            kids.push(played);
            kids.push(h('p', { class: 'lg-sub', text: 'Entering an old match? Set the real date and time. Today\'s and yesterday\'s exports use this date.' }));
        }
        kids.push(err);
        kids.push(h('div', { class: 'lgb-dlg-acts' }, [
            h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Cancel', onclick: closeDlg }),
            save
        ]));
        dlg.appendChild(h('div', { class: 'lgb-dlg-in' }, kids));

        if (!dlg.open) {
            if (typeof dlg.showModal === 'function') dlg.showModal(); else dlg.setAttribute('open', '');
        }
        win.focus();
    }

    /* ---------- export for Discord (Step 4e, drawing is in league-export.js; map choice + Send: PLAN-leaderboard-export.md E3) ---------- */

    // The Map picture needs player places in the settings (Arena has none) and the drawMap of league-export.js.
    function hasMapPicture(mode) {
        return !!(window.LeagueExport && typeof window.LeagueExport.drawMap === 'function' &&
            mode && mode.key !== 'arena' && mode.spots && mode.spots.length);
    }

    // kind 'list' = the picture Export has always drawn; 'map' = the map with the players on their places.
    // Rows carry slot, avatar and guest for the map; the list picture ignores them.
    function exportSpec(kind) {
        var r = state.result;
        var mode = modeByKey(r.mode);
        var onMap = kind === 'map';
        var teams = r.teams.map(function (t) {
            var rows = [];
            t.slots.forEach(function (s, si) {
                var p = s.playerId ? P[s.playerId] : null;
                // Lane modes keep an "Open slot" row; ARAM and Arena just list who is in.
                if (!p && s.lane == null) return;
                var lane = s.lane != null ? laneLabel(s.lane) : null;
                rows.push({
                    // On the map the "#n" is the slot number, like the pin on the Builder page; in the list it is the place in the list.
                    tag: lane !== null ? lane : '#' + (onMap ? si + 1 : rows.length + 1),
                    name: p ? p.name : null,
                    sub: p ? p.curLabel : '',
                    slot: si,
                    avatar: p && p.avatar ? p.avatar : null,
                    guest: !!(p && p.isGuest)
                });
            });
            return { name: t.name, rows: rows };
        });
        var date = new Date().toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
        var spec = { title: 'Custom game · ' + mode.name, subtitle: date + ' · HFECloud', mapUrl: mode.mapUrl || null, teams: teams };
        if (onMap) {
            spec.mapAspect = mode.mapAspect || (16 / 9);
            spec.spots = (mode.spots || []).map(function (sp) { return { team: sp.team, slot: sp.slot, x: sp.x, y: sp.y }; });
        }
        return spec;
    }

    function showExport() {
        if (!dlg || !state.result || !window.LeagueExport) return;
        var mode = modeByKey(state.result.mode);
        var mapOk = hasMapPicture(mode);
        var kind = mapOk ? 'map' : 'list';     // the map is the first choice when the mode has places
        var canSend = !!(window.LeagueSend && window.LeagueSend.canSend && window.LeagueSend.canSend());
        var drawSeq = 0;                       // a late answer from an older picture is dropped
        var ready = false, sending = false, sent = false;
        dlg.textContent = '';

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

        function fileName() { return kind === 'map' ? 'custom-game-teams-map.png' : 'custom-game-teams.png'; }

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

        var kids = [
            h('h3', { id: 'lgb-dlg-title', text: 'Export for Discord' }),
            h('p', { class: 'lg-sub', text: '1280 × 720 PNG of the teams as they are on the board now. Copy it and paste it into a message, download it' + (canSend ? ', or send it to the League Discord channel.' : '.') })
        ];
        if (mapOk) {
            pickMap = h('button', { type: 'button', class: 'lg-btn lg-btn-ghost lgb-seg-b', text: 'With the map', onclick: function () { if (kind !== 'map') { kind = 'map'; render(); } } });
            pickList = h('button', { type: 'button', class: 'lg-btn lg-btn-ghost lgb-seg-b', text: 'The list', onclick: function () { if (kind !== 'list') { kind = 'list'; render(); } } });
            kids.push(h('div', { class: 'lgb-seg', role: 'group', 'aria-label': 'Which picture' }, [pickMap, pickList]));
        }
        kids.push(cv, note);
        kids.push(h('div', { class: 'lgb-dlg-acts' }, [h('button', { type: 'button', class: 'lg-btn lg-btn-ghost', text: 'Close', onclick: closeDlg }), copyBtn, dlBtn, sendBtn]));
        dlg.appendChild(h('div', { class: 'lgb-dlg-in' }, kids));
        if (!dlg.open) {
            if (typeof dlg.showModal === 'function') dlg.showModal(); else dlg.setAttribute('open', '');
        }
        render();
    }

    /* ---------- start ---------- */

    var grid = h('div', { class: 'lgb-grid' });
    grid.appendChild(buildLeft());
    resultEl = h('section', { class: 'lg-card lgb-result' });
    grid.appendChild(resultEl);
    app.appendChild(grid);
    openEl = h('section', { class: 'lg-card lgb-open' });
    app.appendChild(openEl);
    wireDrag(resultEl);
    updateLeft();
    renderResult();
    renderOpen();
})();
