/* League "Game result" picture for Discord (Results/export step 3, spec section 18.2 image 2).
   Builds one fixed 1280 x 720 HTML tile from the data the match popup already has (see Services/LeagueResultImageRules.cs),
   draws it with html2canvas entirely in the browser (nothing is sent to the server) and offers Download PNG / Copy image.
   Shows both teams with KDA per player, the winner banner, the Game MVP (white row, ring, gold star, "MVP") on the
   winning team and the Team MVP (white outline, "Team MVP") on the losing side. A player without KDA shows "no KDA".
   Step 12d adds the Clash progress picture (spec.kind === 'clash', data from GET /LeagueClash/ProgressImage, see
   Services/LeagueClashProgressRules.cs): record, up to 4 game columns with KDA per player and the game MVP, Series MVP chip.
   spec (clash) = { kind: 'clash', eventId, teamId, title, teamName, when, mode, hasLanes, wins, losses, played, maxGames,
            games: [ null | { number, won, players: [{ name, lane, guest, kills, deaths, assists, mvp }] } ], seriesMvpName, seriesMvpGames }
   Step 12e adds the Session collection picture (spec.kind === 'session', data from GET /League/SessionImage, see
   Services/LeagueSessionImageRules.cs): big record, one row per game (winner pill, kills, game MVP, the other side's Team MVP),
   a "Best performance of the session" card.
   spec (session) = { kind: 'session', matchId, date, mode, hasLanes, winsA, winsB, firstNumber, totalGames,
            games: [{ number, modeName, winner: 0|1, killsA, killsB, mvp, teamMvp }], best: null | { name, kills, deaths, assists, game } }
            with mvp / teamMvp = null | { name, lane, kills, deaths, assists }
   Step 12f: the Export matches dialog (league-export-day.js) reuses render, fileName, saveCanvas and copyCanvas; the session picture's
   second line handles a ticked selection ("Games 1, 3, 4 of 5").
   Styles: css/league-result-image.css. Exposes window.LeagueResultImage = { buildHtml, render, open, openClash, openSession,
   rowHeight, fileName, saveCanvas, copyCanvas, rangeText, sendUrl, sendCanvas }.
   Step 12h1 adds Send to Discord (only when the page has #lri-send): POST /LeagueExport/Send with the PNG, done by league-send.js (load it before this file).
   spec = { matchId, title, date, mode: 'normal'|'aram', hasLanes, winner, teams: [{ index, name, won, kills,
            players: [{ name, lane, guest, kills, deaths, assists, badge: 'mvp'|'team'|null }] }] } (camelCase, as the server sends it). */
