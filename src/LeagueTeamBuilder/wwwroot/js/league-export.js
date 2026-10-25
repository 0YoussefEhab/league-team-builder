/* League team image for Discord (Step 4e). Draws the teams on the builder page onto a 1280 x 720 canvas, entirely in
   the browser: nothing is sent to the server. Ported from the prototype's drawExport. Per spec R9 it draws the map
   image as the background only (no player circles). Colours are fixed (white text on a darkened background) so the
   picture reads the same whatever site theme the viewer uses. Exposes window.LeagueExport.draw(canvas, spec):
   spec = { title, subtitle, mapUrl, teams: [{ name, rows: [{ tag, name (null = open slot), sub }] }] }.
   It resolves to { mapUsed } (false when a map was asked for but could not be used).

   Map picture (PLAN-leaderboard-export.md, Part 2, step E2): window.LeagueExport.drawMap(canvas, spec) draws the map
   whole with the players placed on it, the way the Builder page shows them. It takes the same spec plus
   spec.mapAspect (width / height of the map box, default 16 / 9), spec.spots: [{ team, slot, x, y }] (x and y are
   percentages of the map box, as saved in the map settings), and per row: slot (the slot number the spot points to;
   when a row has none, its place in the list is used), avatar (photo address or null), guest (true = dashed ring).
   It resolves to { mapUsed, photosUsed, photosTotal }. With no spots it draws the list picture instead. */
