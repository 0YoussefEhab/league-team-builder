(function () {
  'use strict';
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var root = document.documentElement;
  function store(k, v) { try { if (v === undefined) return localStorage.getItem(k); localStorage.setItem(k, v); } catch (e) {} return null; }

  // ---- fake data (all invented) ----
  var RK = ['Iron', 'Bronze', 'Silver', 'Gold', 'Platinum', 'Emerald', 'Diamond', 'Master'], RO = ['', 'I', 'II', 'III', 'IV'];
  var LANES = ['Top', 'Jungle', 'Mid', 'Adc', 'Support'], COST = [0, 1, 4, 6];
  // name, current rank, div, peak rank, div, lanes (best first), custom tier (0 = strongest)
  var PLAYERS = [['Nova', 3, 2, 3, 1, [0, 2], 2], ['Kestrel', 2, 1, 3, 4, [1, 0], 2], ['Orion', 5, 2, 5, 1, [2, 1], 0], ['Dax', 7, 0, 7, 0, [0, 1], 0],
    ['Mira', 4, 3, 4, 1, [1, 4], 1], ['Brisk', 3, 4, 4, 4, [2, 3], 1], ['Ember', 3, 3, 3, 1, [3, 2], 1], ['Cobalt', 2, 2, 3, 3, [4, 3], 2],
    ['Juno', 4, 4, 5, 4, [3, 4], 1], ['Lumen', 0, 1, 1, 2, [4, 0], 3], ['Pixel', 2, 3, 3, 4, [2, 0], 2], ['Quill', 1, 1, 2, 4, [1, 4], 3],
    ['Rook', 1, 3, 2, 3, [0, 1], 3], ['Sable', 0, 2, 1, 1, [4, 3], 3], ['Tavi', 1, 2, 2, 4, [2, 3], 3], ['Wren', 0, 3, 1, 3, [3, 4], 3]
  ].map(function (a, i) { return { id: i, n: a[0], cr: a[1], cd: a[2], pr: a[3], pd: a[4], ln: a[5], t: a[6], s: a[1] }; });
  function sc(r, d) { return r * 4 + (r < 7 ? 4 - d : 0); }
  function rankText(p) { return RK[p.cr] + (p.cr < 7 ? ' ' + RO[p.cd] : ''); }
  function val(p, bal) { return bal === 4 ? (3 - p.t) * 8 : bal === 3 ? .6 * sc(p.cr, p.cd) + .4 * sc(p.pr, p.pd) : sc(p.cr, p.cd); }
  function lcost(p, l) { var i = p.ln.indexOf(l); return i < 0 ? 8 : COST[i]; }
  var MODES = [['normal', 'Normal draft', 'Five lanes per team, 10 players.'], ['aram', 'ARAM', 'No lane slots, 10 players.'], ['arena', 'Arena', '16 players, 8 teams of 2.']];
  var BALS = [[1, 'Full random', 'Shuffle everyone.'], [2, 'Random, respecting lanes', 'Random teams, best lane for each player.'],
    [3, 'Two teams · rank + lanes', 'Rank (60% now, 40% peak) and lane cost.'], [4, 'Two teams · custom tiers', 'Uses tiers (Carry, Strong, Solid, Learning).']];
  var POS = { normal: [[[21.3, 21.9], [30.4, 51.1], [44.4, 50.1], [70.2, 73.2], [80.1, 80.6]], [[34.1, 12.2], [71, 40], [57.7, 40.6], [77.4, 61.9], [86.8, 69.4]]],
    aram: [[[22.1, 30.4], [29.3, 49.1], [12.3, 53], [16, 73.4], [34, 74.2]], [[61.9, 9.8], [68.1, 29.6], [75.2, 49.4], [79.7, 11.1], [86, 31.2]]] };
  var MAPIMG = { normal: 'img/normal-map.jpg', aram: 'img/aram-map.jpg' };
  var PERMS = []; (function pm(a, k) { if (k === a.length) { PERMS.push(a.slice()); return; } for (var i = k; i < a.length; i++) { var t = a[k]; a[k] = a[i]; a[i] = t; pm(a, k + 1); t = a[k]; a[k] = a[i]; a[i] = t; } })([0, 1, 2, 3, 4], 0);
  var st = { picked: [], mode: 'normal', bal: 3, teams: null, sel: null, phase: 'none', games: 0 };

  // ---- looks ----
  var looks = $('#looks');
  function setLook(n, save) {
    if (n === 'steel') root.removeAttribute('data-look'); else root.setAttribute('data-look', n);
    [].forEach.call(looks.querySelectorAll('button'), function (b) { b.setAttribute('aria-pressed', b.dataset.look === n ? 'true' : 'false'); });
    if (save) store('ltb-look', n);
  }
  looks.addEventListener('click', function (e) { var b = e.target.closest('button[data-look]'); if (b) setLook(b.dataset.look, true); });
  var sl = store('ltb-look'), lq = /[?&]look=(steel|brutal|forest)\b/.exec(location.search); if (lq) setLook(lq[1], false); else if (sl) setLook(sl, false);

  // ---- tabs ----
  var tabs = $('#tabs');
  var curTab = 'builder';
  function showTab(id) {
    curTab = id;
    ['builder', 'clash', 'history'].forEach(function (k) {
      var on = k === id;
      $('#t-' + k).setAttribute('aria-selected', on); $('#t-' + k).tabIndex = on ? 0 : -1; $('#p-' + k).hidden = !on;
    });
  }
  tabs.addEventListener('keydown', function (e) {
    var k = { ArrowRight: 1, ArrowLeft: -1, Home: 'h', End: 'e' }[e.key]; if (k === undefined) return;
    var ids = ['builder', 'clash', 'history'], i = ids.indexOf(curTab); e.preventDefault();
    i = k === 'h' ? 0 : k === 'e' ? 2 : (i + k + 3) % 3; showTab(ids[i]); $('#t-' + ids[i]).focus();
  });
  tabs.addEventListener('click', function (e) { var b = e.target.closest('[role=tab]'); if (b) showTab(b.id.slice(2)); });

  // ---- team builder ----
  var pl = $('#players'), cnt = $('#count'), msg = $('#msg'), gen = $('#gen'), out = $('#result');
  function need() { return st.mode === 'arena' ? 16 : 10; }
  PLAYERS.forEach(function (p) {
    var b = document.createElement('button'); b.type = 'button'; b.className = 'pl'; b.setAttribute('aria-pressed', 'false'); b.dataset.id = p.id;
    b.innerHTML = '<b></b><small></small>'; $('b', b).textContent = p.n; $('small', b).textContent = rankText(p) + ' · ' + LANES[p.ln[0]]; pl.appendChild(b);
  });
  function sync() {
    [].forEach.call(pl.children, function (b) { b.setAttribute('aria-pressed', st.picked.indexOf(+b.dataset.id) >= 0 ? 'true' : 'false'); });
    cnt.textContent = st.picked.length + ' / ' + need(); gen.disabled = st.picked.length !== need();
  }
  pl.addEventListener('click', function (e) {
    var b = e.target.closest('.pl'); if (!b) return; var id = +b.dataset.id, i = st.picked.indexOf(id); msg.textContent = '';
    if (i >= 0) st.picked.splice(i, 1); else if (st.picked.length < need()) st.picked.push(id); else msg.textContent = 'That is enough players. Remove one first.';
    sync();
  });
  function radios(box, name, items, key) {
    items.forEach(function (it) {
      var l = document.createElement('label'), i = document.createElement('input');
      i.type = 'radio'; i.name = name; i.value = it[0]; i.checked = st[key] === it[0];
      i.addEventListener('change', function () { st[key] = isNaN(+it[0]) ? it[0] : +it[0]; if (key === 'mode') { st.picked = []; msg.textContent = ''; sync(); } });
      var t = document.createElement('b'); t.textContent = ' ' + it[1]; var e = document.createElement('em'); e.textContent = it[2];
      l.appendChild(i); l.appendChild(t); l.appendChild(e); box.appendChild(l);
    });
  }
  radios($('#modes'), 'mode', MODES, 'mode'); radios($('#bals'), 'bal', BALS, 'bal');
  function shuf(a) { for (var i = a.length - 1; i > 0; i--) { var j = Math.floor(Math.random() * (i + 1)), t = a[i]; a[i] = a[j]; a[j] = t; } return a; }
  function bestPerm(t) { var best = null, c; PERMS.forEach(function (pm) { c = 0; for (var i = 0; i < 5; i++) c += lcost(t[i], pm[i]); if (best === null || c < best.c) best = { c: c, pm: pm }; }); return best; }
  function lanesFor(t) { var o = []; bestPerm(t).pm.forEach(function (l, i) { o[l] = t[i]; }); return o; }
  function balance() {
    var a = shuf(st.picked.map(function (id) { return PLAYERS[id]; })), m = st.mode, b = st.bal, T = [], A, B;
    if (m === 'arena') {
      if (b >= 3) a.sort(function (x, y) { return val(y, b) - val(x, b) + (Math.random() - .5) * 3; });
      for (var k = 0; k < 8; k++) T.push([b >= 3 ? a[k] : a[2 * k], b >= 3 ? a[15 - k] : a[2 * k + 1]]);
      return T.map(function (t, k) { return { name: 'Team ' + (k + 1), s: t.map(function (p) { return { p: p }; }) }; });
    }
    if (b >= 3) {
      var best = null;
      for (var mask = 1; mask < 1024; mask += 2) {
        var n = 0, x; for (x = mask; x; x &= x - 1) n++; if (n !== 5) continue;
        var ta = [], tb = []; a.forEach(function (p, i) { ((mask >> i) & 1 ? ta : tb).push(p); });
        var va = 0, vb = 0; ta.forEach(function (p) { va += val(p, b); }); tb.forEach(function (p) { vb += val(p, b); });
        var c = Math.abs(va - vb) + Math.random() * 1.2; if (m === 'normal') c += .5 * (bestPerm(ta).c + bestPerm(tb).c);
        if (!best || c < best.c) best = { c: c, a: ta, b: tb };
      }
      A = best.a; B = best.b;
    } else { A = a.slice(0, 5); B = a.slice(5); }
    return [A, B].map(function (t, k) { var o = (m === 'normal' && b !== 1) ? lanesFor(t) : t; return { name: 'Team ' + (k + 1), s: o.map(function (p) { return { p: p }; }) }; });
  }
  function ini(p) { return p.n.slice(0, 2).toUpperCase(); }
  function mapHtml(kind, teams, only) {
    var h = '<div class="map ' + kind + '" style="background-image:url(' + MAPIMG[kind] + ')">';
    teams.forEach(function (t, ti) {
      if (only !== undefined && only !== ti) return;
      t.s.forEach(function (s, si) {
        if (!s) return;
        var q = POS[kind][ti][si];
        h += '<button type="button" class="mk t' + ti + '" data-t="' + ti + '" data-s="' + si + '" style="left:' + q[0] + '%;top:' + q[1] + '%"' + (st.sel && st.sel.t === ti && st.sel.s === si ? ' aria-pressed="true"' : '') +
          '><i>' + ini(s.p) + '</i><span>' + s.p.n + (kind === 'normal' ? '<br>' + LANES[si] : '') + '</span></button>';
      });
    });
    return h + '</div><p class="mut credit">Map picture © Riot Games. Not endorsed by or affiliated with Riot Games.</p>';
  }
  function render() {
    var t = st.teams, m = st.mode, h = '', ph = st.phase;
    if (!t) { out.innerHTML = ''; return; }
    h += '<div class="full"><div class="actions" id="actions"><h2>Result</h2><span class="grow"></span>' +
      '<button type="button" class="btn" data-a="exp" id="exp">Export for Discord</button>' +
      (ph === 'draft' ? '<button type="button" class="btn" data-a="gen">Re-roll</button><button type="button" class="btn main" data-a="submit">Submit teams</button>' : '') +
      (ph === 'submitted' ? '<button type="button" class="btn main" data-a="start">Start game</button>' : '') +
      (ph === 'live' ? '<button type="button" class="btn main" data-a="finish">Finish game</button>' : '') +
      (ph === 'pick' ? '<button type="button" class="btn main" data-a="w0">Team 1 won</button><button type="button" class="btn main" data-a="w1">Team 2 won</button>' : '') +
      '</div><p class="mut" id="state" role="status">' + ({ draft: 'Press two players to swap them.', submitted: 'Teams are locked. Start the game when you are ready.',
        live: 'Game in progress.', pick: 'Who won?', done: 'Game ' + st.games + ' saved (demo only). ' + (st.win === 0 ? 'Team 1' : 'Team 2') + ' won.' }[ph] || '') + '</p>';
    if (m !== 'arena') h += mapHtml(m, t);
    h += '</div>';
    t.forEach(function (tm, ti) {
      h += '<div class="card team' + (ti ? ' b' : '') + '"><h2>' + tm.name + '</h2><ul class="rows">';
      tm.s.forEach(function (s, si) {
        h += '<li><button type="button" class="row" data-t="' + ti + '" data-s="' + si + '"' + (st.sel && st.sel.t === ti && st.sel.s === si ? ' aria-pressed="true"' : '') + '><em>' + (m === 'normal' ? LANES[si] : '#' + (si + 1)) +
          '</em><b>' + s.p.n + '</b><small>' + rankText(s.p) + '</small></button></li>';
      });
      out.className = m === 'arena' ? 'two arena' : 'two';
      h += '</ul></div>';
    });
    out.innerHTML = h;
  }
  function make() { st.teams = balance(); st.sel = null; st.phase = 'draft'; render(); }
  function swap(ti, si) {
    if (st.phase !== 'draft') { $('#state').textContent = 'Teams are locked. Press Re-roll only before you submit.'; return; }
    if (!st.sel) { st.sel = { t: ti, s: si }; } else if (st.sel.t === ti && st.sel.s === si) { st.sel = null; } else {
      var a = st.teams[st.sel.t].s[st.sel.s], b = st.teams[ti].s[si], x = a.p; a.p = b.p; b.p = x; st.sel = null;
    }
    render(); var n = $('[data-t="' + ti + '"][data-s="' + si + '"]'); if (n) n.focus();
  }
  out.addEventListener('click', function (e) {
    var a = e.target.closest('[data-a]'), s = e.target.closest('[data-s]');
    if (a) { var k = a.dataset.a;
      if (k === 'gen') make(); else if (k === 'submit') { st.phase = 'submitted'; render(); } else if (k === 'start') { st.phase = 'live'; render(); }
      else if (k === 'finish') { st.phase = 'pick'; render(); } else if (k === 'exp') exportPic();
      else { st.win = k === 'w0' ? 0 : 1; st.games++; st.phase = 'done'; render(); }
      var f = $('#actions .btn.main') || $('#exp'); if (f) f.focus(); return; }
    if (s) swap(+s.dataset.t, +s.dataset.s);
  });
  gen.addEventListener('click', make);
  function exportPic() {
    var m = st.mode, W = 1000, mh = m === 'normal' ? 863 : m === 'aram' ? 563 : 0, rows = m === 'arena' ? 4 : 5, H = 70 + mh + rows * 36 + 90;
    var c = document.createElement('canvas'); c.width = W; c.height = H; var x = c.getContext('2d'), cs = getComputedStyle(root);
    var bg = cs.getPropertyValue('--bg') || '#0e1013', ink = cs.getPropertyValue('--ink') || '#fff', A = cs.getPropertyValue('--a') || '#19d3ff', B = cs.getPropertyValue('--b') || '#ff7a1a';
    function rr(l, t, w, h, r) { x.beginPath(); if (x.roundRect) x.roundRect(l, t, w, h, r); else x.rect(l, t, w, h); }
    function draw(img) {
      x.fillStyle = bg; x.fillRect(0, 0, W, H); x.fillStyle = ink; x.font = '700 28px system-ui,sans-serif';
      x.fillText((m === 'normal' ? 'Normal draft' : m === 'aram' ? 'ARAM' : 'Arena') + (st.phase === 'done' ? ' · ' + (st.win === 0 ? 'Team 1' : 'Team 2') + ' won' : ''), 24, 44);
      if (mh) {
        if (img) x.drawImage(img, 0, 70, W, mh); else { x.fillStyle = '#171a1f'; x.fillRect(0, 70, W, mh); }
        st.teams.forEach(function (t, ti) { t.s.forEach(function (s, si) {
          var q = POS[m][ti][si], cx = q[0] / 100 * W, cy = 70 + q[1] / 100 * mh; x.fillStyle = 'rgba(14,16,19,.88)'; rr(cx - 26, cy - 26, 52, 52, 8); x.fill();
          x.strokeStyle = ti ? B : A; x.lineWidth = 3; x.stroke(); x.fillStyle = '#fff'; x.textAlign = 'center'; x.font = '700 20px system-ui,sans-serif'; x.fillText(ini(s.p), cx, cy + 7);
          x.font = '700 15px system-ui,sans-serif'; x.fillText(s.p.n, cx, cy + 44); x.textAlign = 'left'; }); });
      }
      var y0 = 70 + mh + 30; x.font = '600 20px system-ui,sans-serif';
      st.teams.forEach(function (t, ti) { var col = m === 'arena' ? (ti % 2) : ti, row = m === 'arena' ? Math.floor(ti / 2) : 0, bx = 24 + col * 490;
        if (m === 'arena') { x.fillStyle = col ? B : A; x.fillText(t.name + ': ' + t.s[0].p.n + ' + ' + t.s[1].p.n, bx, y0 + row * 36); return; }
        x.fillStyle = ti ? B : A; x.fillText(t.name, bx, y0);
        t.s.forEach(function (s, si) { x.fillStyle = ink; x.fillText((m === 'normal' ? LANES[si] + '  ' : '') + s.p.n + '  ' + rankText(s.p), bx, y0 + 34 + si * 32); }); });
      x.fillStyle = '#8d95a3'; x.font = '14px system-ui,sans-serif'; x.fillText('Demo with invented players. Not endorsed by or affiliated with Riot Games.', 24, H - 20);
      c.toBlob(function (bl) { if (!bl) return; var a = document.createElement('a'); a.href = URL.createObjectURL(bl); a.download = 'teams.png'; document.body.appendChild(a); a.click(); a.remove(); }, 'image/png');
    }
    if (!mh) { draw(null); return; }
    var im = new Image(); im.onload = function () { draw(im); }; im.onerror = function () { draw(null); }; im.src = MAPIMG[m];
  }

  // ---- Clash (one team, one side of the map) ----
  var CL = [0, 1, 2, 3, 4, 5, 6, 7], cb = $('#clash'), cl;
  function clNew() { return { busy: { 2: 1, 7: 1 }, slots: [null, null, null, null, null], games: [], ended: false, pick: false, confirm: false, note: '' }; }
  cl = clNew();
  function clLast() { return cl.games[cl.games.length - 1]; }
  function clLocked() { var g = clLast(); return cl.ended || !!(g && (g.res === 'saved' || g.res === 'live')); }
  function clRec() { var w = 0, l = 0; cl.games.forEach(function (g) { if (g.res === 'won') w++; if (g.res === 'lost') l++; }); return [w, l]; }
  function clBest() {
    var f = CL.filter(function (id) { return !cl.busy[id]; }).map(function (id) { return PLAYERS[id]; });
    if (f.length < 5) { cl.note = 'Fewer than 5 players are free.'; return; }
    f.sort(function (a, b) { return val(b, 3) - val(a, 3); });
    cl.slots = lanesFor(f.slice(0, 5)).map(function (p) { return p.id; }); cl.note = '';
  }
  function clFull() { return cl.slots.every(function (x) { return x !== null; }); }
  function fit(p, l) { var c = lcost(p, l); return c === 0 ? 'Main lane' : c === 1 ? 'Second lane' : 'Off lane'; }
  function clRender() {
    var lock = clLocked(), g = cl.games, last = clLast(), n = g.length + 1, rec = clRec(), nb = Object.keys(cl.busy).length, h = '';
    h += '<div class="card"><h2 id="cl-ans-head">1. Answers <span class="mut">' + (CL.length - nb) + ' free · ' + nb + ' busy</span></h2>' +
      '<p class="mut">Friday clash night. Each member answers free or busy. No answer counts as free. Press a name to switch it.</p><div class="players">';
    CL.forEach(function (id) { var p = PLAYERS[id], b = cl.busy[id];
      h += '<button type="button" class="pl busy" data-busy="' + id + '" aria-pressed="' + (b ? 'true' : 'false') + '"' + (lock ? ' disabled' : '') + '><b>' + p.n + '</b><small>' + (b ? 'Busy' : 'Free') + ' · ' + rankText(p) + '</small></button>'; });
    h += '</div></div><div class="card"><div class="actions"><h2 id="cl-team-head">2. Team</h2><span class="grow"></span><button type="button" class="btn" id="cl-best" data-c="best"' + (lock ? ' disabled' : '') + '>Set best team</button></div>' +
      '<p class="mut" role="status">' + (cl.note || 'Pick a player for each lane, or press Set best team.') + '</p><div class="clsplit"><ul class="rows">';
    LANES.forEach(function (ln, i) { var cur = cl.slots[i];
      h += '<li class="clrow"><label for="cl-s' + i + '"><em>' + ln + '</em></label><select id="cl-s' + i + '" data-slot="' + i + '"' + (lock ? ' disabled' : '') + '><option value="">Empty</option>';
      CL.forEach(function (id) { if (cl.busy[id] || (cl.slots.indexOf(id) >= 0 && cur !== id)) return;
        h += '<option value="' + id + '"' + (cur === id ? ' selected' : '') + '>' + PLAYERS[id].n + ' · ' + rankText(PLAYERS[id]) + '</option>'; });
      h += '</select><small>' + (cur === null ? '' : fit(PLAYERS[cur], i)) + '</small></li>'; });
    var team = { s: cl.slots.map(function (id) { return id === null ? null : { p: PLAYERS[id] }; }) };
    st.sel = null;
    h += '</ul><div id="cl-map" class="clashmap">' + mapHtml('normal', [team], 0).replace(/<button type="button"/g, '<div').replace(/<\/button>/g, '</div>') + '</div></div></div>';
    h += '<div class="card"><div class="actions"><h2 id="cl-games-head">3. Games <span class="mut">' + g.length + ' of 4</span></h2><span class="grow"></span><b id="cl-rec">Series record ' + rec[0] + ' – ' + rec[1] + '</b></div>';
    if (g.length) { h += '<ol class="glist">'; g.forEach(function (x) { h += '<li>Game ' + x.n + ' · ' + { saved: 'Saved', live: 'In progress', won: 'Won', lost: 'Lost' }[x.res] + ' <small>' +
      x.slots.map(function (id) { return PLAYERS[id].n; }).join(', ') + '</small></li>'; }); h += '</ol>'; }
    h += '<div class="actions">';
    if (cl.ended) h += '<span>Clash ended. Final series record ' + rec[0] + ' – ' + rec[1] + '.</span><span class="grow"></span><button type="button" class="btn" data-c="reset">Start a new demo clash</button>';
    else if (cl.pick) h += '<span>Game ' + last.n + ' result:</span><button type="button" class="btn main" data-c="won">Won</button><button type="button" class="btn main" data-c="lost">Lost</button>';
    else if (last && last.res === 'live') h += '<button type="button" class="btn main" data-c="finish">Finish game ' + last.n + '</button>';
    else if (last && last.res === 'saved') h += '<button type="button" class="btn main" data-c="start">Start game ' + last.n + '</button>';
    else if (g.length >= 4) h += '<span class="mut">This clash has 4 games, the most a team can have. End the clash when you are done.</span>';
    else h += '<button type="button" class="btn main" data-c="save"' + (clFull() ? '' : ' disabled title="Fill all five lanes first."') + '>Save as game ' + n + '</button>';
    var open = last && (last.res === 'saved' || last.res === 'live');
    h += '<span class="grow"></span>' + (cl.confirm ? '<span>End this clash?</span><button type="button" class="btn" data-c="endyes">Yes, end it</button><button type="button" class="btn" data-c="endno">Keep going</button>' :
      (cl.ended ? '' : '<button type="button" class="btn" id="cl-end" data-c="end"' + (!g.length || open || cl.pick ? ' disabled title="Finish a game first."' : '') + '>End clash</button>')) + '</div></div>';
    cb.innerHTML = h;
  }
  cb.addEventListener('click', function (e) {
    var b = e.target.closest('[data-busy],[data-c]'); if (!b) return; var sel, id = b.dataset.busy, c = b.dataset.c, last = clLast();
    if (id !== undefined) { sel = '[data-busy="' + id + '"]'; id = +id;
      if (cl.busy[id]) delete cl.busy[id]; else { cl.busy[id] = 1; var k = cl.slots.indexOf(id); if (k >= 0) { cl.slots[k] = null; cl.note = PLAYERS[id].n + ' is busy and was taken out of the team.'; } } }
    else { sel = '[data-c="' + c + '"]';
      if (c === 'best') clBest(); else if (c === 'save') cl.games.push({ n: cl.games.length + 1, res: 'saved', slots: cl.slots.slice() });
      else if (c === 'start') last.res = 'live'; else if (c === 'finish') cl.pick = true;
      else if (c === 'won' || c === 'lost') { last.res = c; cl.pick = false; } else if (c === 'end') cl.confirm = true;
      else if (c === 'endno') cl.confirm = false; else if (c === 'endyes') { cl.ended = true; cl.confirm = false; } else if (c === 'reset') cl = clNew(); }
    clRender(); var f = $(sel, cb) || $('.btn.main', cb) || $('#cl-best'); if (f && !f.disabled) f.focus();
  });
  cb.addEventListener('change', function (e) {
    var s = e.target.closest('select[data-slot]'); if (!s) return; cl.slots[+s.dataset.slot] = s.value === '' ? null : +s.value; cl.note = ''; clRender(); var f = $('#' + s.id, cb); if (f) f.focus();
  });
  clRender();

  // ---- History + Fill from screenshots (uses the real league-stats-import.js, unchanged) ----
  var HM = [{ id: 5, mode: 'normal', date: '24 Oct', win: 0, len: '27:14', t: [[3, 4, 2, 6, 7], [0, 1, 5, 8, 9]] },
    { id: 4, mode: 'normal', date: '23 Oct', win: 1, len: '31:02', t: [[10, 11, 14, 15, 13], [3, 4, 2, 6, 7]] },
    { id: 3, mode: 'aram', date: '22 Oct', win: 0, len: '19:40', t: [[0, 1, 2, 3, 4], [5, 6, 7, 8, 9]] },
    { id: 2, mode: 'normal', date: '20 Oct', win: 1, len: '28:45', t: [[0, 4, 5, 8, 9], [3, 1, 10, 6, 13]] },
    { id: 1, mode: 'aram', date: '19 Oct', win: 1, len: '22:10', t: [[10, 11, 12, 13, 14], [1, 2, 3, 4, 5]] }];
  var CH = ['Ahri', 'Garen', 'Jinx', 'Lee Sin', 'Thresh', 'Ashe', 'Lux', 'Darius', 'Sejuani', 'Leona'], CHL = ['Garen', 'Lee Sin', 'Ahri', 'Jinx', 'Thresh'];
  var NF = ['k', 'd', 'a', 'cs', 'dd', 'dt', 'gold', 'vis', 'obj'], NL = ['K', 'D', 'A', 'CS', 'Damage', 'Taken', 'Gold', 'Vision', 'Objective'];
  var MS = {}, hs = { mid: null, panel: null, mode: 'all', pl: 'all', draft: null, res: null, ans: '', note: '' }, hb = $('#history');
  function hm() { return HM.filter(function (m) { return m.id === hs.mid; })[0]; }
  function hrows(m) { var r = []; m.t.forEach(function (ids, ti) { ids.forEach(function (id, si) { r.push({ id: id, team: ti, si: si, lane: m.mode === 'normal' ? (LANES[si] === 'Adc' ? 'ADC' : LANES[si]) : '', p: PLAYERS[id] }); }); }); return r; }
  function mname(m) { return m.mode === 'normal' ? 'Normal draft' : 'ARAM'; }
  function nums(i) { return { k: (i * 7 + 3) % 11, d: (i * 5 + 2) % 9 + 1, a: (i * 3 + 4) % 13, cs: 120 + (i * 37) % 160, dd: 8000 + (i * 2311) % 16000, dt: 9000 + (i * 1733) % 14000, gold: 8500 + (i * 977) % 6500, vis: 8 + (i * 5) % 40, obj: 500 + (i * 1311) % 9000 }; }
  function exData(m) { return hrows(m).map(function (r, i) { var n = nums(i); n.champ = CHL[r.si % 5]; if (i === 0) n.obj = null; return { r: r, n: n }; }); }
  function exAnswer(m) {
    var ex = exData(m), tot = [[0, 0, 0], [0, 0, 0]], L = ['LTB-STATS 1', 'LENGTH ' + m.len];
    ex.forEach(function (e) { var t = tot[e.r.team]; t[0] += e.n.k; t[1] += e.n.d; t[2] += e.n.a; });
    tot.forEach(function (t, i) { L.push('TOTALS | Team ' + (i + 1) + ' | ' + t.join(' | ')); });
    ex.forEach(function (e) { var n = e.n; L.push([e.r.p.n, e.n.champ, e.r.lane].concat(NF.map(function (f) { return n[f] === null ? '' : n[f]; })).join(' | ')); });
    return L.join('\n');
  }
  function hSel(id, label, opts, cur) { return '<label>' + label + ' <select id="' + id + '">' + opts.map(function (o) { return '<option value="' + o[0] + '"' + (String(o[0]) === String(cur) ? ' selected' : '') + '>' + o[1] + '</option>'; }).join('') + '</select></label>'; }
  function readyHtml(m) {
    var d = MS[m.id]; if (!d) return '';
    var teams = m.t.map(function (ids, ti) { return { name: 'Team ' + (ti + 1), players: ids.map(function (id) { var o = { name: PLAYERS[id].n }, x = d[id] || {}; NF.forEach(function (f) { o[f] = typeof x[f] === 'number' ? x[f] : null; }); return o; }) }; });
    var lib = window.LtbStatsImport; return '<ul class="glist">' + lib.describeReadiness(lib.scoreReadiness(teams)).map(function (l) { return '<li>' + l.text + '</li>'; }).join('') + '</ul>';
  }
  function hRender() {
    var h = '', m = hm();
    if (!m) {
      var list = HM.filter(function (x) { return (hs.mode === 'all' || x.mode === hs.mode) && (hs.pl === 'all' || x.t[0].concat(x.t[1]).indexOf(+hs.pl) >= 0); });
      h += '<div class="card"><h2 id="hh-filter">Match history</h2><div class="filters">' + hSel('hf-mode', 'Mode', [['all', 'All modes'], ['normal', 'Normal draft'], ['aram', 'ARAM']], hs.mode) +
        hSel('hf-pl', 'Player', [['all', 'Any player']].concat(PLAYERS.map(function (p) { return [p.id, p.n]; })), hs.pl) + '</div><p class="mut" role="status">' + list.length + ' of ' + HM.length + ' games</p><ul class="hlist">';
      list.forEach(function (x) { h += '<li><button type="button" class="hmrow" data-m="' + x.id + '"><b>Game ' + x.id + '</b> · ' + x.date + ' · ' + mname(x) + ' · Team ' + (x.win + 1) + ' won · ' + x.len +
        '<small>' + x.t[0].map(function (i) { return PLAYERS[i].n; }).join(', ') + ' vs ' + x.t[1].map(function (i) { return PLAYERS[i].n; }).join(', ') + '</small></button></li>'; });
      hb.innerHTML = h + '</ul></div>'; return;
    }
    var d = MS[m.id] || {};
    h += '<p><button type="button" class="btn" data-h="back">‹ All games</button></p><div class="card"><div class="actions" id="h-actions"><h2>Game ' + m.id + ' · ' + mname(m) + ' <span class="mut">' + m.date + ' · ' + m.len + '</span></h2><span class="grow"></span>' +
      (hs.panel ? '' : '<button type="button" class="btn" data-h="stats">Add stats</button><button type="button" class="btn main" data-h="imp">Fill from screenshots</button>') + '</div>' +
      (hs.note ? '<p class="note" role="status">' + hs.note + '</p>' : '') + '</div><div class="two">';
    m.t.forEach(function (ids, ti) { h += '<div class="card team' + (ti ? ' b' : '') + '"><h2>Team ' + (ti + 1) + (m.win === ti ? ' · Won' : '') + '</h2><ul class="rows">';
      ids.forEach(function (id, si) { var x = d[id], p = PLAYERS[id]; h += '<li class="hrow"><em>' + (m.mode === 'normal' ? LANES[si] : '#' + (si + 1)) + '</em><b>' + p.n + '</b><small>' +
        (x && typeof x.k === 'number' ? (x.champ ? x.champ + ' · ' : '') + x.k + '/' + x.d + '/' + x.a : 'No stats yet') + '</small></li>'; }); h += '</ul></div>'; });
    h += '</div>' + readyHtml(m);
    if (hs.panel === 'stats') {
      var dr = hs.draft || {}; h += '<form class="card" id="h-stats" onsubmit="return false"><h2>Stats</h2><p class="mut">Type the numbers, or use Fill from screenshots. Empty boxes change nothing.</p><div class="scroll"><table class="st"><thead><tr><th>Player</th><th>Champion</th>' +
        NL.map(function (x) { return '<th>' + x + '</th>'; }).join('') + '</tr></thead><tbody>';
      hrows(m).forEach(function (r) { var x = dr[r.id] || {}; h += '<tr><th scope="row">' + r.p.n + '</th><td><select data-sf="' + r.id + '" data-f="champ" aria-label="Champion of ' + r.p.n + '"><option value="">—</option>' +
        CH.map(function (c) { return '<option' + (x.champ === c ? ' selected' : '') + '>' + c + '</option>'; }).join('') + '</select></td>' +
        NF.map(function (f, i) { return '<td><input type="number" min="0" data-sf="' + r.id + '" data-f="' + f + '" aria-label="' + NL[i] + ' of ' + r.p.n + '" value="' + (typeof x[f] === 'number' ? x[f] : '') + '"></td>'; }).join('') + '</tr>'; });
      h += '</tbody></table></div><p><button type="button" class="btn main" data-h="save">Save stats (demo)</button> <button type="button" class="btn" data-h="cancel">Cancel</button></p></form>';
    }
    if (hs.panel === 'imp') {
      h += '<div class="card" id="h-imp"><h2>Fill from screenshots</h2><p class="mut">The site never calls an AI. You copy a prompt, paste it with your screenshots into your own AI chat, then paste the answer back here.</p>' +
        '<h3 id="h-names">1. Game names and champions</h3><div class="scroll"><table class="st"><thead><tr><th>Site name</th><th>Game name</th><th>Lane</th><th>Champion (optional)</th></tr></thead><tbody>';
      hrows(m).forEach(function (r) { h += '<tr><th scope="row">' + r.p.n + '</th><td><input type="text" id="hg-' + r.id + '" value="' + r.p.n + 'Plays" aria-label="Game name of ' + r.p.n + '"></td><td>' + (r.lane || '—') + '</td><td><select id="hc-' + r.id + '" aria-label="Champion of ' + r.p.n + '"><option value="">—</option>' +
        CH.map(function (c) { return '<option>' + c + '</option>'; }).join('') + '</select></td></tr>'; });
      h += '</tbody></table></div><h3>2. Copy the prompt</h3><p><button type="button" class="btn main" id="h-promptbtn" data-h="copy">Copy prompt</button></p><p class="mut" id="h-copied" role="status"></p><textarea id="h-prompt" rows="8" readonly hidden aria-label="Prompt"></textarea>' +
        '<h3 id="h-sbh">Your screenshots</h3><div id="h-sb" class="sb"><p class="mut">Example scoreboard drawn by this page. It is not from the game.</p><table class="st"><tbody>';
      exData(m).forEach(function (e, i) { var n = e.n; if (i === 0 || i === 5) h += '<tr><th colspan="5">Team ' + (e.r.team + 1) + '</th></tr>'; h += '<tr><th scope="row">' + e.r.p.n + 'Plays</th><td>' + n.champ + '</td><td>' + n.k + ' / ' + n.d + ' / ' + n.a + '</td><td>' + n.cs + ' CS</td><td>' + n.gold + ' gold</td></tr>'; });
      h += '</tbody></table></div><h3>3. Paste the answer</h3><textarea id="h-ans" rows="9" aria-label="Answer of your AI chat" placeholder="Paste the answer here"></textarea><p><button type="button" class="btn" data-h="example">Use the example answer</button> ' +
        '<button type="button" class="btn main" id="h-check" data-h="check">Check answer</button> <button type="button" class="btn main" id="h-fillbtn" data-h="fill" disabled>Fill the form</button> <button type="button" class="btn" data-h="cancel">Cancel</button></p>' +
        '<p id="h-sum" role="status"></p><ul class="glist" id="h-res"></ul></div>';
    }
    hb.innerHTML = h; var ta = $('#h-ans', hb); if (ta) ta.value = hs.ans;
  }
  function readForm() { var d = {}; [].forEach.call(hb.querySelectorAll('[data-sf]'), function (el) { var id = el.dataset.sf, f = el.dataset.f; d[id] = d[id] || {}; d[id][f] = f === 'champ' ? el.value : (el.value === '' ? null : +el.value); }); return d; }
  function hCheck() {
    var m = hm(), lib = window.LtbStatsImport, rows = hrows(m), sum = $('#h-sum', hb), ul = $('#h-res', hb); hs.ans = $('#h-ans', hb).value; ul.textContent = '';
    if (!lib) { sum.textContent = 'The reader script did not load. Reload the page and try again.'; return; }
    if (!hs.ans.trim()) { sum.textContent = 'Paste the answer of your AI chat first.'; return; }
    var res = lib.parseAnswer(hs.ans, { teams: [{ name: 'Team 1' }, { name: 'Team 2' }], players: rows.map(function (r) { return { id: r.id, site: r.p.n, team: r.team }; }), champions: CH.map(function (c, i) { return { id: i + 1, name: c }; }) });
    var n = 0, warns = 0; res.players.forEach(function (p) { NF.forEach(function (f) { if (typeof p[f] === 'number') n++; }); }); res.problems.forEach(function (p) { if (p.level === 'warn') warns++; });
    function li(t) { var x = document.createElement('li'); x.textContent = t; ul.appendChild(x); }
    res.problems.forEach(function (p) { li((p.level === 'warn' ? 'Problem' : 'Note') + (p.line ? ' (line ' + p.line + ')' : '') + ': ' + p.text); });
    res.players.forEach(function (p) { li(p.site + ': ' + (p.champion ? p.champion.name + ' · ' : '') + p.k + '/' + p.d + '/' + p.a); });
    sum.textContent = n === 0 ? 'Nothing can be filled from this text.' : 'Found ' + res.players.length + ' of ' + rows.length + ' players, ' + n + ' values' + (warns ? ', ' + warns + (warns === 1 ? ' problem' : ' problems') : ', no problems') + '. Look at the list, then press Fill the form.';
    hs.res = n ? res : null; $('#h-fillbtn', hb).disabled = !n;
  }
  hb.addEventListener('input', function (e) { if (e.target.id === 'h-ans') { hs.ans = e.target.value; hs.res = null; var f = $('#h-fillbtn', hb); if (f) f.disabled = true; } });
  hb.addEventListener('change', function (e) { if (e.target.id === 'hf-mode') { hs.mode = e.target.value; hRender(); $('#hf-mode', hb).focus(); } else if (e.target.id === 'hf-pl') { hs.pl = e.target.value; hRender(); $('#hf-pl', hb).focus(); } });
  hb.addEventListener('click', function (e) {
    var r = e.target.closest('[data-m]'), b = e.target.closest('[data-h]'), m = hm(), lib = window.LtbStatsImport;
    if (r) { hs.mid = +r.dataset.m; hs.panel = null; hs.note = ''; hRender(); window.scrollTo(0, 0); return; }
    if (!b) return; var a = b.dataset.h;
    if (a === 'back') { hs.mid = null; hs.panel = null; hs.note = ''; hRender(); return; }
    if (a === 'stats') { hs.panel = 'stats'; hs.draft = JSON.parse(JSON.stringify(MS[m.id] || {})); hs.note = ''; hRender(); return; }
    if (a === 'imp') { hs.panel = 'imp'; hs.ans = ''; hs.res = null; hs.note = ''; hRender(); return; }
    if (a === 'cancel') { hs.panel = null; hRender(); return; }
    if (a === 'save') { MS[m.id] = readForm(); hs.panel = null; hs.note = 'Stats saved. This demo keeps them only until you reload the page.'; hRender(); return; }
    if (a === 'example') { hs.ans = exAnswer(m); $('#h-ans', hb).value = hs.ans; hs.res = null; $('#h-fillbtn', hb).disabled = true; return; }
    if (a === 'check') { hCheck(); return; }
    if (a === 'copy') {
      var rows = hrows(m), c = $('#h-copied', hb), tx = $('#h-prompt', hb);
      var text = lib.buildPrompt({ teams: m.t.map(function (ids, ti) { return { name: 'Team ' + (ti + 1), players: rows.filter(function (r) { return r.team === ti; }).map(function (r) { return { site: r.p.n, game: $('#hg-' + r.id, hb).value, lane: r.lane, champion: $('#hc-' + r.id, hb).value }; }) }; }), champions: CH });
      tx.value = text; tx.hidden = false; c.textContent = 'Prompt ready (' + text.length + ' characters). Paste it into your AI chat with the screenshots.';
      if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(function () { c.textContent = 'Prompt copied (' + text.length + ' characters). Paste it into your AI chat with the screenshots.'; }, function () { tx.select(); });
      return;
    }
    if (a === 'fill' && hs.res) {
      var dr = JSON.parse(JSON.stringify(MS[m.id] || {})), cnt = 0;
      hs.res.players.forEach(function (p) { var x = dr[p.id] = dr[p.id] || {}; if (p.champion) x.champ = p.champion.name; NF.forEach(function (f) { if (typeof p[f] === 'number') { x[f] = p[f]; cnt++; } }); });
      hs.draft = dr; hs.panel = 'stats'; hs.note = 'Filled ' + cnt + ' values. Nothing is saved yet. Check the numbers, then press Save stats.'; hRender(); var s = $('#h-stats', hb); if (s) s.scrollIntoView({ block: 'start' });
    }
  });
  hRender();

  // ---- guided tour engine (reused by every tab) ----
  var tour = null;
  function startTour(steps, opener) {
    if (tour) return;
    var app = $('#app'), i = 0, wrap = document.createElement('div');
    wrap.className = 'tour';
    wrap.innerHTML = '<div class="hole"></div><div class="tbox" role="dialog" aria-modal="true" aria-labelledby="tt" aria-describedby="td" tabindex="-1">' +
      '<h2 id="tt"></h2><p id="td"></p><div class="tnav"><button type="button" class="btn" data-a="back">Back</button>' +
      '<button type="button" class="btn main" data-a="next">Next</button><span class="grow"></span><button type="button" class="btn" data-a="skip">Skip</button></div></div>';
    document.body.appendChild(wrap); app.setAttribute('inert', '');
    var hole = $('.hole', wrap), box = $('.tbox', wrap), reduce = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;
    tour = wrap;
    function place() {
      var el = $(steps[i].sel); if (!el) return; var r = el.getBoundingClientRect(), vw = innerWidth, vh = innerHeight;
      hole.style.top = (r.top - 6) + 'px'; hole.style.left = (r.left - 6) + 'px'; hole.style.width = (r.width + 12) + 'px'; hole.style.height = (r.height + 12) + 'px';
      var bw = box.offsetWidth, bh = box.offsetHeight, top;
      if (r.bottom + 14 + bh < vh) top = r.bottom + 14; else if (r.top - 14 - bh > 0) top = r.top - 14 - bh; else top = vh - bh - 8;
      box.style.top = Math.max(8, top) + 'px'; box.style.left = Math.min(Math.max(8, r.left), vw - bw - 8) + 'px';
    }
    function show() {
      var s = steps[i]; if (s.before) s.before(); var el = $(s.sel);
      $('#tt', wrap).textContent = s.title; $('#td', wrap).textContent = s.text;
      $('[data-a=back]', wrap).disabled = i === 0; $('[data-a=next]', wrap).textContent = i === steps.length - 1 ? 'Done' : 'Next (' + (i + 1) + '/' + steps.length + ')';
      if (el) {
        // inside an iframe, scrollIntoView can also scroll the parent page, so scroll only this document
        if (framed) { var er = el.getBoundingClientRect(); window.scrollTo({ top: Math.max(0, er.top + (window.pageYOffset || 0) - innerHeight / 2 + er.height / 2), behavior: reduce ? 'auto' : 'smooth' }); }
        else el.scrollIntoView({ block: 'center', behavior: reduce ? 'auto' : 'smooth' });
      }
      place(); setTimeout(place, 350); $('[data-a=next]', wrap).focus({ preventScroll: true });
    }
    function end() {
      window.removeEventListener('resize', place); window.removeEventListener('scroll', place, true); document.removeEventListener('keydown', key, true);
      wrap.remove(); app.removeAttribute('inert'); tour = null; if (opener) opener.focus();
    }
    function key(e) {
      if (e.key === 'Escape') { e.preventDefault(); end(); }
      else if (e.key === 'ArrowRight') { e.preventDefault(); go(1); }
      else if (e.key === 'ArrowLeft') { e.preventDefault(); go(-1); }
      else if (e.key === 'Tab') {
        var f = [].filter.call(wrap.querySelectorAll('button'), function (b) { return !b.disabled; }), k = f.indexOf(document.activeElement);
        e.preventDefault(); f[(k + (e.shiftKey ? f.length - 1 : 1)) % f.length].focus();
      }
    }
    function go(d) { var n = i + d; if (n < 0) return; if (n >= steps.length) { end(); return; } i = n; show(); }
    wrap.addEventListener('click', function (e) {
      var b = e.target.closest('button[data-a]'); if (!b) return;
      if (b.dataset.a === 'skip') end(); else go(b.dataset.a === 'next' ? 1 : -1);
    });
    window.addEventListener('resize', place); window.addEventListener('scroll', place, true); document.addEventListener('keydown', key, true);
    show();
  }

  var BUILDER_TOUR = [
    { sel: '#players-head', title: 'Pick your players', text: 'Press a name to add it. A Normal or ARAM game needs 10 players. These players are invented.', before: function () { showTab('builder'); } },
    { sel: '#mode-head', title: 'Game mode', text: 'Normal has five lanes. ARAM has no lanes. Arena makes eight teams of two.' },
    { sel: '#bal-head', title: 'How to balance', text: 'Random, random with good lanes, or two balanced teams by rank or by your own tiers. Rank and lanes is a good start.' },
    { sel: '#gen', title: 'Generate teams', text: 'Ten players are picked for you. Press this button to build two teams.', before: function () { st.mode = 'normal'; st.picked = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]; sync(); } },
    { sel: '#result', title: 'Your two teams', text: 'Every player is on the map at a lane position. Both teams have a similar strength.', before: function () { if (!st.teams) make(); } },
    { sel: '#result .row', title: 'Swap two players', text: 'Press one player, then another one, and they change places. You can press names on the map too.', before: function () { if (!st.teams) make(); } },
    { sel: '#actions', title: 'Submit, start, finish', text: 'Submit locks the teams. Then Start game, and Finish game with the winner. The real site saves the game in History. This demo saves nothing.' },
    { sel: '#exp', title: 'Export for Discord', text: 'This draws a picture of the teams on the map. You can post it in your chat. The real site can also send it to Discord for you.' },
    { sel: '#looks', title: 'Three looks', text: 'Steel, Brutal and Forest. Each person picks their own, and the choice is remembered.' }
  ];
  var CLASH_TOUR = [
    { sel: '#cl-ans-head', title: 'Who can play?', text: 'Each member answers free or busy. Busy players cannot be picked. Press a name to switch it.', before: function () { showTab('clash'); } },
    { sel: '#cl-best', title: 'Set best team', text: 'This picks the five best free players and puts each one in a good lane. You can change any lane by hand below.', before: function () { if (!clFull()) { clBest(); clRender(); } } },
    { sel: '#cl-map', title: 'One side only', text: 'In Clash you build one team, so the map shows only your side. Every player stands at the lane position.' },
    { sel: '#cl-games-head', title: 'Games and series', text: 'Save the team as game 1, start it, then finish it with Won or Lost. A team can play at most 4 games. The series record adds up the results.' },
    { sel: '#cl-end', title: 'End the clash', text: 'When the games are done, End clash closes the event. You need at least one finished game. This demo saves nothing.' }
  ];
  var HIST_TOUR = [
    { sel: '#hh-filter', title: 'Find a game', text: 'The history lists every finished game. Filter by mode or by player.', before: function () { showTab('history'); hs.mid = null; hs.panel = null; hRender(); } },
    { sel: '.hmrow', title: 'Open a game', text: 'Press a game to see both teams and the stats. These games are invented.' },
    { sel: '#h-actions', title: 'Add the stats', text: 'Add stats opens a form to type the numbers. Fill from screenshots does most of the typing for you.', before: function () { hs.mid = 5; hs.panel = null; hs.note = ''; hRender(); } },
    { sel: '#h-names', title: 'Game names and champions', text: 'Each player has a game name. You can also pick the champion, or leave it empty.', before: function () { hs.panel = 'imp'; hs.ans = ''; hs.res = null; hRender(); } },
    { sel: '#h-promptbtn', title: 'Copy the prompt', text: 'The site never calls an AI. It writes a prompt with the players and the exact format. You paste it, with your screenshots, into your own AI chat.' },
    { sel: '#h-sb', title: 'Your screenshots', text: 'This example scoreboard is drawn by this page. It is not from the game. Your own post-game screenshots go into the AI chat.' },
    { sel: '#h-ans', title: 'Paste the answer', text: 'The AI answers with a block of text. You paste it here. The example answer leaves one number empty on purpose.', before: function () { hs.ans = exAnswer(hm()); $('#h-ans', hb).value = hs.ans; } },
    { sel: '#h-sum', title: 'The site checks it', text: 'The site reads the answer and checks it. It lists every problem, such as a missing number or team totals that do not match.', before: function () { hCheck(); } },
    { sel: '#h-fillbtn', title: 'Fill the form', text: 'If the check looks good, press Fill the form. The numbers go into the stats form. Nothing is saved until you press Save stats.' }
  ];
  var TOURS = { builder: BUILDER_TOUR, clash: CLASH_TOUR, history: HIST_TOUR };
  var tb = $('#tour-btn');
  tb.addEventListener('click', function () { startTour(TOURS[curTab] || BUILDER_TOUR, tb); });
  var framed = false; try { framed = window.self !== window.top; } catch (e) { framed = true; }
  if (!store('ltb-tour-seen') && window.requestAnimationFrame) { store('ltb-tour-seen', '1'); setTimeout(function () { startTour(BUILDER_TOUR, tb); }, 500); }
  sync();
})();
