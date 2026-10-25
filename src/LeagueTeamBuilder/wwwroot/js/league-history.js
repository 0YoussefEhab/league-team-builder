/* League match history (Step 5a, docs/LEAGUE_BUILD_SPEC.md section 8).
   - Opens a match in a dialog: GET the server-rendered partial, show it, close with Esc / backdrop / Close,
     and give focus back to the row. Without JavaScript the row is a normal link to the full page.
   - Shows every <time data-lgh-time> in the viewer's own time zone (the server text is UTC).
   - Delete button (only rendered for people allowed LeagueMatch > Delete): two clicks, then POST, then reload.
   - Edit result (Step 5b, only rendered for people allowed LeagueMatch > EditResult on a finished match): shows the
     form inside the popup, POSTs the winner and team kills, then reloads the popup (and the list when it closes).
   - Add / edit stats (Step 5c, only rendered for people allowed LeagueMatch > Stats on a finished match): a table per
     team (champion, lane played, K, D, A, plus a "+" row with the extra stats), the match length, one Save for the
     whole match. The champion is picked from a searchable list (never typed free). Asks before throwing away typed stats.
   - Fill from screenshots (Step 13, only rendered for people allowed LeagueMatch > ImportStats on a finished match):
     step 1 = the game name of every player (13a), step 2 = an optional champion per player and "Copy prompt" (13b),
     step 3 = paste the answer, "Check the answer" (a list of what was read and what was not), "Fill the form" (13c; also needs
     LeagueMatch > Stats). The prompt and the reader live in league-stats-import.js (window.HfeStatsImport). Nothing is sent anywhere
     and nothing is saved by step 3: it only fills the Step 5c form, which is saved with its own Save stats. */