(function (root) {
    'use strict';

    var W = 1280, H = 720;
    var PALETTE = ['#4f7cff', '#ef5a4f', '#2fb67c', '#e6a828', '#a366ff', '#27b5d6', '#ff7ab6', '#9aa5b1'];
    var INK = '#ffffff';
    var MUTED = 'rgba(255,255,255,.72)';

    function cssVar(name, fallback) {
        var v = getComputedStyle(document.documentElement).getPropertyValue(name);
        return v && v.trim() ? v.trim() : fallback;
    }

    // Text that is too wide gets cut with an ellipsis, so a long name never runs out of its box.
    function fit(ctx, text, maxW) {
        if (ctx.measureText(text).width <= maxW) return text;
        var t = text;
        while (t.length > 1 && ctx.measureText(t + '…').width > maxW) t = t.slice(0, -1);
        return t + '…';
    }

    // Resolves to the image, or null when it cannot be used. crossOrigin keeps the canvas exportable; an image host
    // that sends no CORS headers fails here and the caller falls back to the plain background.
    function loadImage(url) {
        return new Promise(function (resolve) {
            var im = new Image();
            im.crossOrigin = 'anonymous';
            im.onload = function () { resolve(im.width > 0 && im.height > 0 ? im : null); };
            im.onerror = function () { resolve(null); };
            im.src = url;
        });
    }

    function background(ctx, im, accent) {
        ctx.fillStyle = '#0e1015';
        ctx.fillRect(0, 0, W, H);
        if (im) {
            var s = Math.max(W / im.width, H / im.height);
            ctx.drawImage(im, (W - im.width * s) / 2, (H - im.height * s) / 2, im.width * s, im.height * s);
            ctx.fillStyle = 'rgba(0,0,0,.58)';
            ctx.fillRect(0, 0, W, H);
            return;
        }
        var glow = ctx.createRadialGradient(300, 100, 10, 300, 100, 760);
        glow.addColorStop(0, accent + '55');
        glow.addColorStop(1, 'rgba(14,16,21,0)');
        ctx.fillStyle = glow;
        ctx.fillRect(0, 0, W, H);
        ctx.strokeStyle = 'rgba(255,255,255,.07)';
        ctx.lineWidth = 1;
        for (var x = 0; x <= W; x += 64) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, H); ctx.stroke(); }
        for (var y = 0; y <= H; y += 64) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke(); }
    }

    function card(ctx, x, y, w, h, color) {
        ctx.fillStyle = 'rgba(10,10,12,.74)';
        ctx.fillRect(x, y, w, h);
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        ctx.strokeRect(x, y, w, h);
    }

    // Two teams side by side, one row per slot.
    function drawTwo(ctx, spec, fam, display) {
        var rows = Math.max(1, spec.teams[0].rows.length, spec.teams[1].rows.length);
        var rh = Math.min(84, (H - 190) / rows);
        var big = rh >= 52;
        var nameSize = Math.max(11, Math.min(26, Math.floor(rh * 0.36)));
        var subSize = Math.max(10, Math.min(16, Math.floor(rh * 0.22)));
        var tagSize = Math.max(10, Math.min(16, Math.floor(rh * 0.26)));
        var colW = 564;

        spec.teams.forEach(function (t, ti) {
            var x = 48 + ti * 620;
            var color = PALETTE[ti % PALETTE.length];
            ctx.textBaseline = 'alphabetic';
            ctx.fillStyle = color;
            ctx.font = '700 34px ' + display;
            ctx.fillText(fit(ctx, t.name.toUpperCase(), colW), x, 160);

            t.rows.forEach(function (row, i) {
                var y = 180 + i * rh, ch = rh - 8, mid = y + ch / 2;
                card(ctx, x, y, colW, ch, color);
                ctx.textBaseline = 'middle';
                ctx.fillStyle = color;
                ctx.font = '700 ' + tagSize + 'px ' + fam;
                ctx.fillText(fit(ctx, row.tag.toUpperCase(), 100), x + 16, mid);

                var open = row.name == null;
                ctx.fillStyle = open ? MUTED : INK;
                ctx.font = '700 ' + nameSize + 'px ' + fam;
                if (big) {
                    ctx.fillText(fit(ctx, open ? 'Open slot' : row.name, colW - 150), x + 130, mid - (row.sub && !open ? 9 : 0));
                    if (row.sub && !open) {
                        ctx.fillStyle = MUTED;
                        ctx.font = '500 ' + subSize + 'px ' + fam;
                        ctx.fillText(fit(ctx, row.sub, colW - 150), x + 130, mid + 15);
                    }
                } else {
                    // Short rows: the rank goes on the right of the same line.
                    var subW = 0;
                    if (row.sub && !open) {
                        ctx.font = '500 ' + subSize + 'px ' + fam;
                        subW = ctx.measureText(row.sub).width + 12;
                        ctx.fillStyle = MUTED;
                        ctx.textAlign = 'right';
                        ctx.fillText(row.sub, x + colW - 14, mid);
                        ctx.textAlign = 'left';
                    }
                    ctx.fillStyle = open ? MUTED : INK;
                    ctx.font = '700 ' + nameSize + 'px ' + fam;
                    ctx.fillText(fit(ctx, open ? 'Open slot' : row.name, colW - 130 - subW), x + 110, mid);
                }
            });
        });
    }

    // Any other number of teams (Arena): a grid of small cards.
    function drawGrid(ctx, spec, fam, display) {
        var T = spec.teams.length;
        var cols = T <= 8 ? 4 : 5;
        var gridRows = Math.ceil(T / cols);
        var gap = 14, top = 130, left = 48;
        var w = (W - left * 2 - gap * (cols - 1)) / cols;
        var h = (H - top - 30 - gap * (gridRows - 1)) / gridRows;
        var most = Math.max(1, Math.max.apply(null, spec.teams.map(function (t) { return t.rows.length; })));
        var headH = Math.min(46, h * 0.28);
        var rh = Math.min(48, (h - headH - 10) / most);
        var headSize = Math.max(12, Math.min(26, Math.floor(headH * 0.6)));
        var nameSize = Math.max(10, Math.min(22, Math.floor(rh * 0.5)));
        var subSize = Math.max(9, Math.min(14, Math.floor(rh * 0.3)));

        spec.teams.forEach(function (t, ti) {
            var x = left + (ti % cols) * (w + gap);
            var y = top + Math.floor(ti / cols) * (h + gap);
            var color = PALETTE[ti % PALETTE.length];
            card(ctx, x, y, w, h, color);
            ctx.textBaseline = 'middle';
            ctx.fillStyle = color;
            ctx.font = '700 ' + headSize + 'px ' + display;
            ctx.fillText(fit(ctx, t.name.toUpperCase(), w - 24), x + 12, y + headH / 2 + 2);
            t.rows.forEach(function (row, i) {
                var cy = y + headH + 6 + i * rh + rh / 2;
                var open = row.name == null;
                ctx.fillStyle = open ? MUTED : INK;
                ctx.font = '700 ' + nameSize + 'px ' + fam;
                var subW = 0;
                if (row.sub && !open && rh >= 22) {
                    ctx.font = '500 ' + subSize + 'px ' + fam;
                    subW = ctx.measureText(row.sub).width + 10;
                    ctx.fillStyle = MUTED;
                    ctx.textAlign = 'right';
                    ctx.fillText(row.sub, x + w - 12, cy);
                    ctx.textAlign = 'left';
                }
                ctx.fillStyle = open ? MUTED : INK;
                ctx.font = '700 ' + nameSize + 'px ' + fam;
                ctx.fillText(fit(ctx, open ? 'Open slot' : row.name, w - 24 - subW), x + 12, cy);
            });
        });
    }

    function draw(canvas, spec) {
        canvas.width = W;
        canvas.height = H;
        var ctx = canvas.getContext('2d');
        var fam = getComputedStyle(document.body).fontFamily || 'sans-serif';
        var display = cssVar('--hfe-font-display', '') || fam;
        var accent = /^#[0-9a-f]{6}$/i.test(cssVar('--hfe-accent', '')) ? cssVar('--hfe-accent', '') : '#3b6cf6';

        var fonts = document.fonts && document.fonts.ready ? document.fonts.ready : Promise.resolve();
        var img = spec.mapUrl ? fonts.then(function () { return loadImage(spec.mapUrl); }) : fonts.then(function () { return null; });

        return img.then(function (im) {
            background(ctx, im, accent);
            ctx.textBaseline = 'alphabetic';
            ctx.textAlign = 'left';
            ctx.fillStyle = INK;
            ctx.font = '700 48px ' + display;
            ctx.fillText(fit(ctx, spec.title.toUpperCase(), W - 96), 48, 70);
            ctx.fillStyle = MUTED;
            ctx.font = '500 18px ' + fam;
            ctx.fillText(spec.subtitle, 48, 100);

            if (spec.teams.length === 2) drawTwo(ctx, spec, fam, display);
            else drawGrid(ctx, spec, fam, display);
            return { mapUsed: !!im };
        });
    }

    /* ---------- map picture (E2) ---------- */

    // Same as loadImage, but gives up after ms so one slow photo cannot hold the picture back.
    function loadImageWithin(url, ms) {
        return new Promise(function (resolve) {
            var done = false;
            var timer = setTimeout(function () { if (!done) { done = true; resolve(null); } }, ms);
            loadImage(url).then(function (im) {
                if (done) return;
                done = true;
                clearTimeout(timer);
                resolve(im);
            });
        });
    }

    // Two letters, like the Builder page: first letters of the first two words, else the first two letters.
    function initials(name) {
        var parts = String(name || '').trim().split(/\s+/).filter(Boolean);
        if (!parts.length) return '?';
        var a = Array.from(parts[0]);
        var b = parts.length > 1 ? Array.from(parts[1]) : [];
        return (a[0] + (b.length ? b[0] : (a[1] || ''))).toUpperCase();
    }

    function roundRect(ctx, x, y, w, h, r) {
        r = Math.min(r, w / 2, h / 2);
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.lineTo(x + w - r, y);
        ctx.arcTo(x + w, y, x + w, y + r, r);
        ctx.lineTo(x + w, y + h - r);
        ctx.arcTo(x + w, y + h, x + w - r, y + h, r);
        ctx.lineTo(x + r, y + h);
        ctx.arcTo(x, y + h, x, y + h - r, r);
        ctx.lineTo(x, y + r);
        ctx.arcTo(x, y, x + r, y, r);
        ctx.closePath();
    }

    // The map is fitted whole inside the canvas (never cropped), so the spots stay where the settings put them.
    function mapBox(aspect) {
        var a = aspect > 0 ? aspect : 16 / 9;
        var w = W, h = W / a;
        if (h > H) { h = H; w = H * a; }
        return { x: (W - w) / 2, y: (H - h) / 2, w: w, h: h };
    }

    function drawMapBackground(ctx, box, im) {
        ctx.fillStyle = '#0e1015';
        ctx.fillRect(0, 0, W, H);
        ctx.save();
        ctx.beginPath();
        ctx.rect(box.x, box.y, box.w, box.h);
        ctx.clip();
        if (im) {
            // Like the Builder page: the image covers the map box.
            var s = Math.max(box.w / im.width, box.h / im.height);
            ctx.drawImage(im, box.x + (box.w - im.width * s) / 2, box.y + (box.h - im.height * s) / 2, im.width * s, im.height * s);
            ctx.fillStyle = 'rgba(0,0,0,.3)';
            ctx.fillRect(box.x, box.y, box.w, box.h);
        } else {
            ctx.fillStyle = '#161a22';
            ctx.fillRect(box.x, box.y, box.w, box.h);
            ctx.strokeStyle = 'rgba(255,255,255,.07)';
            ctx.lineWidth = 1;
            for (var x = 0; x <= box.w; x += 64) { ctx.beginPath(); ctx.moveTo(box.x + x, box.y); ctx.lineTo(box.x + x, box.y + box.h); ctx.stroke(); }
            for (var y = 0; y <= box.h; y += 64) { ctx.beginPath(); ctx.moveTo(box.x, box.y + y); ctx.lineTo(box.x + box.w, box.y + y); ctx.stroke(); }
        }
        ctx.restore();
        ctx.strokeStyle = 'rgba(255,255,255,.14)';
        ctx.lineWidth = 1;
        ctx.strokeRect(box.x + .5, box.y + .5, box.w - 1, box.h - 1);
    }

    // Small label in the top left corner: the title and what each ring colour means. Pins are drawn after it.
    function drawMapLabel(ctx, spec, fam, display) {
        if (!spec.title) return;
        var pad = 12, x = 20, y = 18;
        ctx.textBaseline = 'alphabetic';
        ctx.textAlign = 'left';
        ctx.font = '700 22px ' + display;
        var titleText = fit(ctx, String(spec.title).toUpperCase(), 520);
        var titleW = ctx.measureText(titleText).width;
        ctx.font = '700 14px ' + fam;
        var names = (spec.teams || []).map(function (t, i) {
            var n = fit(ctx, t.name, 150);
            return { text: n, w: ctx.measureText(n).width, color: PALETTE[i % PALETTE.length] };
        });
        var legendW = names.reduce(function (s, n) { return s + 12 + 6 + n.w + 14; }, 0);
        var w = Math.max(titleW, legendW) + pad * 2;
        var h = names.length ? 66 : 40;
        ctx.fillStyle = 'rgba(8,8,10,.72)';
        roundRect(ctx, x, y, w, h, 10);
        ctx.fill();
        ctx.fillStyle = INK;
        ctx.font = '700 22px ' + display;
        ctx.fillText(titleText, x + pad, y + 28);
        var cx = x + pad;
        ctx.font = '700 14px ' + fam;
        names.forEach(function (n) {
            ctx.fillStyle = n.color;
            ctx.beginPath();
            ctx.arc(cx + 6, y + 49, 6, 0, Math.PI * 2);
            ctx.fill();
            ctx.fillStyle = INK;
            ctx.fillText(n.text, cx + 18, y + 54);
            cx += 12 + 6 + n.w + 14;
        });
    }

    // One player: circle (photo or initials) with the team ring, name under it, lane or #n under that. The whole
    // stack is centred on the spot, like the pin on the Builder page.
    function drawPin(ctx, cx, cy, pin, fam) {
        var D = 64, GAP = 3, PILL_H = 22, TAG_H = 14;
        var stackH = D + GAP + PILL_H + GAP + TAG_H;
        var top = cy - stackH / 2;
        var mx = cx, my = top + D / 2;
        var open = pin.name == null;

        // circle
        ctx.save();
        if (open) ctx.globalAlpha = .55;
        ctx.beginPath();
        ctx.arc(mx, my, D / 2 - 1.5, 0, Math.PI * 2);
        ctx.fillStyle = '#1c1b19';
        ctx.fill();
        if (!open && pin.image) {
            ctx.save();
            ctx.clip();
            var s = Math.max(D / pin.image.width, D / pin.image.height);
            ctx.drawImage(pin.image, mx - pin.image.width * s / 2, my - pin.image.height * s / 2, pin.image.width * s, pin.image.height * s);
            ctx.restore();
        } else {
            ctx.fillStyle = '#ffffff';
            ctx.font = '700 ' + (open ? 30 : 24) + 'px ' + fam;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText(open ? '+' : initials(pin.name), mx, my + 1);
        }
        ctx.beginPath();
        ctx.arc(mx, my, D / 2 - 1.5, 0, Math.PI * 2);
        ctx.strokeStyle = pin.color;
        ctx.lineWidth = 3;
        if (open || pin.guest) ctx.setLineDash([7, 5]);
        ctx.stroke();
        ctx.setLineDash([]);
        ctx.restore();

        // name
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.font = '700 14px ' + fam;
        var label = fit(ctx, open ? 'Open' : pin.name, 112);
        var pw = Math.min(128, ctx.measureText(label).width + 18);
        var py = top + D + GAP;
        ctx.fillStyle = 'rgba(8,8,8,.78)';
        roundRect(ctx, mx - pw / 2, py, pw, PILL_H, PILL_H / 2);
        ctx.fill();
        ctx.fillStyle = '#ffffff';
        ctx.fillText(label, mx, py + PILL_H / 2 + 1);

        // lane or #n
        ctx.save();
        ctx.font = '700 12px ' + fam;
        if ('letterSpacing' in ctx) ctx.letterSpacing = '1.5px';
        ctx.shadowColor = 'rgba(0,0,0,.9)';
        ctx.shadowBlur = 3;
        ctx.shadowOffsetY = 1;
        ctx.fillStyle = '#ffffff';
        ctx.fillText(fit(ctx, String(pin.tag || '').toUpperCase(), 120), mx, py + PILL_H + GAP + TAG_H / 2);
        ctx.restore();
        ctx.textAlign = 'left';
        ctx.textBaseline = 'alphabetic';
    }

    function drawMap(canvas, spec) {
        var spots = (spec.spots || []).filter(function (sp) {
            return sp && isFinite(sp.x) && isFinite(sp.y) && spec.teams && spec.teams[sp.team];
        });
        // No places to draw (Arena): the list picture is the picture.
        if (!spots.length) return draw(canvas, spec);

        canvas.width = W;
        canvas.height = H;
        var ctx = canvas.getContext('2d');
        var fam = getComputedStyle(document.body).fontFamily || 'sans-serif';
        var display = cssVar('--hfe-font-display', '') || fam;
        var box = mapBox(spec.mapAspect);

        // One pin per spot. A spot whose slot has no row (an ARAM slot nobody fills) is left out.
        var pins = [];
        spots.forEach(function (sp) {
            var rows = spec.teams[sp.team].rows || [];
            var row = null;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].slot === sp.slot) { row = rows[i]; break; }
            }
            if (!row && rows.every(function (r) { return r.slot === undefined; })) row = rows[sp.slot] || null;
            if (!row) return;
            pins.push({ x: sp.x, y: sp.y, name: row.name, tag: row.tag, guest: !!row.guest, avatar: row.avatar || null,
                color: PALETTE[sp.team % PALETTE.length], image: null });
        });

        var fonts = document.fonts && document.fonts.ready ? document.fonts.ready : Promise.resolve();
        return fonts.then(function () {
            var mapP = spec.mapUrl ? loadImage(spec.mapUrl) : Promise.resolve(null);
            var photoP = pins.map(function (p) {
                return p.name != null && p.avatar ? loadImageWithin(p.avatar, 8000) : Promise.resolve(null);
            });
            return Promise.all([mapP, Promise.all(photoP)]);
        }).then(function (got) {
            var im = got[0], photos = got[1];
            var total = 0, used = 0;
            pins.forEach(function (p, i) {
                if (p.name != null && p.avatar) { total++; if (photos[i]) used++; }
                p.image = photos[i];
            });
            drawMapBackground(ctx, box, im);
            drawMapLabel(ctx, spec, fam, display);
            pins.forEach(function (p) {
                drawPin(ctx, box.x + box.w * p.x / 100, box.y + box.h * p.y / 100, p, fam);
            });
            return { mapUsed: !!im, photosUsed: used, photosTotal: total };
        });
    }

    root.LeagueExport = { draw: draw, drawMap: drawMap, WIDTH: W, HEIGHT: H };
})(window);