(function (root) {
    'use strict';

    var W = 1280, H = 720;
    var TEAM_COLOR = ['#4fd1c9', '#e6b84f'];
    var TROPHY = '<svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M8 4h8v6a4 4 0 0 1-8 0zM8 6H4v1a4 4 0 0 0 4 4M16 6h4v1a4 4 0 0 1-4 4M12 14v4M8 21h8M10 18h4"/></svg>';
    var STAR = '<svg width="26" height="26" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2l2.9 6.3 6.9.8-5.1 4.7 1.4 6.8L12 17.3 5.9 20.6l1.4-6.8L2.2 9.1l6.9-.8z"/></svg>';

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    // Lane icon (same drawing as the approved design). Colour is explicit: currentColor is not reliable inside a drawn SVG.
    function glyph(lane, c, sz) {
        var frame = '<rect x="3" y="3" width="18" height="18" rx="3" fill="none" stroke="' + c + '" stroke-opacity=".35" stroke-width="1.4"/><path d="M4.5 19.5L19.5 4.5" stroke="' + c + '" stroke-opacity=".35" stroke-width="1.4"/>';
        var line = 'fill="none" stroke="' + c + '" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"';
        var shapes = {
            Top: '<path d="M4.5 19.5V4.5H19.5" ' + line + '/>',
            Mid: '<path d="M6 18L18 6" stroke="' + c + '" stroke-width="2.8" stroke-linecap="round"/>',
            ADC: '<path d="M4.5 19.5H19.5V4.5" ' + line + '/><circle cx="14.5" cy="19.5" r="2.4" fill="' + c + '"/>',
            Support: '<path d="M4.5 19.5H19.5V4.5" ' + line + '/><path d="M12 14v5M9.5 16.5h5" stroke="' + c + '" stroke-width="2" stroke-linecap="round"/>',
            Jungle: '<circle cx="8.5" cy="8.5" r="2.7" fill="' + c + '"/><circle cx="15.5" cy="15.5" r="2.7" fill="' + c + '"/>'
        };
        if (!shapes[lane]) return '';
        return '<svg width="' + sz + '" height="' + sz + '" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">' + frame + shapes[lane] + '</svg>';
    }

    // Six or more players per team (a custom game can have up to 10 in total, so normally 5): shrink the rows to fit.
    function rowHeight(n) { return n <= 5 ? 70 : Math.max(34, Math.floor(440 / n) - 8); }

    function kdaHtml(p) {
        if (p.kills == null && p.deaths == null && p.assists == null) return '<span class="lri-kd"><i class="lri-no">no KDA</i></span>';
        function part(v) { return v == null ? '-' : String(v); }
        return '<span class="lri-kd"><b>' + part(p.kills) + '</b><i>/</i><b>' + part(p.deaths) + '</b><i>/</i><b>' + part(p.assists) + '</b></span>';
    }

    function rowHtml(p, i, k, hasLanes) {
        var color = TEAM_COLOR[k] || TEAM_COLOR[0];
        var cls = p.badge === 'mvp' ? ' lri-mvp' : p.badge === 'team' ? ' lri-tm' : '';
        var icon = hasLanes && p.lane ? glyph(p.lane, color, 26) : '';
        var sub = [];
        if (hasLanes && p.lane) sub.push(esc(p.lane));
        if (p.guest) sub.push('Guest');
        var size = String(p.name || '').length > 16 ? 19 : 22;
        var pill = p.badge === 'mvp' ? '<span class="lri-mb">MVP</span>' : p.badge === 'team' ? '<span class="lri-mb">Team MVP</span>' : '';
        var star = p.badge === 'mvp' ? '<span class="lri-star">' + STAR + '</span>' : '';
        return '<div class="lri-rw' + cls + '">' + star +
            '<div class="lri-gb" style="color:' + color + '">' + (icon || String(i + 1)) + '</div>' +
            '<div class="lri-who"><div class="lri-nm" style="font-size:' + size + 'px">' + esc(p.name) + '</div><small>' + sub.join(' · ') + '</small></div>' +
            pill + kdaHtml(p) + '</div>';
    }

    function columnHtml(t, k, spec) {
        var rh = rowHeight(t.players.length);
        var rows = t.players.map(function (p, i) { return rowHtml(p, i, k, spec.hasLanes); }).join('');
        return '<div class="lri-col ' + (k === 0 ? 'lri-c1' : 'lri-c2') + ' ' + (t.won ? 'lri-won' : 'lri-lose') + '" style="--rh:' + rh + 'px">' +
            '<div class="lri-ch"><b class="lri-nm">' + esc(t.name || ('Team ' + (k + 1))) + '</b><span class="lri-tag">' + (t.won ? 'Victory' : 'Defeat') + '</span></div>' + rows + '</div>';
    }

    // The picture as an HTML string (pure: no DOM needed, so it can be tested in node).
    function buildHtml(spec) {
        if (spec.kind === 'clash') return buildClashHtml(spec);
        if (spec.kind === 'session') return buildSessionHtml(spec);
        var teams = spec.teams;
        var wi = 0;
        for (var i = 0; i < teams.length; i++) if (teams[i].won) wi = i;
        var winnerName = teams[wi].name || ('Team ' + (wi + 1));
        var winStyle = wi === 0 ? '--c:#4fd1c9;--on:#0b1a1a' : '--c:#e6b84f;--on:#2b1e08';
        var k0 = teams[0].kills, k1 = teams[1].kills;
        var killsText = k0 != null && k1 != null ? k0 + ' - ' + k1 + ' kills' : '';
        return '<div class="lri-tile" data-mode="' + (spec.mode === 'aram' ? 'aram' : 'normal') + '">' +
            '<div class="lri-scrim"></div>' +
            '<div class="lri-rh"><div><div class="lri-k">Custom game · Result</div><h3>' + esc(spec.title) + '</h3><div class="lri-dt">' + esc(spec.date) + ' · HFECloud</div></div>' +
            '<div class="lri-win" style="' + winStyle + '"><div class="lri-tr">' + TROPHY + '</div><div><b>' + esc(winnerName) + ' won</b>' + (killsText ? '<small>' + killsText + '</small>' : '') + '</div></div></div>' +
            columnHtml(teams[0], 0, spec) + columnHtml(teams[1], 1, spec) +
            '<div class="lri-ft"><span>hfecloud.com</span><span class="lri-lg"><span><b>★</b> MVP of the winning team</span><span>Team MVP on the other side</span></span></div>' +
            '</div>';
    }

    // ── Clash progress (12d) ─────────────────────────────────────────────────────────────────────

    var STAR_S = '<svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2l2.9 6.3 6.9.8-5.1 4.7 1.4 6.8L12 17.3 5.9 20.6l1.4-6.8L2.2 9.1l6.9-.8z"/></svg>';

    function clashRowHtml(p, i) {
        var chip = p.lane ? esc(p.lane) : String(i + 1);
        return '<div class="lri-pr' + (p.mvp ? ' lri-pmvp' : '') + '">' + (p.mvp ? '<span class="lri-pstar">' + STAR_S + '</span>' : '') +
            '<span class="lri-ln">' + chip + '</span><span class="lri-nm">' + esc(p.name) + '</span>' + kdaHtml(p) + '</div>';
    }

    function clashCardHtml(g, n) {
        if (!g) return '<div class="lri-gc lri-pend"><b>Game ' + n + '</b><span>Not played yet</span></div>';
        return '<div class="lri-gc"><div class="lri-gh"><b>Game ' + g.number + '</b><span class="lri-wl ' + (g.won ? 'lri-w' : 'lri-l') + '">' + (g.won ? 'WIN' : 'LOSS') + '</span></div>' +
            g.players.map(clashRowHtml).join('') + '</div>';
    }

    function buildClashHtml(spec) {
        var max = spec.maxGames || 4;
        var games = spec.games || [];
        var cards = [];
        for (var i = 0; i < max; i++) cards.push(clashCardHtml(games[i] || null, i + 1));
        var chip = spec.seriesMvpName
            ? '<div class="lri-smvp"><i class="lri-smi">' + STAR_S + '</i><div><b>' + esc(spec.seriesMvpName) + '</b><small>Series MVP · best across ' + spec.seriesMvpGames + ' ' + (spec.seriesMvpGames === 1 ? 'game' : 'games') + '</small></div></div>'
            : '';
        var kicker = 'Clash · Series progress' + (spec.teamName ? ' · ' + esc(spec.teamName) : '');
        return '<div class="lri-tile lri-rp" data-mode="' + (spec.mode === 'aram' ? 'aram' : 'normal') + '">' +
            '<div class="lri-scrim"></div>' +
            '<div class="lri-rh"><div class="lri-rl"><div class="lri-k">' + kicker + '</div><h3>' + esc(spec.title) + '</h3>' + chip + '</div>' +
            '<div class="lri-rec"><b>' + spec.wins + '<i>-</i>' + spec.losses + '</b><small>' + esc(spec.when) + ' · ' + spec.played + ' of ' + max + ' played</small></div></div>' +
            '<div class="lri-gs">' + cards.join('') + '</div>' +
            '<div class="lri-ft"><span>hfecloud.com</span><span class="lri-lg"><span><b>★</b> MVP of the game</span></span></div>' +
            '</div>';
    }

    // ── Session collection (12e) ─────────────────────────────────────────────────────────────────

    var SIDE_STYLE = ['--c:#4fd1c9;--on:#0b1a1a', '--c:#e6b84f;--on:#2b1e08'];
    var STAR_XS = '<svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2l2.9 6.3 6.9.8-5.1 4.7 1.4 6.8L12 17.3 5.9 20.6l1.4-6.8L2.2 9.1l6.9-.8z"/></svg>';
    var STAR_XS_LINE = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linejoin="round"><path d="M12 2l2.9 6.3 6.9.8-5.1 4.7 1.4 6.8L12 17.3 5.9 20.6l1.4-6.8L2.2 9.1l6.9-.8z"/></svg>';

    // One player cell: kind 'm' = the game MVP (white, gold star), 't' = the Team MVP (white outline). No stored pick = an empty cell.
    function sessionPickHtml(p, kind) {
        var label = kind === 'm' ? 'MVP' : 'Team MVP';
        if (!p) return '<div class="lri-pk lri-pke"><div><small>No ' + (kind === 'm' ? 'game' : 'Team') + ' MVP</small></div></div>';
        var sub = (p.lane ? esc(p.lane) + ' · ' : '') + label;
        return '<div class="lri-pk ' + (kind === 'm' ? 'lri-pm' : 'lri-pt') + '">' +
            '<span class="lri-pks">' + (kind === 'm' ? STAR_XS : STAR_XS_LINE) + '</span>' +
            '<div><b class="lri-nm">' + esc(p.name) + '</b><small>' + sub + '</small></div>' + kdaHtml(p) + '</div>';
    }

    function sessionRowHtml(g) {
        var kills = g.killsA != null && g.killsB != null ? '<small>' + g.killsA + ' - ' + g.killsB + ' kills</small>' : '';
        return '<div class="lri-gr" style="' + SIDE_STYLE[g.winner === 1 ? 1 : 0] + '">' +
            '<div class="lri-n">Game ' + g.number + '<small>' + esc(g.modeName) + '</small></div>' +
            '<div class="lri-wn">Team ' + (g.winner === 1 ? 2 : 1) + ' won' + kills + '</div>' +
            sessionPickHtml(g.mvp, 'm') + sessionPickHtml(g.teamMvp, 't') + '</div>';
    }

    // Step 12f: the line under the date. Numbers are each game's number in the DAY, so a ticked selection can have gaps:
    // all games of the day = "Results after each game", a run = "Games 5-8 of 9", a gap = "Games 1, 3, 4 of 5".
    function gameNumbers(spec) {
        return (spec.games || []).map(function (g) { return g.number; });
    }

    function rangeText(spec) {
        var nums = gameNumbers(spec);
        if (!nums.length || spec.totalGames <= nums.length) return 'Results after each game';
        var run = nums.every(function (n, i) { return i === 0 || n === nums[i - 1] + 1; });
        if (run && nums.length > 1) return 'Games ' + nums[0] + '-' + nums[nums.length - 1] + ' of ' + spec.totalGames;
        return (nums.length === 1 ? 'Game ' : 'Games ') + nums.join(', ') + ' of ' + spec.totalGames;
    }

    function buildSessionHtml(spec) {
        var games = spec.games || [];
        var range = rangeText(spec);
        var best = spec.best
            ? '<div class="lri-bm"><span class="lri-bms">' + STAR_S + '</span><small>Best performance of the session</small><b class="lri-nm">' + esc(spec.best.name) + '</b>' +
              '<span>' + spec.best.kills + ' / ' + spec.best.deaths + ' / ' + spec.best.assists + ' · Game ' + spec.best.game + '</span></div>'
            : '';
        return '<div class="lri-tile lri-rx" data-mode="' + (spec.mode === 'aram' ? 'aram' : 'normal') + '">' +
            '<div class="lri-scrim"></div>' +
            '<div class="lri-big"><div class="lri-k">Session result</div>' +
            '<div class="lri-srec">' + spec.winsA + '<i>-</i><span>' + spec.winsB + '</span></div>' +
            '<div class="lri-dt">' + games.length + (games.length === 1 ? ' game' : ' games') + ' · ' + esc(spec.date) + '<br>' + range + '</div>' +
            '<div class="lri-stm"><span><i style="background:#4fd1c9"></i>Team 1 won ' + spec.winsA + '</span><span><i style="background:#e6b84f"></i>Team 2 won ' + spec.winsB + '</span></div>' +
            best + '</div>' +
            '<div class="lri-list">' + games.map(sessionRowHtml).join('') + '</div>' +
            '<div class="lri-ft lri-ftx"><span>hfecloud.com</span><span>★ MVP of the winning team</span></div>' +
            '</div>';
    }

    // ── Drawing (browser only) ────────────────────────────────────────────────────────────────────

    function fontsReady() {
        if (!document.fonts || !document.fonts.load) return Promise.resolve();
        var wanted = ['800 22px Inter', '600 14px Inter', '700 14px Inter', '900 46px Poppins', '800 18px Poppins'];
        return Promise.all(wanted.map(function (f) { return document.fonts.load(f).catch(function () { return []; }); }));
    }

    function bgReady(mode) {
        return new Promise(function (resolve) {
            var im = new Image();
            im.onload = im.onerror = function () { resolve(); };
            im.src = '/img/league-export/bg-' + (mode === 'aram' ? 'aram' : 'normal') + '.jpg';
        });
    }

    // Resolves to a 1280 x 720 canvas.
    function render(spec) {
        if (typeof root.html2canvas !== 'function') return Promise.reject(new Error('The picture library did not load. Reload the page and try again.'));
        var host = document.createElement('div');
        host.setAttribute('aria-hidden', 'true');
        host.style.cssText = 'position:fixed;left:-10000px;top:0;width:' + W + 'px;height:' + H + 'px;pointer-events:none;';
        host.innerHTML = buildHtml(spec);
        document.body.appendChild(host);
        var tile = host.firstElementChild;
        function cleanup() { if (host.parentNode) host.parentNode.removeChild(host); }
        return Promise.all([fontsReady(), bgReady(spec.mode)])
            .then(function () { return root.html2canvas(tile, { width: W, height: H, scale: 1, backgroundColor: '#12161f', useCORS: true, logging: false, scrollX: 0, scrollY: 0 }); })
            .then(function (c) { cleanup(); return c; }, function (err) { cleanup(); throw err; });
    }

    var dlg = null, prev = null, msg = null, btnCopy = null, btnDl = null, btnSend = null, canvas = null, token = 0, current = null;
    var titleEl = null, hintEl = null;

    function setMsg(text, isErr) {
        msg.textContent = text || '';
        msg.classList.toggle('is-err', !!isErr);
    }

    function ensureDialog() {
        if (dlg) return;
        dlg = document.createElement('dialog');
        dlg.className = 'lri-dlg';
        dlg.setAttribute('aria-labelledby', 'lri-title');
        dlg.innerHTML = '<div class="lri-dlg-in">' +
            '<h3 id="lri-title">Result image for Discord</h3>' +
            '<p class="lri-hint" data-lri-hint></p>' +
            '<div class="lri-prev" data-lri-prev><div class="lri-wait">Drawing the picture…</div></div>' +
            '<p class="lri-msg" role="status" aria-live="polite" data-lri-msg></p>' +
            '<div class="lri-acts"><button type="button" class="lg-btn lg-btn-ghost" data-lri-close>Close</button>' +
            '<button type="button" class="lg-btn lg-btn-ghost" data-lri-copy disabled>Copy image</button>' +
            '<button type="button" class="lg-btn lg-btn-ghost" data-lri-send disabled hidden>Send to Discord</button>' +
            '<button type="button" class="lg-btn" data-lri-dl disabled>Download PNG</button></div></div>';
        document.body.appendChild(dlg);
        titleEl = dlg.querySelector('#lri-title');
        hintEl = dlg.querySelector('[data-lri-hint]');
        prev = dlg.querySelector('[data-lri-prev]');
        msg = dlg.querySelector('[data-lri-msg]');
        btnCopy = dlg.querySelector('[data-lri-copy]');
        btnDl = dlg.querySelector('[data-lri-dl]');
        btnSend = dlg.querySelector('[data-lri-send]');
        btnSend.hidden = !sendUrl();
        dlg.addEventListener('click', function (e) {
            if (e.target === dlg || (e.target.closest && e.target.closest('[data-lri-close]'))) dlg.close();
        });
        dlg.addEventListener('close', function () { token++; });
        btnDl.addEventListener('click', download);
        btnCopy.addEventListener('click', copy);
        btnSend.addEventListener('click', send);
    }

    // The PNG file name of a picture (shared with the Export matches dialog).
    function fileName(spec) {
        if (spec && spec.kind === 'clash') return 'hfe-clash-' + spec.eventId + '-team-' + spec.teamId + '-progress.png';
        if (spec && spec.kind === 'session') {
            var nums = gameNumbers(spec);
            var run = nums.every(function (n, i) { return i === 0 || n === nums[i - 1] + 1; });
            var part = run ? nums[0] + '-' + nums[nums.length - 1] : nums.join('-');
            return 'hfe-session-' + String(spec.date).toLowerCase().replace(/\s+/g, '-') + '-games-' + part + '.png';
        }
        return 'hfe-game-' + (spec ? spec.matchId : 'result') + '-result.png';
    }

    function canvasBlob(c) {
        return new Promise(function (resolve, reject) {
            c.toBlob(function (b) { b ? resolve(b) : reject(new Error('The picture could not be made.')); }, 'image/png');
        });
    }

    // Download a drawn canvas as a PNG. Resolves when the download was started.
    function saveCanvas(c, spec) {
        return canvasBlob(c).then(function (blob) {
            var a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = fileName(spec);
            document.body.appendChild(a);
            a.click();
            setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
        });
    }

    // Copy a drawn canvas to the clipboard as an image. Rejects with a message the member can read.
    function copyCanvas(c) {
        if (!navigator.clipboard || typeof root.ClipboardItem !== 'function') return Promise.reject(new Error('This browser cannot copy pictures. Use Download PNG.'));
        return canvasBlob(c)
            .then(function (blob) { return navigator.clipboard.write([new root.ClipboardItem({ 'image/png': blob })]); })
            .catch(function () { throw new Error('Could not copy. Use Download PNG.'); });
    }

    // Step 12h1: post a drawn picture to the League Discord channel. The code that posts lives in league-send.js (E1, shared with the
    // Builder and Clash dialogs). The page carries <div id="lri-send"> only when the webhook is set and the member has
    // LeagueExport > Send (Views/Shared/_LeagueSendConfig.cshtml), so no element = no button.
    function sendUrl() {
        return root.LeagueSend ? root.LeagueSend.url() : null;
    }

    function sendCanvas(c, spec) {
        if (!root.LeagueSend) return Promise.reject(new Error('Sending to Discord is not set up.'));
        return root.LeagueSend.sendCanvas(c, fileName(spec));
    }

    function send() {
        if (!canvas) return;
        btnSend.disabled = true;
        setMsg('Sending to Discord…', false);
        sendCanvas(canvas, current).then(function () { setMsg('Sent to Discord.', false); })
            .catch(function (e) { setMsg(e.message, true); })
            .then(function () { btnSend.disabled = !canvas; });
    }

    function download() {
        if (!canvas) return;
        saveCanvas(canvas, current).then(function () { setMsg('Downloaded.', false); }).catch(function (e) { setMsg(e.message, true); });
    }

    function copy() {
        if (!canvas) return;
        copyCanvas(canvas).then(function () { setMsg('Copied. Paste it into Discord.', false); }).catch(function (e) { setMsg(e.message, true); });
    }

    function describe(kind) {
        if (kind === 'clash') {
            titleEl.textContent = 'Clash progress for Discord';
            hintEl.textContent = 'A 1280 × 720 picture of this team\'s games so far. It is drawn in your browser; nothing is sent anywhere.';
        } else if (kind === 'session') {
            titleEl.textContent = 'Session image for Discord';
            hintEl.textContent = 'A 1280 × 720 picture of this day\'s finished custom games (four to a picture). It is drawn in your browser; nothing is sent anywhere.';
        } else {
            titleEl.textContent = 'Result image for Discord';
            hintEl.textContent = 'A 1280 × 720 picture of this game. It is drawn in your browser; nothing is sent anywhere.';
        }
    }

    // The Clash page asks the server for the data (the games change while the page is open), then draws it.
    function openClash(url) { return openRemote(url, 'clash'); }

    // The Session image button (History popup) asks the server for the day's games, then draws them (12e).
    function openSession(url) { return openRemote(url, 'session'); }

    function openRemote(url, kind) {
        ensureDialog();
        describe(kind);
        current = null;
        canvas = null;
        btnCopy.disabled = btnDl.disabled = btnSend.disabled = true;
        setMsg('', false);
        prev.innerHTML = '<div class="lri-wait">Drawing the picture…</div>';
        if (!dlg.open) dlg.showModal();
        var mine = ++token;
        return fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) {
                if (!r.ok) throw new Error(r.status === 403 ? 'You do not have permission to make this picture.' : 'The server could not make the picture data.');
                return r.json();
            })
            .then(function (data) {
                if (mine !== token) return null;
                if (!data || !data.ok || !data.spec) throw new Error((data && data.error) || 'The picture data was empty.');
                current = data.spec;
                return render(data.spec);
            })
            .then(function (c) {
                if (!c || mine !== token) return;
                canvas = c;
                prev.innerHTML = '';
                prev.appendChild(c);
                btnCopy.disabled = btnDl.disabled = btnSend.disabled = false;
            })
            .catch(function (e) {
                if (mine !== token) return;
                prev.innerHTML = '';
                setMsg((e && e.message) || 'The picture could not be made.', true);
            });
    }

    function open(spec) {
        ensureDialog();
        describe(spec.kind);
        current = spec;
        canvas = null;
        btnCopy.disabled = btnDl.disabled = btnSend.disabled = true;
        setMsg('', false);
        prev.innerHTML = '<div class="lri-wait">Drawing the picture…</div>';
        if (!dlg.open) dlg.showModal();
        var mine = ++token;
        render(spec).then(function (c) {
            if (mine !== token) return;
            canvas = c;
            prev.innerHTML = '';
            prev.appendChild(c);
            btnCopy.disabled = btnDl.disabled = btnSend.disabled = false;
        }).catch(function (e) {
            if (mine !== token) return;
            prev.innerHTML = '';
            setMsg((e && e.message) || 'The picture could not be made.', true);
        });
    }

    if (typeof document !== 'undefined') {
        document.addEventListener('click', function (e) {
            var cbtn = e.target.closest && e.target.closest('[data-lri-clash]');
            if (cbtn) { openClash(cbtn.getAttribute('data-lri-clash')); return; }
            var sbtn = e.target.closest && e.target.closest('[data-lri-session]');
            if (sbtn) { openSession(sbtn.getAttribute('data-lri-session')); return; }
            var btn = e.target.closest && e.target.closest('[data-lri-open]');
            if (!btn) return;
            var holder = document.querySelector('script[data-lri-spec="' + btn.getAttribute('data-lri-open') + '"]');
            var spec = null;
            try { spec = holder ? JSON.parse(holder.textContent) : null; } catch (err) { spec = null; }
            if (spec && spec.teams && spec.teams.length === 2) open(spec);
        });
    }

    var api = { buildHtml: buildHtml, render: render, open: open, openClash: openClash, openSession: openSession, rowHeight: rowHeight,
        fileName: fileName, saveCanvas: saveCanvas, copyCanvas: copyCanvas, rangeText: rangeText,
        sendUrl: sendUrl, sendCanvas: sendCanvas };
    root.LeagueResultImage = api;
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