(function () {
    'use strict';

    var dataEl = document.getElementById('lgh-data');
    if (!dataEl) return;
    var cfg = {};
    try { cfg = JSON.parse(dataEl.textContent || '{}'); } catch (e) { cfg = {}; }

    var dlg = document.getElementById('lgh-dlg');
    var toastEl = document.getElementById('lgh-toast');
    var lastRow = null;
    var reqId = 0;
    var toastTimer = null;
    var changed = false;   // a result was edited: the list behind the dialog is out of date
    var escAt = 0;         // when Esc last closed the champion list (so the same Esc does not also close the dialog)
    var cbOpen = null;     // the champion picker that is open: { box, btn, hidden, panel, search, list, items, active }

    function localize(root) {
        var list = (root || document).querySelectorAll('time[data-lgh-time]');
        for (var i = 0; i < list.length; i++) {
            var el = list[i];
            var d = new Date(el.getAttribute('datetime'));
            if (isNaN(d.getTime())) continue;
            if (!el.title) el.title = el.textContent;
            // Results/export step 1: League shows Cairo time for everybody (league-time.js), not the device's own zone.
            el.textContent = window.LeagueTime
                ? window.LeagueTime.format(el.getAttribute('datetime'))
                : d.toLocaleString([], { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
        }
    }

    // F1: ms is optional; the fill toast carries the score notice and stays longer.
    function toast(msg, ms) {
        if (!toastEl) return;
        toastEl.textContent = msg;
        toastEl.classList.add('is-on');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { toastEl.classList.remove('is-on'); }, ms || 4000);
    }

    function esc(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

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
            return res.json().catch(function () { return { ok: false, error: 'The server did not accept the request. Reload the page and try again.' }; });
        });
    }

    // ── Dialog ────────────────────────────────────────────────────────────────────────────────────────
    function showError(message) {
        dlg.innerHTML = '<article class="lgh-md"><p class="lgh-md-err is-on" role="alert">' + esc(message) +
            '</p><div class="lgh-md-acts"><button type="button" class="lg-btn lg-btn-ghost" data-lgh-close>Close</button></div></article>';
    }

    var pendingNotice = '';

    function openMatch(link) {
        lastRow = link;
        cbOpen = null;
        var mine = ++reqId;
        dlg.innerHTML = '<article class="lgh-md"><p class="lgh-loading">Loading...</p></article>';
        if (!dlg.open) dlg.showModal();

        fetch(link.href, {
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'text/html' }
        }).then(function (res) {
            if (res.status === 404) throw new Error('That match no longer exists.');
            if (!res.ok) throw new Error('Could not load this match.');
            return res.text();
        }).then(function (html) {
            if (mine !== reqId) return;
            // A page that is not the match (for example a sign-in or access-denied page) has no marker.
            if (html.indexOf('data-match-id') < 0) throw new Error('You do not have access to this match.');
            dlg.innerHTML = html;
            localize(dlg);
            // Step 12c.1: a note from the last MVP save (for example why "Back to automatic" named nobody).
            if (pendingNotice) {
                var art = dlg.querySelector('.lgh-md') || dlg;
                var note = document.createElement('p');
                note.className = 'lgh-md-err is-on';
                note.setAttribute('role', 'status');
                note.textContent = pendingNotice;
                art.insertBefore(note, art.firstChild);
                pendingNotice = '';
            }
            var h = dlg.querySelector('h3');
            if (h) { h.setAttribute('tabindex', '-1'); h.focus(); }
        }).catch(function (err) {
            if (mine !== reqId) return;
            showError(err && err.message ? err.message : 'Could not load this match.');
        });
    }

    // Typed-in stats are not saved until Save: ask before they are thrown away.
    function dirtyStats() {
        var f = dlg && dlg.querySelector ? dlg.querySelector('[data-lgh-statsform]:not([hidden])') : null;
        if (!f) f = document.querySelector('[data-lgh-statsform]:not([hidden])');
        return !!(f && f.getAttribute('data-dirty') === '1');
    }
    function confirmLeave() {
        return !dirtyStats() || window.confirm('Discard the stats you typed? They are not saved yet.');
    }

    if (dlg && typeof dlg.showModal === 'function') {
        dlg.addEventListener('click', function (e) { if (e.target === dlg && confirmLeave()) dlg.close(); });
        dlg.addEventListener('cancel', function (e) {
            if (cbOpen || Date.now() - escAt < 300) { e.preventDefault(); closePanel(true); return; }   // Esc closes the champion list first
            if (!confirmLeave()) e.preventDefault();
        });
        dlg.addEventListener('close', function () {
            reqId++;
            cbOpen = null;
            if (changed) { window.location.reload(); return; }
            if (lastRow && document.contains(lastRow)) lastRow.focus();
        });
    }

    // The "Played on" form has no submit button; Enter in its box must not reload the page.
    document.addEventListener('submit', function (e) {
        if (e.target && e.target.closest && e.target.closest('[data-lgh-dateform]')) {
            e.preventDefault();
            var b = e.target.querySelector('[data-lgh-datesave]');
            if (b) onSaveDate(b);
        }
    });

    document.addEventListener('click', function (e) {
        var t = e.target;
        if (!t || !t.closest) return;

        var row = t.closest('[data-lgh-open]');
        if (row) {
            if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
            if (!dlg || typeof dlg.showModal !== 'function') return;   // old browser: follow the link to the full page
            e.preventDefault();
            openMatch(row);
            return;
        }

        if (t.closest('[data-lgh-close]')) {
            if (!confirmLeave()) return;
            if (dlg && dlg.open) dlg.close();
            else if (cfg.historyUrl) window.location.href = cfg.historyUrl;
            return;
        }

        var del = t.closest('[data-lgh-delete]');
        if (del) { onDelete(del); return; }

        if (t.closest('[data-lgh-edit]')) { toggleImp(t.closest('.lgh-md'), false); toggleEdit(t.closest('.lgh-md'), true); return; }
        if (t.closest('[data-lgh-editcancel]')) { toggleEdit(t.closest('.lgh-md'), false); return; }
        var save = t.closest('[data-lgh-save]');
        if (save) { onSave(save); return; }
        var dateSave = t.closest('[data-lgh-datesave]');
        if (dateSave) { onSaveDate(dateSave); return; }

        if (t.closest('[data-lgh-imp]')) { toggleImp(t.closest('.lgh-md'), true); return; }
        if (t.closest('[data-lgh-impcancel]')) { toggleImp(t.closest('.lgh-md'), false); return; }
        var impCopy = t.closest('[data-lgh-impcopy]');
        if (impCopy) { onCopyPrompt(impCopy); return; }
        var impRead = t.closest('[data-lgh-impread]');
        if (impRead) { onReadAnswer(impRead, false); return; }
        var impFill = t.closest('[data-lgh-impfill]');
        if (impFill) { onReadAnswer(impFill, true); return; }
        var impSave = t.closest('[data-lgh-impsave]');
        if (impSave) { onSaveNames(impSave); return; }

        if (t.closest('[data-lgh-stats]')) { toggleImp(t.closest('.lgh-md'), false); toggleStats(t.closest('.lgh-md'), true); return; }
        if (t.closest('[data-lgh-statscancel]')) {
            if (confirmLeave()) toggleStats(t.closest('.lgh-md'), false);
            return;
        }
        var saveStats = t.closest('[data-lgh-statssave]');
        if (saveStats) { onSaveStats(saveStats); return; }

        if (t.closest('[data-lgh-mvp-open]')) { toggleImp(t.closest('.lgh-md'), false); toggleMvp(t.closest('.lgh-md'), true); return; }
        if (t.closest('[data-lgh-mvpcancel]')) { toggleMvp(t.closest('.lgh-md'), false); return; }
        var mvpSave = t.closest('[data-lgh-mvpsave]');
        if (mvpSave) { onSaveMvp(mvpSave, false); return; }
        var mvpAuto = t.closest('[data-lgh-mvpauto]');
        if (mvpAuto) { onSaveMvp(mvpAuto, true); return; }

        var chipX = t.closest('[data-lgh-chipx]');
        if (chipX) { removeChip(chipX); return; }
        var more = t.closest('[data-lgh-more]');
        if (more) { toggleExtra(more.closest('tr'), more); return; }
        var moreAll = t.closest('[data-lgh-moreall]');
        if (moreAll) { toggleAllExtras(moreAll); return; }

        var cbBtn = t.closest('[data-lgh-cbbtn]');
        if (cbBtn) {
            if (cbOpen && cbOpen.btn === cbBtn) closePanel(true); else openPanel(cbBtn, '');
            return;
        }
        var item = t.closest('[data-lgh-cblist] li[data-pick]');
        if (item && cbOpen) { choose(item.getAttribute('data-pick')); return; }

        // A click anywhere else closes an open champion list.
        if (cbOpen && !t.closest('[data-lgh-cbpanel]')) closePanel(false);
    });

    // The list is positioned on screen, so it closes when something scrolls (except the list itself) or the window changes.
    document.addEventListener('scroll', function (e) {
        if (!cbOpen) return;
        if (e.target && e.target.closest && e.target.closest('[data-lgh-cbpanel]')) return;
        closePanel(false);
    }, true);
    window.addEventListener('resize', function () { if (cbOpen) closePanel(false); });

    // A changed answer text means the last check no longer counts: the Fill button waits for a new check.
    document.addEventListener('input', function (e) {
        var ta = e.target && e.target.matches && e.target.matches('[data-lgh-impans]') ? e.target : null;
        if (!ta) return;
        var fb = ta.closest('[data-lgh-impform]').querySelector('[data-lgh-impfill]');
        if (fb) fb.disabled = true;
    });

    // Any typing in the stats form marks it as not saved.
    document.addEventListener('input', function (e) {
        var f = e.target && e.target.closest ? e.target.closest('[data-lgh-statsform]') : null;
        if (f) f.setAttribute('data-dirty', '1');
    });

    // role="button" links should also react to Space.
    document.addEventListener('keydown', function (e) {
        if (e.key !== ' ' && e.key !== 'Spacebar') return;
        var t = e.target;
        if (t && t.matches && t.matches('a[data-lgh-open]')) { e.preventDefault(); t.click(); }
    });

    // ── Delete (two clicks) ───────────────────────────────────────────────────────────────────────────
    function setErr(scope, msg) {
        var el = scope.querySelector('[data-lgh-err]');
        if (!el) { toast(msg); return; }
        el.textContent = msg || '';
        el.classList.toggle('is-on', !!msg);
    }

    function onDelete(btn) {
        var scope = btn.closest('.lgh-md') || document;
        if (btn.disabled) return;

        if (btn.getAttribute('data-armed') !== '1') {
            btn.setAttribute('data-armed', '1');
            btn.setAttribute('data-label', btn.textContent);
            btn.textContent = 'Really delete? Click again';
            setErr(scope, '');
            setTimeout(function () {
                if (btn.getAttribute('data-armed') === '1' && document.contains(btn)) {
                    btn.removeAttribute('data-armed');
                    btn.textContent = btn.getAttribute('data-label') || 'Delete match';
                }
            }, 5000);
            return;
        }

        btn.disabled = true;
        post(cfg.deleteUrl, { id: parseInt(btn.getAttribute('data-lgh-delete'), 10) }).then(function (res) {
            if (res && res.ok) {
                if (dlg && dlg.open) dlg.close();
                if (dlg) window.location.reload();            // History: the list and the counts changed
                else window.location.href = cfg.historyUrl;   // full page: that match is gone
                return;
            }
            btn.disabled = false;
            btn.removeAttribute('data-armed');
            btn.textContent = btn.getAttribute('data-label') || 'Delete match';
            setErr(scope, (res && res.error) || 'Could not delete the match.');
        }).catch(function () {
            btn.disabled = false;
            btn.removeAttribute('data-armed');
            btn.textContent = btn.getAttribute('data-label') || 'Delete match';
            setErr(scope, 'Could not reach the server. Try again.');
        });
    }

    // ── Edit result (Step 5b) ─────────────────────────────────────────────────────────────────────────
    function toggleEdit(scope, on) {
        if (!scope) return;
        var form = scope.querySelector('[data-lgh-editform]');
        var btn = scope.querySelector('[data-lgh-edit]');
        if (!form) return;
        if (on) closeMvp(scope);
        if (on && !closeStatsForm(scope)) return;
        form.hidden = !on;
        if (btn) btn.hidden = on;
        if (on) {
            var first = form.querySelector('input[type="radio"]:checked') || form.querySelector('input');
            if (first) first.focus();
        } else if (btn) {
            btn.focus();
        }
    }

    function killsValue(form, index) {
        var el = form.querySelector('[data-lgh-kills="' + index + '"]');
        if (!el || el.value.trim() === '') return null;
        var n = parseInt(el.value, 10);
        return isNaN(n) ? null : n;
    }

    // Results/export step 1: change "Played on" of a finished match. The value is Cairo wall-clock time; the server
    // converts it to UTC and checks it (not in the future, not absurdly old).
    function onSaveDate(btn) {
        var form = btn.closest('[data-lgh-dateform]');
        if (!form || btn.disabled) return;
        var input = form.querySelector('[data-lgh-played]');
        var errEl = form.querySelector('[data-lgh-dateerr]');
        function err(msg) { errEl.textContent = msg || ''; errEl.classList.toggle('is-on', !!msg); }
        if (!input || !input.value) { err('Enter the date and time the match was played.'); return; }
        err('');

        btn.disabled = true;
        post(cfg.editDateUrl, {
            id: parseInt(btn.getAttribute('data-lgh-datesave'), 10),
            playedOn: input.value
        }).then(function (res) {
            btn.disabled = false;
            if (res && res.ok) {
                changed = true;
                if (dlg && dlg.open && lastRow) openMatch(lastRow);   // show the saved date; the list reloads on close
                else window.location.reload();                       // full page
                return;
            }
            err((res && res.error) || 'Could not save the date.');
        }).catch(function () {
            btn.disabled = false;
            err('Could not reach the server. Try again.');
        });
    }

    function onSave(btn) {
        var scope = btn.closest('.lgh-md');
        var form = btn.closest('[data-lgh-editform]');
        if (!scope || !form || btn.disabled) return;

        var pick = form.querySelector('input[type="radio"]:checked');
        var errEl = form.querySelector('[data-lgh-editerr]');
        function err(msg) { errEl.textContent = msg || ''; errEl.classList.toggle('is-on', !!msg); }
        if (!pick) { err('Choose which team won.'); return; }
        err('');

        btn.disabled = true;
        post(cfg.editResultUrl, {
            id: parseInt(btn.getAttribute('data-lgh-save'), 10),
            winner: parseInt(pick.value, 10),
            kills0: killsValue(form, 0),
            kills1: killsValue(form, 1)
        }).then(function (res) {
            if (res && res.ok) {
                changed = true;
                if (dlg && dlg.open && lastRow) openMatch(lastRow);   // show the saved result; the list reloads on close
                else window.location.reload();                       // full page
                return;
            }
            btn.disabled = false;
            err((res && res.error) || 'Could not save the result.');
        }).catch(function () {
            btn.disabled = false;
            err('Could not reach the server. Try again.');
        });
    }

    // ── Set MVPs (Step 8a3, only rendered for people allowed LeagueMatch > SetMvp on a finished match) ──
    function closeMvp(scope) {
        var form = scope && scope.querySelector('[data-lgh-mvpform]');
        if (form && !form.hidden) toggleMvp(scope, false);
    }

    function toggleMvp(scope, on) {
        if (!scope) return;
        var form = scope.querySelector('[data-lgh-mvpform]');
        var btn = scope.querySelector('[data-lgh-mvp-open]');
        if (!form) return;
        if (on) {
            // The result form, the stats form and this one are never open together.
            var ef = scope.querySelector('[data-lgh-editform]');
            if (ef && !ef.hidden) toggleEdit(scope, false);
            if (!closeStatsForm(scope)) return;
        }
        form.hidden = !on;
        if (btn) btn.hidden = on;
        if (on) {
            var first = form.querySelector('select');
            if (first) first.focus();
        } else {
            var err = form.querySelector('[data-lgh-mvperr]');
            if (err) { err.textContent = ''; err.classList.remove('is-on'); }
            if (btn) btn.focus();
        }
    }

    function mvpValue(form, key) {
        var el = form.querySelector('[data-lgh-mvp="' + key + '"]');
        if (!el || el.value === '') return null;
        var n = parseInt(el.value, 10);
        return isNaN(n) ? null : n;
    }

    function onSaveMvp(btn, auto) {
        var form = btn.closest('[data-lgh-mvpform]');
        if (!form || btn.disabled) return;
        var errEl = form.querySelector('[data-lgh-mvperr]');
        function err(msg) { errEl.textContent = msg || ''; errEl.classList.toggle('is-on', !!msg); }
        err('');

        var buttons = form.querySelectorAll('button');
        function lock(on) { for (var i = 0; i < buttons.length; i++) buttons[i].disabled = on; }
        lock(true);
        post(cfg.setMvpUrl, {
            id: parseInt(btn.getAttribute(auto ? 'data-lgh-mvpauto' : 'data-lgh-mvpsave'), 10),
            auto: !!auto,
            mvp: mvpValue(form, 'mvp'),
            statsMvp: mvpValue(form, 'statsMvp'),
            teamMvp: mvpValue(form, 'teamMvp'),
            statsTeamMvp: mvpValue(form, 'statsTeamMvp')
        }).then(function (res) {
            if (res && res.ok) {
                changed = true;
                pendingNotice = res.notice || '';
                if (dlg && dlg.open && lastRow) openMatch(lastRow);   // show the saved MVPs; the list reloads on close
                else window.location.reload();
                return;
            }
            lock(false);
            err((res && res.error) || 'Could not save the MVPs.');
        }).catch(function () {
            lock(false);
            err('Could not reach the server. Try again.');
        });
    }


    // ── Fill from screenshots, step 1: game names (Step 13a) ──────────────────────────────────────────
    // Only the names for now: a box for every player who has no game name, one Save for the match. Reading the screenshots
    // is a later step. Opening it closes the other forms (and asks first when typed stats would be lost).
    function toggleImp(scope, on) {
        if (!scope) return;
        var form = scope.querySelector('[data-lgh-impform]');
        var btn = scope.querySelector('[data-lgh-imp]');
        if (!form) return;
        if (on) {
            if (!closeStatsForm(scope)) return;
            var other = scope.querySelector('[data-lgh-editform]');
            if (other && !other.hidden) toggleEdit(scope, false);
            closeMvp(scope);
        }
        form.hidden = !on;
        if (btn) btn.hidden = on;
        if (on) {
            initCombos(form);
            var first = form.querySelector('[data-lgh-impname]') || form.querySelector('button');
            if (first) first.focus();
        } else if (btn && !btn.hidden && document.contains(btn)) {
            closePanel(false);
            // Closed by the user (not because another form opened): give the focus back to the button.
            if (document.activeElement && form.contains(document.activeElement)) btn.focus();
        }
    }

    function impSay(el, msg) {
        if (!el) return;
        el.textContent = msg || '';
        el.classList.toggle('is-on', !!msg);
    }

    function onSaveNames(btn) {
        var form = btn.closest('[data-lgh-impform]');
        if (!form || btn.disabled) return;
        var errEl = form.querySelector('[data-lgh-imperr]');
        var okEl = form.querySelector('[data-lgh-impok]');
        impSay(errEl, '');
        impSay(okEl, '');

        var names = [];
        var inputs = form.querySelectorAll('[data-lgh-impname]');
        for (var i = 0; i < inputs.length; i++) {
            var v = (inputs[i].value || '').trim();
            if (!v) continue;
            var row = inputs[i].closest('[data-lgh-improw]');
            var pid = row ? parseInt(row.getAttribute('data-lgh-improw'), 10) : NaN;
            if (!isNaN(pid)) names.push({ playerId: pid, gameName: v });
        }
        if (!names.length) { impSay(errEl, 'Type at least one name first.'); return; }

        btn.disabled = true;
        post(cfg.importUrl, { id: parseInt(btn.getAttribute('data-lgh-impsave'), 10), names: names }).then(function (res) {
            btn.disabled = false;
            if (!res || !res.ok) { impSay(errEl, (res && res.error) || 'Could not save the names.'); return; }
            // Every saved name becomes plain text; the others keep their box.
            var players = res.players || [];
            for (var k = 0; k < players.length; k++) {
                if (!players[k].gameName) continue;
                var li = form.querySelector('[data-lgh-improw="' + players[k].playerId + '"]');
                var box = li ? li.querySelector('[data-lgh-impname]') : null;
                if (!box) continue;
                var span = document.createElement('span');
                span.className = 'lgh-imp-game';
                span.textContent = players[k].gameName;
                box.parentNode.replaceChild(span, box);
            }
            var total = form.querySelectorAll('[data-lgh-improw]').length;
            var have = form.querySelectorAll('.lgh-imp-game').length;
            var count = form.querySelector('[data-lgh-impcount]');
            if (count) count.textContent = have + ' of ' + total + ' players have a game name.';
            impSay(okEl, res.message || 'Saved.');
        }).catch(function () {
            btn.disabled = false;
            impSay(errEl, 'Could not reach the server. Try again.');
        });
    }

    // ── Fill from screenshots, step 2: the prompt (Step 13b) ──────────────────────────────────────────
    // Reads the panel (names, lanes, the champions the mod picked), lets league-stats-import.js build the text, and copies it.
    // Browsers may refuse copying (or have no clipboard): then the text is shown in a box, selected, to copy by hand.
    function onCopyPrompt(btn) {
        var form = btn.closest('[data-lgh-impform]');
        if (!form) return;
        var okEl = form.querySelector('[data-lgh-impcopyok]');
        var errEl = form.querySelector('[data-lgh-imperr]');
        var fall = form.querySelector('[data-lgh-impfall]');
        var area = form.querySelector('[data-lgh-imptext]');
        impSay(okEl, '');
        impSay(errEl, '');
        if (fall) fall.hidden = true;

        var lib = window.HfeStatsImport;
        if (!lib || typeof lib.buildPrompt !== 'function') {
            impSay(errEl, 'The prompt script did not load. Reload the page (Ctrl+F5) and try again.');
            return;
        }

        var teams = [];
        var unknown = 0;
        var teamEls = form.querySelectorAll('[data-lgh-impteam]');
        for (var i = 0; i < teamEls.length; i++) {
            var players = [];
            var rows = teamEls[i].querySelectorAll('[data-lgh-improw]');
            for (var j = 0; j < rows.length; j++) {
                var row = rows[j];
                var siteEl = row.querySelector('.lgh-imp-site');
                var gameEl = row.querySelector('.lgh-imp-game');
                var typed = row.querySelector('[data-lgh-impname]');
                var game = gameEl ? gameEl.textContent : (typed ? typed.value : '');
                var hidden = row.querySelector('[data-lgh-champ]');
                var champ = hidden && hidden.value ? champById(form, hidden.value) : null;
                if (!String(game).trim()) unknown++;
                players.push({
                    site: siteEl ? siteEl.textContent : '',
                    game: game,
                    lane: row.getAttribute('data-lane') || '',
                    champion: champ ? champ.name : ''
                });
            }
            teams.push({ name: teamEls[i].getAttribute('data-lgh-impteam') || '', players: players });
        }

        var names = [];
        var all = champsOf(form);
        for (var k = 0; k < all.length; k++) if (!all[k].retired) names.push(all[k].name);

        var text = lib.buildPrompt({ teams: teams, champions: names });
        if (!text) { impSay(errEl, 'There is no player to write a prompt for.'); return; }

        var note = unknown > 0
            ? ' ' + unknown + (unknown === 1 ? ' player has' : ' players have') + ' no game name yet; the AI will look for ' + (unknown === 1 ? 'that row' : 'those rows') + ' by team, lane and champion.'
            : '';
        function copied() { impSay(okEl, 'Prompt copied (' + text.length + ' characters). Paste it into your AI chat with the screenshots.' + note); }
        function byHand() {
            if (!area || !fall) { impSay(errEl, 'Could not copy the prompt.'); return; }
            area.value = text;
            fall.hidden = false;
            area.focus();
            area.select();
            var done = false;
            try { done = document.execCommand('copy'); } catch (e) { done = false; }
            if (done) copied();
            else impSay(okEl, 'Could not copy by itself. The text is selected below: press Ctrl+C, then paste it into your AI chat.' + note);
        }

        if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
            navigator.clipboard.writeText(text).then(copied, byHand);
        } else {
            byHand();
        }
    }

    // ── Fill from screenshots, step 3: the answer (Step 13c) ──────────────────────────────────────────
    // What the reader needs to know about this match, read from the panel: teams, players (site name, id, team), champions.
    function importContext(form) {
        var teams = [], players = [];
        var teamEls = form.querySelectorAll('[data-lgh-impteam]');
        for (var i = 0; i < teamEls.length; i++) {
            teams.push({ name: teamEls[i].getAttribute('data-lgh-impteam') || '' });
            var rows = teamEls[i].querySelectorAll('[data-lgh-improw]');
            for (var j = 0; j < rows.length; j++) {
                var siteEl = rows[j].querySelector('.lgh-imp-site');
                players.push({ id: parseInt(rows[j].getAttribute('data-lgh-improw'), 10), site: siteEl ? siteEl.textContent : '', team: i });
            }
        }
        var champions = [];
        var all = champsOf(form);
        for (var k = 0; k < all.length; k++) if (!all[k].retired) champions.push({ id: all[k].id, name: all[k].name });
        return { teams: teams, players: players, champions: champions };
    }

    function showResult(form, res, notice) {
        var list = form.querySelector('[data-lgh-impres]');
        if (!list) return;
        list.textContent = '';
        // F1: one line per team about the score first (short, and the list can scroll), then the problems of the answer.
        // A notice only: it never blocks Fill the form or Save stats.
        if (notice && notice.length) {
            var anyOpen = false;
            for (var s = 0; s < notice.length; s++) {
                var nli = document.createElement('li');
                nli.className = notice[s].ready ? 'is-ok' : 'is-warn';
                var ntag = document.createElement('b');
                ntag.textContent = 'Score';
                nli.appendChild(ntag);
                nli.appendChild(document.createTextNode(notice[s].text));
                list.appendChild(nli);
                if (!notice[s].ready) anyOpen = true;
            }
            if (anyOpen) {
                var hint = document.createElement('li');
                var htag = document.createElement('b');
                htag.textContent = 'Note';
                hint.appendChild(htag);
                hint.appendChild(document.createTextNode('These stats are needed for the all-stats MVP and the leaderboard score. You can still fill and save without them.'));
                list.appendChild(hint);
            }
        }
        for (var i = 0; i < res.problems.length; i++) {
            var pr = res.problems[i];
            var li = document.createElement('li');
            if (pr.level === 'warn') li.className = 'is-warn';
            var tag = document.createElement('b');
            tag.textContent = pr.level === 'warn' ? 'Problem' : 'Note';
            li.appendChild(tag);
            li.appendChild(document.createTextNode((pr.line ? 'Line ' + pr.line + ': ' : '') + pr.text));
            list.appendChild(li);
        }
    }

    // Counts what the answer would put into the form.
    function countFillable(res) {
        var values = 0;
        for (var i = 0; i < res.players.length; i++) {
            var p = res.players[i];
            var keys = ['champion', 'lane', 'k', 'd', 'a', 'cs', 'dd', 'dt', 'gold', 'vis', 'obj'];
            for (var j = 0; j < keys.length; j++) if (p[keys[j]] !== null && p[keys[j]] !== undefined) values++;
            values += Object.keys(p.extras || {}).length;   // step 13d: the other stats count too
        }
        if (res.durationSeconds !== null) values++;
        return values;
    }

    // fill = false: "Check the answer" (reads and lists, changes nothing).
    // fill = true: "Fill the form" (checks again; fills only when the text is the one that was just checked).
    function onReadAnswer(btn, fill) {
        var form = btn.closest('[data-lgh-impform]');
        if (!form || btn.disabled) return;
        var ta = form.querySelector('[data-lgh-impans]');
        var errEl = form.querySelector('[data-lgh-imperr]');
        var sumEl = form.querySelector('[data-lgh-impsum]');
        var fillBtn = form.querySelector('[data-lgh-impfill]');
        impSay(errEl, '');
        impSay(sumEl, '');
        var lib = window.HfeStatsImport;
        if (!lib || typeof lib.parseAnswer !== 'function') {
            impSay(errEl, 'The reader script did not load. Reload the page (Ctrl+F5) and try again.');
            return;
        }
        var text = ta ? ta.value : '';
        if (!text.trim()) { impSay(errEl, 'Paste the answer of your AI chat first.'); if (ta) ta.focus(); return; }

        var res = lib.parseAnswer(text, importContext(form));
        var n = countFillable(res);
        var warns = 0;
        for (var i = 0; i < res.problems.length; i++) if (res.problems[i].level === 'warn') warns++;

        if (fill && form.getAttribute('data-checked') === text) {
            // The same text the person just looked at: fill.
            var scope = form.closest('.lgh-md');
            var done = fillStatsForm(scope, res);
            if (done) {
                // F1: the form boxes are filled now, so the notice reads what is really there (saved values + what the answer added).
                var after = scoreNotice(scope, null);
                var tail = after.map(function (x) { return x.text; }).join(' ');
                toast(done.message + (tail ? ' ' + tail : ''), tail ? 10000 : 4000);
            }
            return;
        }

        // F1: what the form would hold after the fill (no fill happens here); nothing to say when the answer fills nothing.
        showResult(form, res, n > 0 ? scoreNotice(form.closest('.lgh-md'), res) : []);
        form.setAttribute('data-checked', n > 0 ? text : '');
        if (fillBtn) fillBtn.disabled = n === 0;
        if (n === 0) {
            impSay(sumEl, 'Nothing can be filled from this text.');
        } else {
            impSay(sumEl, 'Found ' + res.players.length + ' of ' + importContext(form).players.length + ' players, ' + n + ' values'
                + (warns ? ', ' + warns + (warns === 1 ? ' problem' : ' problems') : ', no problems') + '. '
                + (fill ? 'The text had changed, so it was checked again: look at the list, then press Fill the form.' : 'Look at the list, then press Fill the form.'));
        }
    }

    // Puts the values into the Step 5c form and opens it. Empty values change nothing. Nothing is saved.
    function fillStatsForm(scope, res) {
        var sf = scope ? scope.querySelector('[data-lgh-statsform]') : null;
        if (!sf) return null;
        toggleImp(scope, false);
        toggleStats(scope, true);   // opens the form and paints its champion boxes

        var set = 0, replaced = 0, playersFilled = 0, otherSet = 0;
        function put(el, v) {
            if (!el || v === null || v === undefined) return false;
            var next = String(v);
            var old = String(el.value).trim();
            if (old === next) return false;
            if (old !== '') replaced++;
            el.value = next;
            set++;
            return true;
        }

        for (var i = 0; i < res.players.length; i++) {
            var p = res.players[i];
            var row = sf.querySelector('tr[data-lgh-prow="' + p.id + '"]');
            if (!row) continue;
            var before = set;

            if (p.champion) {
                var hidden = row.querySelector('[data-lgh-champ]');
                if (hidden && put(hidden, p.champion.id)) paintCombo(hidden.closest('[data-lgh-cb]'));
            }
            if (p.lane !== null) {
                var laneEl = row.querySelector('[data-lgh-lane]');
                if (laneEl && laneEl.querySelector('option[value="' + p.lane + '"]')) put(laneEl, p.lane);
            }
            put(row.querySelector('[data-lgh-k]'), p.k);
            put(row.querySelector('[data-lgh-d]'), p.d);
            put(row.querySelector('[data-lgh-a]'), p.a);

            var xr = row.nextElementSibling;
            if (xr && xr.hasAttribute('data-lgh-extrarow')) {
                var anyExtra = false;
                for (var x = 0; x < EXTRA_FIELDS.length; x++) {
                    var key = EXTRA_FIELDS[x][0];
                    if (put(xr.querySelector('[data-lgh-x="' + key + '"]'), p[key])) anyExtra = true;
                }
                var ex = p.extras || {};
                Object.keys(ex).forEach(function (name) {
                    var how = setChip(xr, name, ex[name]);
                    if (how) { set++; otherSet++; anyExtra = true; if (how === 'changed') replaced++; }
                });
                if (anyExtra) setExtra(row, row.querySelector('[data-lgh-more]'), true);
            }
            if (set > before) playersFilled++;
        }

        if (res.durationSeconds !== null) {
            put(sf.querySelector('[data-lgh-dmin]'), Math.floor(res.durationSeconds / 60));
            put(sf.querySelector('[data-lgh-dsec]'), res.durationSeconds % 60);
        }

        sf.setAttribute('data-dirty', '1');
        var message = 'Filled ' + set + ' values for ' + playersFilled + (playersFilled === 1 ? ' player' : ' players')
            + (otherSet ? ' (' + otherSet + ' of them other stats, in the extra row)' : '')
            + (replaced ? ' (' + replaced + ' replaced a value that was already there)' : '')
            + '. Check them, then press Save stats. Nothing is saved yet.';
        var note = sf.querySelector('[data-lgh-fillnote]');
        if (note) { note.textContent = message; note.classList.add('is-on'); }
        if (sf.scrollIntoView) sf.scrollIntoView({ block: 'start' });
        return { set: set, replaced: replaced, players: playersFilled, message: message };
    }

    // ── Add / edit stats (Step 5c) ────────────────────────────────────────────────────────────────────
    // Closes the stats form (asking first when typed stats would be lost). Returns false when the user said no.
    function closeStatsForm(scope) {
        var form = scope.querySelector('[data-lgh-statsform]');
        if (!form || form.hidden) return true;
        if (!confirmLeave()) return false;
        toggleStats(scope, false);
        return true;
    }

    function toggleStats(scope, on) {
        if (!scope) return;
        var form = scope.querySelector('[data-lgh-statsform]');
        var btn = scope.querySelector('[data-lgh-stats]');
        if (!form) return;
        if (on) {
            // The result form and the stats form are never open together.
            var other = scope.querySelector('[data-lgh-editform]');
            if (other && !other.hidden) toggleEdit(scope, false);
            closeMvp(scope);
        }
        form.hidden = !on;
        if (btn) btn.hidden = on;
        if (on) {
            initCombos(form);
            form.removeAttribute('data-dirty');
            resetStatWarnings(form);
            var first = form.querySelector('[data-lgh-cbbtn]') || form.querySelector('input, select');
            if (first) first.focus();
        } else {
            closePanel(false);
            form.removeAttribute('data-dirty');
            resetStatWarnings(form);
            var err = form.querySelector('[data-lgh-statserr]');
            if (err) { err.textContent = ''; err.classList.remove('is-on'); }
            var fnote = form.querySelector('[data-lgh-fillnote]');
            if (fnote) { fnote.textContent = ''; fnote.classList.remove('is-on'); }
            if (btn) btn.focus();
        }
    }

    function intOrNull(el, max) {
        if (!el || el.value.trim() === '') return { value: null };
        if (el.validity && el.validity.badInput) return { bad: true };
        var text = el.value.trim();
        if (!/^\d+$/.test(text)) return { bad: true };
        var n = parseInt(text, 10);
        if (n > max) return { bad: true };
        return { value: n };
    }

    // ── Other stats chips (step 13d) ──────────────────────────────────────────────────────────────────
    function chipLabel(name) {
        var lib = window.HfeStatsImport;
        return lib && lib.extraLabel ? lib.extraLabel(name) : name;
    }
    function chipText(name, value) { return chipLabel(name) + ' ' + Number(value).toLocaleString('en-US'); }

    // {name: value} of the chips in a player's extra row; null when the row has no chip block (then nothing is sent).
    function readChips(xr) {
        var box = xr ? xr.querySelector('[data-lgh-chips]') : null;
        if (!box) return null;
        var out = {};
        var li = box.querySelectorAll('[data-lgh-chip]');
        for (var i = 0; i < li.length; i++) {
            var v = parseInt(li[i].getAttribute('data-lgh-chipval'), 10);
            if (!isNaN(v)) out[li[i].getAttribute('data-lgh-chip')] = v;
        }
        return out;
    }

    // Adds a chip or changes its value. Returns 'new', 'changed' or '' (same value already there).
    function setChip(xr, name, value) {
        var box = xr ? xr.querySelector('[data-lgh-chips]') : null;
        if (!box) return '';
        var found = null, low = String(name).toLowerCase();
        var li = box.querySelectorAll('[data-lgh-chip]');
        for (var i = 0; i < li.length; i++) if (li[i].getAttribute('data-lgh-chip').toLowerCase() === low) { found = li[i]; break; }
        if (found) {
            if (parseInt(found.getAttribute('data-lgh-chipval'), 10) === value) return '';
            found.setAttribute('data-lgh-chipval', String(value));
            found.querySelector('.lgh-chip-txt').textContent = chipText(found.getAttribute('data-lgh-chip'), value);
            return 'changed';
        }
        found = document.createElement('li');
        found.className = 'lgh-chip';
        found.setAttribute('data-lgh-chip', name);
        found.setAttribute('data-lgh-chipval', String(value));
        var txt = document.createElement('span');
        txt.className = 'lgh-chip-txt';
        txt.textContent = chipText(name, value);
        var x = document.createElement('button');
        x.type = 'button';
        x.className = 'lgh-chip-x';
        x.setAttribute('data-lgh-chipx', '');
        x.setAttribute('aria-label', 'Remove ' + chipLabel(name));
        x.title = 'Remove';
        x.innerHTML = '&times;';
        found.appendChild(txt);
        found.appendChild(x);
        box.appendChild(found);
        var wrap = box.closest('[data-lgh-other]');
        if (wrap) wrap.hidden = false;
        return 'new';
    }

    function removeChip(btn) {
        var li = btn.closest('[data-lgh-chip]');
        var wrap = btn.closest('[data-lgh-other]');
        var form = btn.closest('[data-lgh-statsform]');
        if (li) li.remove();
        if (wrap && !wrap.querySelector('[data-lgh-chip]')) wrap.hidden = true;
        if (form) form.setAttribute('data-dirty', '1');
    }

    // ── Extra stats row ("+") ─────────────────────────────────────────────────────────────────────────
    function setExtra(row, btn, on) {
        var xr = row ? row.nextElementSibling : null;
        if (!xr || !xr.hasAttribute('data-lgh-extrarow')) return;
        xr.hidden = !on;
        btn.textContent = on ? '-' : '+';
        btn.setAttribute('aria-expanded', on ? 'true' : 'false');
    }

    function toggleExtra(row, btn) {
        var xr = row ? row.nextElementSibling : null;
        if (!xr) return;
        setExtra(row, btn, xr.hidden);
        if (!xr.hidden) { var f = xr.querySelector('input'); if (f) f.focus(); }
    }

    function toggleAllExtras(btn) {
        var form = btn.closest('[data-lgh-statsform]');
        if (!form) return;
        var on = btn.getAttribute('aria-pressed') !== 'true';
        var mores = form.querySelectorAll('[data-lgh-more]');
        for (var i = 0; i < mores.length; i++) setExtra(mores[i].closest('tr'), mores[i], on);
        btn.setAttribute('aria-pressed', on ? 'true' : 'false');
        btn.textContent = on ? 'Hide extra stats' : 'Show extra stats for everyone';
    }

    // ── Champion picker: a button that opens a searchable list (spec: no free typing, so no typos) ─────
    // The picker serves the stats form and the "Fill from screenshots" panel; the list sits once in the popup.
    var PICKER_FORM = '[data-lgh-statsform], [data-lgh-impform]';

    function champsOf(form) {
        if (form._champs) return form._champs;
        var scope = form.closest('.lgh-md') || document;
        var el = scope.querySelector('script[data-lgh-champs]');
        var list = [];
        try { list = JSON.parse(el ? el.textContent || '[]' : '[]'); } catch (e) { list = []; }
        form._champs = list;
        return list;
    }

    function norm(text) { return String(text).toLowerCase().replace(/[\s'\u2019.\-]/g, ''); }

    function champById(form, id) {
        var list = champsOf(form);
        for (var i = 0; i < list.length; i++) if (String(list[i].id) === String(id)) return list[i];
        return null;
    }

    // Shows the chosen champion (icon and name) on the button, or the placeholder.
    function paintCombo(box) {
        var form = box.closest(PICKER_FORM);
        var hidden = box.querySelector('[data-lgh-champ]');
        var btn = box.querySelector('[data-lgh-cbbtn]');
        var label = box.querySelector('[data-lgh-cblabel]');
        var old = btn.querySelector('img.lgh-cb-ic');
        if (old) old.remove();
        var c = hidden.value ? champById(form, hidden.value) : null;
        if (!c) {
            label.textContent = hidden.value ? 'Unknown champion' : 'Pick champion';
            btn.classList.toggle('is-empty', !hidden.value);
            return;
        }
        label.textContent = c.name + (c.retired ? ' (retired)' : '');
        btn.classList.remove('is-empty');
        if (c.iconUrl) {
            var img = document.createElement('img');
            img.className = 'lgh-cb-ic';
            img.src = c.iconUrl;
            img.alt = '';
            img.width = 20;
            img.height = 20;
            btn.insertBefore(img, btn.firstChild);
        }
    }

    function initCombos(form) {
        if (form.getAttribute('data-cb-init') === '1') return;
        form.setAttribute('data-cb-init', '1');
        var boxes = form.querySelectorAll('[data-lgh-cb]');
        for (var i = 0; i < boxes.length; i++) paintCombo(boxes[i]);
    }

    function renderList(query) {
        var o = cbOpen;
        if (!o) return;
        var form = o.box.closest(PICKER_FORM);
        var current = o.hidden.value;
        var q = norm(query);
        var starts = [], rest = [];
        var all = champsOf(form);
        for (var i = 0; i < all.length; i++) {
            var c = all[i];
            // A retired champion is only offered on the player it already belongs to.
            if (c.retired && String(c.id) !== String(current)) continue;
            var n = norm(c.name);
            if (q === '') { rest.push(c); continue; }
            var at = n.indexOf(q);
            if (at === 0) starts.push(c); else if (at > 0) rest.push(c);
        }
        var shown = q === '' ? rest : starts.concat(rest);

        o.list.textContent = '';
        o.items = [];
        function add(pick, c, text) {
            var li = document.createElement('li');
            li.setAttribute('role', 'option');
            li.id = 'lgh-cb-o' + o.items.length;
            li.setAttribute('data-pick', pick);
            var sel = pick === String(current) || (pick === '' && !current);
            li.setAttribute('aria-selected', sel ? 'true' : 'false');
            if (c && c.iconUrl) {
                var img = document.createElement('img');
                img.src = c.iconUrl; img.alt = ''; img.width = 20; img.height = 20; img.loading = 'lazy';
                li.appendChild(img);
            }
            var span = document.createElement('span');
            span.textContent = text;
            li.appendChild(span);
            o.list.appendChild(li);
            o.items.push(li);
        }
        if (q === '') add('', null, 'No champion');
        for (var k = 0; k < shown.length; k++) add(String(shown[k].id), shown[k], shown[k].name + (shown[k].retired ? ' (retired)' : ''));
        if (shown.length === 0) {
            var none = document.createElement('li');
            none.className = 'lgh-cb-none';
            none.textContent = 'No champion matches "' + query + '"';
            o.list.appendChild(none);
        }
        // Highlight: the typed match first, else the current champion.
        var idx = -1;
        if (q !== '') idx = 0;
        else for (var j = 0; j < o.items.length; j++) if (o.items[j].getAttribute('aria-selected') === 'true') { idx = j; break; }
        setActive(idx < 0 && o.items.length ? 0 : idx);
    }

    function setActive(i) {
        var o = cbOpen;
        if (!o || !o.items.length) { if (o) o.search.removeAttribute('aria-activedescendant'); return; }
        i = Math.max(0, Math.min(o.items.length - 1, i));
        if (o.active >= 0 && o.items[o.active]) o.items[o.active].classList.remove('is-active');
        o.active = i;
        var li = o.items[i];
        li.classList.add('is-active');
        o.search.setAttribute('aria-activedescendant', li.id);
        li.scrollIntoView({ block: 'nearest' });
    }

    function openPanel(btn, firstChars) {
        closePanel(false);
        var box = btn.closest('[data-lgh-cb]');
        var scope = btn.closest('.lgh-md');
        var panel = scope.querySelector('[data-lgh-cbpanel]');
        if (!box || !panel) return;
        var search = panel.querySelector('[data-lgh-cbsearch]');
        cbOpen = {
            box: box, btn: btn, hidden: box.querySelector('[data-lgh-champ]'), panel: panel,
            search: search, list: panel.querySelector('[data-lgh-cblist]'), items: [], active: -1
        };
        search.value = firstChars || '';
        panel.hidden = false;

        // Fixed position under the button (or above it when there is no room below).
        var r = btn.getBoundingClientRect();
        var width = Math.max(r.width, 260);
        var left = Math.min(r.left, window.innerWidth - width - 8);
        panel.style.width = width + 'px';
        panel.style.left = Math.max(8, left) + 'px';
        var below = window.innerHeight - r.bottom;
        if (below < 300 && r.top > below) { panel.style.top = 'auto'; panel.style.bottom = (window.innerHeight - r.top + 4) + 'px'; }
        else { panel.style.bottom = 'auto'; panel.style.top = (r.bottom + 4) + 'px'; }

        btn.setAttribute('aria-expanded', 'true');
        renderList(search.value);
        search.focus();
    }

    function closePanel(focusButton) {
        var o = cbOpen;
        if (!o) return;
        cbOpen = null;
        if (o.panel) o.panel.hidden = true;
        if (o.btn) {
            o.btn.setAttribute('aria-expanded', 'false');
            if (focusButton && document.contains(o.btn)) o.btn.focus();
        }
    }

    function choose(pick) {
        var o = cbOpen;
        if (!o) return;
        var box = o.box, hidden = o.hidden;
        var changedValue = hidden.value !== pick;
        hidden.value = pick;
        paintCombo(box);
        closePanel(true);
        if (changedValue) hidden.dispatchEvent(new Event('input', { bubbles: true }));   // marks the form as not saved
    }

    // Moves focus to the next field of the same row (lane, then K).
    function focusNextInRow(box) {
        var td = box.closest('td');
        var next = td ? td.nextElementSibling : null;
        var f = next ? next.querySelector('select, input') : null;
        if (f) f.focus();
    }

    document.addEventListener('input', function (e) {
        if (cbOpen && e.target === cbOpen.search) renderList(cbOpen.search.value);
    });

    document.addEventListener('keydown', function (e) {
        var t = e.target;
        if (!t || !t.matches) return;

        // In the search box: arrows move, Enter picks, Esc closes, Tab picks what is highlighted and moves on.
        if (cbOpen && t === cbOpen.search) {
            if (e.key === 'ArrowDown') { e.preventDefault(); setActive(cbOpen.active + 1); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(cbOpen.active - 1); }
            else if (e.key === 'Enter') {
                e.preventDefault();
                var li = cbOpen.items[cbOpen.active];
                if (li) choose(li.getAttribute('data-pick'));
            } else if (e.key === 'Escape') {
                e.preventDefault(); e.stopPropagation(); escAt = Date.now(); closePanel(true);
            } else if (e.key === 'Tab') {
                var box = cbOpen.box;
                if (e.shiftKey) { closePanel(true); e.preventDefault(); return; }
                e.preventDefault();
                var hit = cbOpen.search.value.trim() !== '' ? cbOpen.items[cbOpen.active] : null;
                if (hit) choose(hit.getAttribute('data-pick')); else closePanel(false);
                focusNextInRow(box);
            }
            return;
        }

        // On the champion button: arrows or a letter open the list (a letter starts the search).
        if (t.matches('[data-lgh-cbbtn]')) {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); openPanel(t, ''); }
            else if (e.key.length === 1 && e.key !== ' ' && !e.ctrlKey && !e.metaKey && !e.altKey) { e.preventDefault(); openPanel(t, e.key); }
        }
    });

    // ── Save ──────────────────────────────────────────────────────────────────────────────────────────
    var EXTRA_FIELDS = [
        ['cs', 'cs', 'CS', 9999],
        ['dd', 'damageDealt', 'damage dealt', 999999],
        ['dt', 'damageTaken', 'damage taken', 999999],
        ['gold', 'gold', 'gold', 999999],
        ['vis', 'visionScore', 'vision score', 999],
        ['obj', 'objectiveDamage', 'objective damage', 999999]
    ];

    // MVP redesign Step 8: stat sanity check. Reads the boxes of the stats form and lists values that look like a typo
    // (an extra or missing digit). It only WARNS: the first Save press shows the list and turns the button into
    // "Save anyway"; the second press with the same list saves. Nothing is checked on the server. A check is skipped
    // whenever the boxes it needs are empty, so a half-filled match gives no warnings.
    var SAVE_LABEL = 'Save stats';
    var WARN_SHARE = 0.55;      // one player above this share of team damage (teams of 4 or more)
    var WARN_LOW_DMG = 0.10;    // damage under this share of the team average
    var WARN_LOW_TAKEN = 0.15;  // damage taken under this share of the team average
    var WARN_MIN_CS = 20;       // CS under this for a player whose lane is known and is not Support
    var LANE_SUPPORT = '4';     // LeagueLane.Support

    function boxNum(el) {
        if (!el) return null;
        var t = el.value.trim();
        return /^\d+$/.test(t) ? parseInt(t, 10) : null;
    }
    function fmtNum(n) { return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ','); }
    function pct(x) { return Math.round(x * 100) + '%'; }
    function sumOf(list, key) { var t = 0; for (var i = 0; i < list.length; i++) t += list[i][key]; return t; }
    function allHave(list, key) {
        if (!list.length) return false;
        for (var i = 0; i < list.length; i++) if (list[i][key] === null) return false;
        return true;
    }

    function extraNum(xr, key) { return boxNum(xr ? xr.querySelector('[data-lgh-x="' + key + '"]') : null); }

    function readStatsTeams(form) {
        var teams = [];
        var tables = form.querySelectorAll('.lgh-st-table');
        for (var t = 0; t < tables.length; t++) {
            var cap = tables[t].querySelector('caption');
            var team = { name: cap ? cap.textContent.trim() : 'Team ' + (t + 1), players: [] };
            var rows = tables[t].querySelectorAll('tr[data-lgh-prow]');
            for (var r = 0; r < rows.length; r++) {
                var row = rows[r];
                var nameEl = row.querySelector('.lgh-st-pname');
                var laneEl = row.querySelector('[data-lgh-lane]');
                var xr = row.nextElementSibling;
                if (xr && !xr.hasAttribute('data-lgh-extrarow')) xr = null;
                team.players.push({
                    id: parseInt(row.getAttribute('data-lgh-prow'), 10),   // F1: so the score notice can lay an answer over the boxes
                    name: nameEl ? nameEl.textContent.trim() : 'A player',
                    lane: laneEl ? laneEl.value : '',
                    k: boxNum(row.querySelector('[data-lgh-k]')),
                    d: boxNum(row.querySelector('[data-lgh-d]')),
                    a: boxNum(row.querySelector('[data-lgh-a]')),   // F1: a, gold, vis and obj join the ones the typo check reads
                    cs: extraNum(xr, 'cs'), dd: extraNum(xr, 'dd'), dt: extraNum(xr, 'dt'),
                    gold: extraNum(xr, 'gold'), vis: extraNum(xr, 'vis'), obj: extraNum(xr, 'obj')
                });
            }
            teams.push(team);
        }
        return teams;
    }

    // F1 (PLAN-leaderboard-export.md, Part 3): one line per team saying whether the game has the 9 numbers the all-stats score needs.
    // Reads the stats form boxes. With res (the check), the values of the answer are laid over what the boxes hold, the same way
    // fillStatsForm would put them (empty values change nothing), so the line says what the form WILL hold. With res = null
    // (after the fill) it reads the boxes as they are. Changes nothing on the page. Returns [] when there is nothing to show
    // (no stats form, the script did not load), so the page works as it did before.
    function scoreNotice(scope, res) {
        try {
            var lib = window.HfeStatsImport;
            var sf = scope ? scope.querySelector('[data-lgh-statsform]') : null;
            if (!sf || !lib || typeof lib.scoreReadiness !== 'function' || typeof lib.describeReadiness !== 'function') return [];
            var teams = readStatsTeams(sf);
            if (res && Array.isArray(res.players)) {
                var keys = ['k', 'd', 'a', 'cs', 'dd', 'dt', 'gold', 'vis', 'obj'];
                var byId = {};
                teams.forEach(function (t) { t.players.forEach(function (p) { byId[p.id] = p; }); });
                res.players.forEach(function (ap) {
                    var target = byId[ap.id];
                    if (!target) return;
                    keys.forEach(function (key) { if (ap[key] !== null && ap[key] !== undefined) target[key] = ap[key]; });
                });
            }
            return lib.describeReadiness(lib.scoreReadiness(teams));
        } catch (e) {
            return [];
        }
    }

    function statWarnings(form) {
        var out = [];
        var teams = readStatsTeams(form);
        for (var t = 0; t < teams.length; t++) {
            var team = teams[t], ps = team.players, n = ps.length;
            var fullDmg = n >= 4 && allHave(ps, 'dd');
            var fullTaken = n >= 4 && allHave(ps, 'dt');
            var enemy = teams.length === 2 ? teams[1 - t].players : null;
            var enemyTaken = enemy && allHave(enemy, 'dt') ? sumOf(enemy, 'dt') : null;
            var dmgTotal = fullDmg ? sumOf(ps, 'dd') : 0;
            var dmgAvg = fullDmg ? dmgTotal / n : 0;
            var takenAvg = fullTaken ? sumOf(ps, 'dt') / n : 0;
            for (var i = 0; i < n; i++) {
                var p = ps[i], w = p.name + ' (' + team.name + '): ';
                if (p.dd !== null && enemyTaken !== null && enemyTaken > 0 && p.dd > enemyTaken)
                    out.push(w + 'damage ' + fmtNum(p.dd) + ' is more than all the damage the other team took (' + fmtNum(enemyTaken) + ').');
                else if (fullDmg && dmgTotal > 0 && p.dd / dmgTotal > WARN_SHARE)
                    out.push(w + 'damage ' + fmtNum(p.dd) + ' is ' + pct(p.dd / dmgTotal) + ' of the team\'s damage.');
                if (fullDmg && dmgAvg > 0 && p.dd < WARN_LOW_DMG * dmgAvg)
                    out.push(w + 'damage ' + fmtNum(p.dd) + ' is far below the team average (' + fmtNum(Math.round(dmgAvg)) + ').');
                if (fullTaken && takenAvg > 0 && p.dt < WARN_LOW_TAKEN * takenAvg)
                    out.push(w + 'damage taken ' + fmtNum(p.dt) + ' is far below the team average (' + fmtNum(Math.round(takenAvg)) + ').');
                if (p.cs !== null && p.lane !== '' && p.lane !== LANE_SUPPORT && p.cs < WARN_MIN_CS)
                    out.push(w + 'CS ' + p.cs + ' is very low for a player who is not Support.');
            }
        }
        if (teams.length === 2) {
            for (var a = 0; a < 2; a++) {
                var mine = teams[a], other = teams[1 - a];
                if (allHave(mine.players, 'k') && allHave(other.players, 'd')) {
                    var kills = sumOf(mine.players, 'k'), deaths = sumOf(other.players, 'd');
                    if (kills > deaths)
                        out.push(mine.name + ': ' + kills + ' kills, but ' + other.name + ' only has ' + deaths + ' deaths.');
                }
            }
        }
        return out;
    }

    function showStatWarnings(form, btn, list) {
        var box = form.querySelector('[data-lgh-statswarn]');
        if (!box) return;
        box.textContent = '';
        if (!list.length) { box.classList.remove('is-on'); return; }
        var head = document.createElement('strong');
        head.textContent = 'Please check these values (a typo is likely):';
        box.appendChild(head);
        var ul = document.createElement('ul');
        for (var i = 0; i < list.length; i++) {
            var li = document.createElement('li');
            li.textContent = list[i];
            ul.appendChild(li);
        }
        box.appendChild(ul);
        var foot = document.createElement('span');
        foot.textContent = 'Fix them and press Save stats, or press Save anyway if they are right.';
        box.appendChild(foot);
        box.classList.add('is-on');
        if (btn) btn.textContent = 'Save anyway';
    }

    function resetStatWarnings(form) {
        form.removeAttribute('data-warnshown');
        var box = form.querySelector('[data-lgh-statswarn]');
        if (box) { box.textContent = ''; box.classList.remove('is-on'); }
        var b = form.querySelector('[data-lgh-statssave]');
        if (b) b.textContent = SAVE_LABEL;
    }

    function onSaveStats(btn) {
        var form = btn.closest('[data-lgh-statsform]');
        if (!form || btn.disabled) return;
        var errEl = form.querySelector('[data-lgh-statserr]');
        function err(msg) { errEl.textContent = msg || ''; errEl.classList.toggle('is-on', !!msg); }

        // Match length: minutes and seconds, both optional.
        var minEl = form.querySelector('[data-lgh-dmin]');
        var secEl = form.querySelector('[data-lgh-dsec]');
        var duration = null;
        var mn = intOrNull(minEl, 999);
        var sc = intOrNull(secEl, 59);
        if (mn.bad) { err('Match length: minutes must be a whole number from 0 to 999.'); minEl.focus(); return; }
        if (sc.bad) { err('Match length: seconds must be a whole number from 0 to 59.'); secEl.focus(); return; }
        if (mn.value !== null || sc.value !== null) duration = (mn.value || 0) * 60 + (sc.value || 0);

        var rows = form.querySelectorAll('tr[data-lgh-prow]');
        var players = [];
        for (var r = 0; r < rows.length; r++) {
            var row = rows[r];
            var nameEl = row.querySelector('.lgh-st-pname');
            var who = nameEl ? nameEl.textContent.trim() : 'A player';

            var champVal = row.querySelector('[data-lgh-champ]').value;
            var champId = champVal === '' ? null : parseInt(champVal, 10);

            var laneEl = row.querySelector('[data-lgh-lane]');
            var lane = laneEl && laneEl.value !== '' ? parseInt(laneEl.value, 10) : null;

            var item = { playerId: parseInt(row.getAttribute('data-lgh-prow'), 10), championId: champId, lane: lane };

            var core = [['k', 'kills', 'kills'], ['d', 'deaths', 'deaths'], ['a', 'assists', 'assists']];
            for (var q = 0; q < core.length; q++) {
                var el = row.querySelector('[data-lgh-' + core[q][0] + ']');
                var got = intOrNull(el, 999);
                if (got.bad) {
                    err(who + ': ' + core[q][1] + ' must be a whole number from 0 to 999.');
                    el.focus();
                    return;
                }
                item[core[q][2]] = got.value;
            }

            // The extra row (CS, damage, ... note) may be closed; its values count either way.
            var xr = row.nextElementSibling;
            if (xr && xr.hasAttribute('data-lgh-extrarow')) {
                for (var x = 0; x < EXTRA_FIELDS.length; x++) {
                    var xe = xr.querySelector('[data-lgh-x="' + EXTRA_FIELDS[x][0] + '"]');
                    var xv = intOrNull(xe, EXTRA_FIELDS[x][3]);
                    if (xv.bad) {
                        setExtra(row, row.querySelector('[data-lgh-more]'), true);
                        err(who + ': ' + EXTRA_FIELDS[x][2] + ' must be a whole number from 0 to ' + EXTRA_FIELDS[x][3] + '.');
                        xe.focus();
                        return;
                    }
                    item[EXTRA_FIELDS[x][1]] = xv.value;
                }
                var noteEl = xr.querySelector('[data-lgh-x="notes"]');
                var note = noteEl ? noteEl.value.trim() : '';
                item.notes = note === '' ? null : note;
                var chips = readChips(xr);
                if (chips !== null) item.extras = chips;   // sent even when empty: it replaces what is saved
            }
            players.push(item);
        }
        err('');

        // Step 8: warn about values that look wrong. The first press shows them; pressing again with the same list saves.
        var warns = statWarnings(form);
        if (warns.length) {
            var sig = warns.join('|');
            if (form.getAttribute('data-warnshown') !== sig) {
                form.setAttribute('data-warnshown', sig);
                showStatWarnings(form, btn, warns);
                var wbox = form.querySelector('[data-lgh-statswarn]');
                if (wbox && wbox.scrollIntoView) wbox.scrollIntoView({ block: 'nearest' });
                return;
            }
        } else {
            resetStatWarnings(form);
        }

        btn.disabled = true;
        post(cfg.statsUrl, {
            id: parseInt(btn.getAttribute('data-lgh-statssave'), 10),
            durationSeconds: duration,
            players: players
        }).then(function (res) {
            if (res && res.ok) {
                changed = true;
                form.removeAttribute('data-dirty');
                if (dlg && dlg.open && lastRow) openMatch(lastRow);   // show the saved stats; the list reloads on close
                else window.location.reload();                          // full page
                return;
            }
            btn.disabled = false;
            err((res && res.error) || 'Could not save the stats.');
        }).catch(function () {
            btn.disabled = false;
            err('Could not reach the server. Try again.');
        });
    }

    // MVP redesign Step 5e: "Recompute MVPs" runs the existing LeagueMatch/BackfillMvps on every finished match without a
    // hand-picked MVP, after one confirm. The server answers { ok, error, checked, updated }; it is safe to run again.
    (function () {
        var rbtn = document.getElementById('lgh-recompute');
        var rmsg = document.getElementById('lgh-recompute-msg');
        if (!rbtn || !cfg.backfillUrl) return;
        function say(text) { if (rmsg) rmsg.textContent = text; }
        rbtn.addEventListener('click', function () {
            if (!window.confirm('Recompute the MVP of every finished match with the current MVP rule?\n\nMVPs picked by hand are kept. The old automatic MVPs are replaced and this cannot be undone, so take a database backup first if you have not.')) return;
            rbtn.disabled = true;
            say('Recomputing... this can take a moment.');
            post(cfg.backfillUrl, {}).then(function (res) {
                rbtn.disabled = false;
                var checked = res && typeof res.checked === 'number' ? res.checked : 0;
                var updated = res && typeof res.updated === 'number' ? res.updated : 0;
                if (res && res.ok) {
                    say('Done. ' + checked + (checked === 1 ? ' match' : ' matches') + ' checked, ' + updated + (updated === 1 ? ' MVP result' : ' MVP results') + ' changed.' +
                        (updated > 0 ? ' Reload the Leaderboard and Overview to see the new numbers.' : ''));
                    toast('MVPs recomputed: ' + updated + ' changed.');
                    return;
                }
                say(((res && res.error) || 'Could not recompute the MVPs.') + (checked ? ' (' + updated + ' of ' + checked + ' were changed before it stopped. Run it again.)' : ''));
            }).catch(function () {
                rbtn.disabled = false;
                say('Could not reach the server. Try again.');
            });
        });
    })();

    localize(document);
})();
