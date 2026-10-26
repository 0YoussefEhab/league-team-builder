/* League "Fill from screenshots" (Step 13, PLAN-league.md "Step 13 - design change").
   The site never calls an AI. The mod copies a prompt made here, pastes it with the screenshots into their own AI chat,
   and later (step 13c) pastes the answer back.
   13b: buildPrompt(). 13c: parseAnswer() reads the answer and checks it. Both are pure functions: data in, data out, no page
   access, so node can test them (LeagueTeamBuilder.Tests/js/league-stats-import.test.js). The page glue lives in league-history.js
   and calls window.LtbStatsImport. */
(function (root) {
    'use strict';

    var FORMAT_VERSION = 1;
    var LANES = ['Top', 'Jungle', 'Mid', 'ADC', 'Support'];

    // control characters, zero-width and direction marks, line and paragraph separators (written as escapes on purpose)
    var CONTROL = new RegExp('[\\u0000-\\u001f\\u007f-\\u009f\\u200b-\\u200f\\u2028\\u2029\\u202a-\\u202e\\u2060\\ufeff]', 'g');

    // One line of plain text: no line breaks, no control or invisible characters, no double spaces.
    // Names come from people; they must not be able to break the layout of the prompt.
    function clean(value) {
        return String(value == null ? '' : value)
            .replace(CONTROL, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }

    // opts = {
    //   teams: [{ name, players: [{ site, game, lane, champion }] }],   (game, lane, champion may be empty)
    //   champions: ['Ahri', ...]   (the spellings the site knows; may be empty, then that part is left out)
    // }
    // Returns the prompt text, or '' when there is no player.
    function buildPrompt(opts) {
        opts = opts || {};
        var teams = [];
        var src = Array.isArray(opts.teams) ? opts.teams : [];
        for (var i = 0; i < src.length; i++) {
            var players = [];
            var sp = src[i] && Array.isArray(src[i].players) ? src[i].players : [];
            for (var j = 0; j < sp.length; j++) {
                var site = clean(sp[j] && sp[j].site);
                if (!site) continue;
                players.push({
                    site: site,
                    game: clean(sp[j].game),
                    lane: clean(sp[j].lane),
                    champion: clean(sp[j].champion)
                });
            }
            if (players.length) teams.push({ name: clean(src[i].name) || ('Team ' + (teams.length + 1)), players: players });
        }
        if (!teams.length) return '';

        var hasLanes = false, anyUnknown = false;
        teams.forEach(function (t) {
            t.players.forEach(function (p) { if (p.lane) hasLanes = true; if (!p.game) anyUnknown = true; });
        });

        var champs = [];
        var seen = {};
        (Array.isArray(opts.champions) ? opts.champions : []).forEach(function (c) {
            var n = clean(c);
            if (n && !seen[n.toLowerCase()]) { seen[n.toLowerCase()] = true; champs.push(n); }
        });

        var L = [];
        L.push('You will read the post-game screenshots of ONE League of Legends match and turn them into a text block.');
        L.push('I will paste your answer into a website, so the format below must be followed exactly.');
        L.push('');
        L.push('SCREENSHOTS');
        L.push('I attached the screenshots of the post-game screen of the League client: the Scoreboard tab and the pages of the Stats tab.');
        L.push('Use only what you can see in them.');
        L.push('');
        L.push('PLAYERS (site name = name in the game)');
        teams.forEach(function (t) {
            L.push('Team: ' + t.name);
            t.players.forEach(function (p) {
                var line = '- ' + p.site + ' = ' + (p.game || '(game name not known)');
                if (p.lane) line += ' | lane: ' + p.lane;
                if (p.champion) line += ' | champion: ' + p.champion + ' (given by me, use it as written)';
                L.push(line);
            });
        });
        L.push('The screenshots show the game name. In your answer, start every player line with the SITE name (the left side), never the game name.');
        if (anyUnknown) L.push('For a player whose game name is not known, find the row by the team, the lane and the champion.');
        L.push('');

        if (champs.length) {
            L.push('CHAMPION SPELLINGS THE SITE KNOWS');
            L.push(champs.join(', '));
            L.push('Write a champion exactly as it is spelled in this list. If you cannot tell the champion from the icon, leave it empty.');
            L.push('');
        }

        L.push('ANSWER FORMAT');
        L.push('Plain text, nothing else is needed. One block, in this order:');
        L.push('1. First line: LTB-STATS ' + FORMAT_VERSION);
        L.push('2. A line: LENGTH mm:ss   (the match length shown on the screen)');
        L.push('3. One TOTALS line per team: TOTALS | team name | kills | deaths | assists');
        L.push('   Copy the three numbers the scoreboard shows for the team. Do not add them up yourself.');
        L.push('4. One line per player: the site name, then 11 values, all separated by | , in exactly this order:');
        L.push('   site name | champion | lane | kills | deaths | assists | CS | damage to champions | damage taken | gold | vision score | objective damage');
        L.push('   - CS = minions killed + neutral minions (jungle monsters) killed, added together.');
        L.push('   - damage to champions = the damage dealt to champions (not the total damage).');
        if (hasLanes) L.push('   - lane = one of ' + LANES.join(', ') + '; leave it empty when you cannot tell.');
        else L.push('   - lane: leave it empty (this mode has no lanes).');
        L.push('   - Numbers are plain digits (13273, not 13,273).');
        // F1 (PLAN-leaderboard-export.md, Part 3): the site's score needs these 9 numbers for every player, so the AI is told to look for them on every Stats page.
        L.push('   - These 9 numbers matter most: kills, deaths, assists, CS, damage to champions, damage taken, gold, vision score, objective damage. Look for them on every Stats page and leave one empty only when you truly cannot see it.');
        L.push('   - Always write all 11 values. When a value is not visible or you are not sure, leave it empty but keep the | around it.');
        L.push('5. After the 11 values, on the same line, any other number you can see for that player on the Stats tab, each as name=value');
        L.push('   and separated by |, for example: wardsPlaced=12 | wardsDestroyed=3 | controlWards=2 | turretDamage=3300 | largestKillingSpree=4 | ccScore=18 | healing=1200');
        L.push('   Names are written in camelCase without spaces. Add only numbers you can really see.');
        L.push('');
        L.push('EXAMPLE (only to show the shape; these are not the values of this match)');
        L.push('LTB-STATS ' + FORMAT_VERSION);
        L.push('LENGTH 27:14');
        L.push('TOTALS | ' + teams[0].name + ' | 23 | 36 | 22');
        L.push('ExampleOne | Ahri | ' + (hasLanes ? 'Mid' : '') + ' | 8 | 2 | 10 | 215 | 28311 | 17200 | 14500 | 22 | 4100 | wardsPlaced=12 | turretDamage=3300');
        L.push('ExampleTwo | | ' + (hasLanes ? 'Jungle' : '') + ' | 1 | 5 | 3 | | 12000 | 9800 | | 31 | | wardsPlaced=7');
        L.push('');
        L.push('RULES');
        L.push('- Read only what you can see. Do not invent, estimate or calculate anything.');
        L.push('- When you are not sure of a value, leave it empty.');
        L.push('- Copy the team totals from the scoreboard; do not add up the players.');
        L.push('- One line for every player listed above, with the site name at the start. No extra players, no missing players.');
        L.push('- Put the answer in a single code block so it is easy to copy.');
        return L.join('\n');
    }


    // -- The reader (Step 13c) -------------------------------------------------------------------------
    // parseAnswer(text, ctx) reads the block the AI wrote and checks it. It never throws and never fails as a whole:
    // a line it cannot use is listed in `problems` and skipped. It does not touch the page.
    //
    // ctx = {
    //   teams: [{ name }],                              (same order as the page)
    //   players: [{ id, site, team }],                  (id = LeagueMatchPlayer id, team = index into teams)
    //   champions: [{ id, name }]                       (active champions of the catalog)
    // }
    // Returns {
    //   found: true when an LTB-STATS (or old HFE-STATS) line or at least one line of data was seen,
    //   version, durationSeconds (or null),
    //   totals: [{ team, k, d, a }],
    //   players: [{ id, site, line, champion: {id,name}|null, lane: 0..4|null,
    //               k, d, a, cs, dd, dt, gold, vis, obj, extras: {name: number} }]   (only players that will be filled)
    //   missing: [site names with no usable line],
    //   problems: [{ level: 'warn'|'info', line: number|null, text }]
    // }
    var LIMITS = { k: 999, d: 999, a: 999, cs: 9999, dd: 999999, dt: 999999, gold: 999999, vis: 999, obj: 999999, minutes: 999 };
    var FIELDS = [   // the 11 values after the site name, in order
        ['champion', 'champion'], ['lane', 'lane'], ['k', 'kills'], ['d', 'deaths'], ['a', 'assists'], ['cs', 'CS'],
        ['dd', 'damage to champions'], ['dt', 'damage taken'], ['gold', 'gold'], ['vis', 'vision score'], ['obj', 'objective damage']
    ];
    var LANE_WORDS = {
        top: 0, toplane: 0,
        jungle: 1, jng: 1, jgl: 1, jungler: 1,
        mid: 2, middle: 2, midlane: 2,
        adc: 3, bot: 3, bottom: 3, botlane: 3, carry: 3, marksman: 3, adcarry: 3,
        support: 4, sup: 4, supp: 4, utility: 4
    };

    function lower(v) { return clean(v).normalize ? clean(v).normalize('NFKC').toLowerCase() : clean(v).toLowerCase(); }
    function squash(v) { return lower(v).replace(/[\s'\u2019.\-_]/g, ''); }

    // "13,273" "13 273" "13'273" -> 13273. '' and "-" "n/a" "?" are an empty value (null, no problem).
    // Returns { v: number|null, bad: bool }
    function readNumber(raw) {
        var t = clean(raw).replace(/^\*+|\*+$/g, '').trim();
        if (t === '' || /^(-|--|\u2013|\u2014|\?|n\/a|na|none|null|unknown|unsure)$/i.test(t)) return { v: null, bad: false };
        var u = t.replace(/[,'\u2019_\s]/g, '');
        if (/^\d+$/.test(u)) return { v: parseInt(u, 10), bad: false };
        if (/^\d{1,3}(\.\d{3})+$/.test(u)) return { v: parseInt(u.replace(/\./g, ''), 10), bad: false };   // 13.273
        return { v: null, bad: true };
    }

    // "27:14", "1:02:03", "27m 14s" -> seconds, or null.
    function readLength(raw) {
        var t = clean(raw).replace(/^\*+|\*+$/g, '').replace(/[`|]/g, ' ').trim();
        var m = /^(\d{1,3}):(\d{2})$/.exec(t);
        if (m) return parseInt(m[1], 10) * 60 + parseInt(m[2], 10) <= 0 || parseInt(m[2], 10) > 59 ? null : parseInt(m[1], 10) * 60 + parseInt(m[2], 10);
        m = /^(\d{1,2}):(\d{2}):(\d{2})$/.exec(t);
        if (m) return parseInt(m[2], 10) > 59 || parseInt(m[3], 10) > 59 ? null : (parseInt(m[1], 10) * 60 + parseInt(m[2], 10)) * 60 + parseInt(m[3], 10);
        m = /^(\d{1,3})\s*m(?:in(?:ute)?s?)?\s*(\d{1,2})\s*s(?:ec(?:ond)?s?)?$/i.exec(t);
        if (m) return parseInt(m[2], 10) > 59 ? null : parseInt(m[1], 10) * 60 + parseInt(m[2], 10);
        return null;
    }

    // Removes what chats add around a line: list marks, quotes, back ticks, bold, table borders. Returns '' for a skipped line.
    function tidyLine(raw) {
        var t = String(raw == null ? '' : raw).replace(/\u00a0/g, ' ').trim();
        if (/^(```|~~~)/.test(t)) return '';
        t = t.replace(/^>+\s*/, '').replace(/^(?:[-*\u2022]|\d{1,2}[.)])\s+/, '');
        t = t.replace(/`/g, '').replace(/\*\*/g, '').trim();
        if (/^[\s|:\-]+$/.test(t) && t.indexOf('|') >= 0) return '';           // a markdown table rule
        if (t.charAt(0) === '|' && t.charAt(t.length - 1) === '|' && t.length > 1) t = t.slice(1, -1).trim();   // a table row
        return t;
    }

    function laneOf(raw) {
        var k = squash(raw);
        return Object.prototype.hasOwnProperty.call(LANE_WORDS, k) ? LANE_WORDS[k] : null;
    }


    // ── Other stats (step 13d) ──────────────────────────────────────────────────────────────────────
    // The server keeps them as {"wardsPlaced": 12} in LeagueMatchPlayer.ExtraJson. A name is camelCase letters and digits,
    // starts with a lower-case letter, at most 32 characters (same rule as LeagueExtraStatRules.cs). Names that already have
    // their own box in the form are not "other" stats.
    var EXTRA_MAX_NAMES = 30, EXTRA_MAX_NAME = 32, EXTRA_MAX_VALUE = 999999;
    var EXTRA_RESERVED = ['kills', 'deaths', 'assists', 'cs', 'damagedealt', 'damagetaken', 'gold', 'visionscore', 'objectivedamage', 'notes', 'champion', 'lane'];
    var ACRONYMS = { cc: 'CC', kda: 'KDA', hp: 'HP', ap: 'AP', ad: 'AD', cs: 'CS' };

    // "Wards_Placed", "WardsPlaced", "CCScore" -> "wardsPlaced", "wardsPlaced", "ccScore". '' when nothing usable is left.
    function normalizeExtraName(raw) {
        var s = String(raw == null ? '' : raw).replace(/[_\-.]+([A-Za-z0-9])/g, function (m, c) { return c.toUpperCase(); });
        s = s.replace(/[^A-Za-z0-9]/g, '').replace(/^[0-9]+/, '');
        if (!s) return '';
        var lead = /^([A-Z]+)(?=[A-Z][a-z])/.exec(s);
        if (lead) s = lead[1].toLowerCase() + s.slice(lead[1].length);
        else if (/^[A-Z]+$/.test(s)) s = s.toLowerCase();
        else s = s.charAt(0).toLowerCase() + s.slice(1);
        return s.slice(0, EXTRA_MAX_NAME);
    }

    function isReservedExtra(name) { return EXTRA_RESERVED.indexOf(String(name).toLowerCase()) >= 0; }

    // "wardsPlaced" -> "Wards placed", "ccScore" -> "CC score". Same words as LeagueExtraStatRules.Label in C#.
    function extraLabel(name) {
        var s = String(name || '');
        var words = [], cur = '';
        for (var i = 0; i < s.length; i++) {
            var c = s.charAt(i), prev = i > 0 ? s.charAt(i - 1) : '', next = i + 1 < s.length ? s.charAt(i + 1) : '';
            var up = c >= 'A' && c <= 'Z', prevUp = prev >= 'A' && prev <= 'Z', nextLow = next >= 'a' && next <= 'z';
            if (cur && up && (!prevUp || nextLow)) { words.push(cur); cur = ''; }
            cur += c;
        }
        if (cur) words.push(cur);
        return words.map(function (w, i) {
            var low = w.toLowerCase();
            if (Object.prototype.hasOwnProperty.call(ACRONYMS, low)) return ACRONYMS[low];
            return i === 0 ? low.charAt(0).toUpperCase() + low.slice(1) : low;
        }).join(' ');
    }

    function parseAnswer(text, ctx) {
        ctx = ctx || {};
        var teams = Array.isArray(ctx.teams) ? ctx.teams : [];
        var plist = Array.isArray(ctx.players) ? ctx.players : [];
        var champList = Array.isArray(ctx.champions) ? ctx.champions : [];
        var res = { found: false, version: null, durationSeconds: null, totals: [], players: [], missing: [], problems: [], extrasCount: 0 };
        function warn(line, msg) { res.problems.push({ level: 'warn', line: line, text: msg }); }
        function info(line, msg) { res.problems.push({ level: 'info', line: line, text: msg }); }

        var bySite = {};   // normalized site name -> player
        plist.forEach(function (p) { bySite[lower(p.site)] = p; });
        var pipeSites = plist.filter(function (p) { return String(p.site).indexOf('|') >= 0; })
            .sort(function (a, b) { return String(b.site).length - String(a.site).length; });
        var byTeam = {};
        teams.forEach(function (t, i) { byTeam[lower(t.name)] = i; });
        var champBy = {};
        champList.forEach(function (c) { champBy[squash(c.name)] = c; });

        var lines = String(text == null ? '' : text).replace(/\r/g, '').split('\n');
        var seen = {};        // player id -> line numbers
        var parsed = [];      // candidate players, before the duplicate check
        var totalsSeen = {};

        for (var n = 0; n < lines.length; n++) {
            var no = n + 1;
            var line = tidyLine(lines[n]);
            if (!line) continue;

            // Header
            var h = /^(?:LTB|HFE)-?STATS\b\s*[:=]?\s*v?(\d+)?/i.exec(line);
            if (h) {
                res.found = true;
                res.version = h[1] ? parseInt(h[1], 10) : null;
                if (res.version !== null && res.version !== FORMAT_VERSION) warn(no, 'The answer says format ' + res.version + ', this page reads format ' + FORMAT_VERSION + '. Read anyway.');
                continue;
            }

            // Match length
            var lm = /^LENGTH\b\s*[:=|]?\s*(.*)$/i.exec(line);
            if (lm && (line.indexOf('|') < 0 || readLength(lm[1]) !== null)) {
                var secs = readLength(lm[1]);
                if (secs === null) { warn(no, 'Match length "' + clean(lm[1]) + '" is not mm:ss. Length left as it is.'); }
                else if (Math.floor(secs / 60) > LIMITS.minutes) { warn(no, 'Match length is over ' + LIMITS.minutes + ' minutes. Left as it is.'); }
                else if (res.durationSeconds !== null && res.durationSeconds !== secs) { warn(no, 'A second LENGTH line was ignored.'); }
                else { res.durationSeconds = secs; res.found = true; }
                continue;
            }

            // Team totals
            var tm = /^TOTALS?\b\s*[:=|]?\s*(.*)$/i.exec(line);
            if (tm) {
                res.found = true;
                var seg = tm[1].split('|').map(function (x) { return x.trim(); });
                var tname = '', nums = null;
                if (seg.length >= 4) { tname = seg[0]; nums = [seg[1], seg[2], seg[3]]; }
                else {
                    // "TOTALS Blue 23/36/22" and similar: the last three numbers, the rest is the team name.
                    var tail = /^(.*?)[\s:]*(\d+)\s*[\/\-,\s]\s*(\d+)\s*[\/\-,\s]\s*(\d+)\s*$/.exec(tm[1].replace(/\|/g, ' '));
                    if (tail) { tname = clean(tail[1]); nums = [tail[2], tail[3], tail[4]]; }
                }
                if (!nums) { warn(no, 'Could not read the TOTALS line: "' + clip(line) + '".'); continue; }
                var ti = Object.prototype.hasOwnProperty.call(byTeam, lower(tname)) ? byTeam[lower(tname)] : null;
                if (ti === null && teams.length === 1) ti = 0;
                if (ti === null) { warn(no, 'TOTALS for "' + clip(tname) + '": no team of this match has that name.'); continue; }
                if (totalsSeen[ti]) { warn(no, 'A second TOTALS line for ' + teams[ti].name + ' was ignored.'); continue; }
                var kda = nums.map(readNumber);
                if (kda.some(function (x) { return x.bad || x.v === null; })) { warn(no, 'TOTALS for ' + teams[ti].name + ' has a value I could not read. Not used for the check.'); continue; }
                totalsSeen[ti] = true;
                res.totals.push({ team: ti, k: kda[0].v, d: kda[1].v, a: kda[2].v });
                continue;
            }

            // Anything without a bar is talk around the block.
            if (line.indexOf('|') < 0) continue;

            // A player line: site name, 11 values, then name=value pairs.
            var site, rest;
            var pre = null;
            for (var q = 0; q < pipeSites.length; q++) {
                var ps = lower(pipeSites[q].site);
                if (lower(line).indexOf(ps) === 0 && /^\s*\|/.test(line.slice(clean(pipeSites[q].site).length))) { pre = pipeSites[q]; break; }
            }
            var segs;
            if (pre) {
                site = clean(pre.site);
                segs = line.slice(site.length).replace(/^\s*\|/, '').split('|');
            } else {
                var all = line.split('|');
                site = clean(all[0]);
                segs = all.slice(1);
            }
            site = site.replace(/^\*+|\*+$/g, '').trim();
            if (!site) { warn(no, 'A line starts with a bar and no name: "' + clip(line) + '".'); continue; }
            var player = Object.prototype.hasOwnProperty.call(bySite, lower(site)) ? bySite[lower(site)] : null;
            if (!player) { warn(no, '"' + clip(site) + '" is not a player of this match. Line skipped.'); continue; }

            var fixed = [], extraSegs = [];
            for (var z = 0; z < segs.length; z++) {
                var sv = segs[z].trim();
                if (extraSegs.length || /^[A-Za-z][A-Za-z0-9_ ]*=/.test(sv)) { if (sv !== '') extraSegs.push(sv); }
                else fixed.push(sv);
            }
            if (fixed.length < FIELDS.length) {
                warn(no, player.site + ': the line has ' + fixed.length + ' values instead of ' + FIELDS.length + ', so I cannot tell which is which. Line skipped.');
                continue;
            }
            if (fixed.length > FIELDS.length) info(no, player.site + ': more than ' + FIELDS.length + ' values, the extra ones were ignored.');

            var item = { id: player.id, site: player.site, line: no, champion: null, lane: null, k: null, d: null, a: null,
                         cs: null, dd: null, dt: null, gold: null, vis: null, obj: null, extras: {} };

            // champion
            var champText = fixed[0].replace(/^\*+|\*+$/g, '').trim();
            if (champText !== '' && !/^(-|\?|n\/a|unknown)$/i.test(champText)) {
                var c = Object.prototype.hasOwnProperty.call(champBy, squash(champText)) ? champBy[squash(champText)] : null;
                if (c) item.champion = { id: c.id, name: c.name };
                else warn(no, player.site + ': champion "' + clip(champText) + '" is not in the site list. Left empty.');
            }
            // lane
            var laneText = fixed[1].replace(/^\*+|\*+$/g, '').trim();
            if (laneText !== '' && !/^(-|\?|n\/a|unknown)$/i.test(laneText)) {
                var lane = laneOf(laneText);
                if (lane === null) warn(no, player.site + ': lane "' + clip(laneText) + '" is not Top, Jungle, Mid, ADC or Support. Left empty.');
                else item.lane = lane;
            }
            // numbers
            for (var f = 2; f < FIELDS.length; f++) {
                var key = FIELDS[f][0];
                var nv = readNumber(fixed[f]);
                if (nv.bad) { warn(no, player.site + ': ' + FIELDS[f][1] + ' "' + clip(fixed[f]) + '" is not a number. Left empty.'); continue; }
                if (nv.v !== null && nv.v > LIMITS[key]) { warn(no, player.site + ': ' + FIELDS[f][1] + ' ' + nv.v + ' is over the limit of ' + LIMITS[key] + ' of the stats form. Left empty.'); continue; }
                item[key] = nv.v;
            }
            // other stats (kept since step 13d): clean name, own boxes skipped, limits as the server has them
            extraSegs.forEach(function (e) {
                var eq = e.indexOf('=');
                if (eq < 1) return;
                var shown = clean(e.slice(0, eq)).replace(/\s+/g, '');
                var name = normalizeExtraName(shown);
                var ev = readNumber(e.slice(eq + 1));
                if (!shown) return;
                if (!name) { info(no, player.site + ': other stat \"' + clip(shown) + '\" has no usable name. Ignored.'); return; }
                if (ev.bad) { info(no, player.site + ': other stat \"' + clip(shown) + '\" is not a number. Ignored.'); return; }
                if (ev.v === null) return;
                if (isReservedExtra(name)) { info(no, player.site + ': \"' + name + '\" already has its own box in the form. Ignored here.'); return; }
                if (ev.v > EXTRA_MAX_VALUE) { info(no, player.site + ': other stat \"' + name + '\" is over ' + EXTRA_MAX_VALUE + '. Ignored.'); return; }
                var lowName = name.toLowerCase();
                var clash = Object.keys(item.extras).some(function (k) { return k.toLowerCase() === lowName; });
                if (clash) { info(no, player.site + ': other stat \"' + name + '\" appears twice. The first one is kept.'); return; }
                if (Object.keys(item.extras).length >= EXTRA_MAX_NAMES) { info(no, player.site + ': more than ' + EXTRA_MAX_NAMES + ' other stats, \"' + name + '\" was ignored.'); return; }
                item.extras[name] = ev.v; res.extrasCount++;
            });

            (seen[player.id] = seen[player.id] || []).push(no);
            parsed.push(item);
            res.found = true;
        }

        // The same player twice: which line is right? Neither is used.
        var dup = {};
        Object.keys(seen).forEach(function (id) { if (seen[id].length > 1) dup[id] = seen[id]; });
        var usable = [];
        parsed.forEach(function (it) {
            if (dup[it.id]) {
                if (seen[it.id][0] === it.line) warn(null, it.site + ' has more than one line (lines ' + dup[it.id].join(', ') + '). None of them is used.');
                return;
            }
            usable.push(it);
        });

        // Players with no usable line
        var have = {};
        usable.forEach(function (it) { have[it.id] = true; });
        plist.forEach(function (p) {
            if (!have[p.id] && !dup[p.id]) { res.missing.push(p.site); }
        });
        // A skipped line of a known player is already explained above; the plain "no line" note is for the rest.
        if (res.missing.length) info(null, 'No usable line for: ' + res.missing.join(', ') + '. Their boxes stay as they are.');
        res.players = usable;

        // K/D/A of each team against the TOTALS line
        res.totals.forEach(function (t) {
            var members = plist.filter(function (p) { return p.team === t.team; });
            [['k', 'kills'], ['d', 'deaths'], ['a', 'assists']].forEach(function (st) {
                var sum = 0, count = 0;
                members.forEach(function (m) {
                    var it = usable.filter(function (u) { return u.id === m.id; })[0];
                    if (it && it[st[0]] !== null) { sum += it[st[0]]; count++; }
                });
                if (count === members.length && members.length > 0) {
                    if (sum !== t[st[0]]) warn(null, teams[t.team].name + ': the ' + st[1] + ' add up to ' + sum + ' but the scoreboard total is ' + t[st[0]] + '. One of the numbers is probably misread.');
                } else {
                    info(null, teams[t.team].name + ': ' + st[1] + ' not checked, ' + (members.length - count) + ' of ' + members.length + ' players have no value.');
                }
            });
        });
        teams.forEach(function (t, i) { if (!totalsSeen[i]) info(null, t.name + ': no TOTALS line, so kills, deaths and assists were not checked.'); });

        if (res.extrasCount > 0) info(null, res.extrasCount + ' other stats were read (wards, turret damage and so on). They go into the form\'s extra row and are saved with Save stats.');
        if (!res.found || (!res.players.length && !res.problems.some(function (p) { return p.level === 'warn'; }))) {
            res.problems.unshift({ level: 'warn', line: null, text: 'I could not find any stats in this text. Paste the whole answer of the AI chat.' });
        }
        return res;
    }

    function clip(v) { var t = clean(v); return t.length > 40 ? t.slice(0, 37) + '...' : t; }

    // -- Score readiness (F1, PLAN-leaderboard-export.md Part 3) ----------------------------------------
    // The all-stats score (LeagueMvpScoring.Score, gate LeagueMvpRules.HasAllStats) exists for a team only when it has at least
    // 2 players and EVERY player has these 9 numbers. The lane is not needed to have a score (it only picks the weights).
    // scoreReadiness is pure: no page access, no throwing.
    //   teams = [{ name, players: [{ name, k, d, a, cs, dd, dt, gold, vis, obj }] }]   (a value is a number or null/undefined)
    // Returns one item per team, same order:
    //   { name, ready, tooSmall, kda, missing: [{ name, fields: ['vision', ...], none }] }
    //   ready    = the team has a score;  tooSmall = fewer than 2 players (no score, whatever the numbers are)
    //   kda      = every player has kills, deaths and assists (then the K/D/A MVP still works)
    //   missing  = the players that lack something; none = true when the player has none of the 9
    var SCORE_FIELDS = [
        ['k', 'kills'], ['d', 'deaths'], ['a', 'assists'], ['cs', 'CS'], ['dd', 'damage dealt'],
        ['dt', 'damage taken'], ['gold', 'gold'], ['vis', 'vision'], ['obj', 'objective damage']
    ];

    function hasNumber(v) { return typeof v === 'number' && isFinite(v) && v >= 0; }

    function scoreReadiness(teams) {
        var out = [];
        var src = Array.isArray(teams) ? teams : [];
        for (var i = 0; i < src.length; i++) {
            var t = src[i] || {};
            var players = Array.isArray(t.players) ? t.players : [];
            var item = { name: clean(t.name) || ('Team ' + (i + 1)), ready: false, tooSmall: players.length < 2, kda: players.length > 0, missing: [] };
            for (var j = 0; j < players.length; j++) {
                var p = players[j] || {};
                var fields = [];
                for (var f = 0; f < SCORE_FIELDS.length; f++) if (!hasNumber(p[SCORE_FIELDS[f][0]])) fields.push(SCORE_FIELDS[f][1]);
                if (!hasNumber(p.k) || !hasNumber(p.d) || !hasNumber(p.a)) item.kda = false;
                if (fields.length) item.missing.push({ name: clean(p.name) || ('Player ' + (j + 1)), fields: fields, none: fields.length === SCORE_FIELDS.length });
            }
            item.ready = !item.tooSmall && item.missing.length === 0;
            out.push(item);
        }
        return out;
    }

    // The one line per team the page shows (result list and fill toast). Pure, so node can test the words.
    // The page does not know the match mode, so the text never says the match is in the leaderboard average.
    function describeReadiness(list) {
        var lines = [];
        var src = Array.isArray(list) ? list : [];
        for (var i = 0; i < src.length; i++) {
            var r = src[i];
            if (!r) continue;
            if (r.ready) { lines.push({ team: r.name, ready: true, text: r.name + ': all stats there, this game counts for the score.' }); continue; }
            if (r.tooSmall) { lines.push({ team: r.name, ready: false, text: r.name + ': no score. A team needs at least 2 players for it.' }); continue; }
            var parts = r.missing.map(function (m) { return m.none ? m.name + ': no stats.' : m.name + ': ' + m.fields.join(', ') + ' missing.'; });
            lines.push({
                team: r.name, ready: false,
                text: r.name + ': no score. ' + parts.join(' ') + ' '
                    + (r.kda ? 'The MVP uses K/D/A for this match.' : 'Kills, deaths and assists are not complete either, so no MVP can be set by itself.')
            });
        }
        return lines;
    }

    var api = { buildPrompt: buildPrompt, parseAnswer: parseAnswer, scoreReadiness: scoreReadiness, describeReadiness: describeReadiness, readNumber: readNumber, readLength: readLength, clean: clean, normalizeExtraName: normalizeExtraName, extraLabel: extraLabel, LIMITS: LIMITS, FORMAT_VERSION: FORMAT_VERSION };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    root.LtbStatsImport = api;
})(typeof window !== 'undefined' ? window : (typeof globalThis !== 'undefined' ? globalThis : this));
