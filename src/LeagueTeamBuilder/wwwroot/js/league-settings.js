/* League settings (docs/LEAGUE_BUILD_SPEC.md sections 3 and 11). Everything here only enhances forms
   that already work without JavaScript:
   - Map spots: the X / Y fields are the source of truth; this adds circles on the map with click to
     place, drag, and arrow-key nudge (Shift = 5%), kept in sync with the fields.
   - Guest form: hides the peak rank picker while "peak is the same as current" is ticked. */
(function () {
  'use strict';

  function round1(n) { return Math.round(n * 10) / 10; }
  function clamp(n) { return Math.min(100, Math.max(0, n)); }

  function enhanceSpots(form) {
    var map = form.querySelector('[data-lg-map]');
    var rows = Array.prototype.slice.call(form.querySelectorAll('[data-lg-row]')).map(function (tr) {
      return {
        tr: tr,
        x: tr.querySelector('[data-lg-x]'),
        y: tr.querySelector('[data-lg-y]'),
        team: tr.getAttribute('data-team'),
        label: tr.getAttribute('data-label'),
        pin: null
      };
    });
    var nextLabel = form.querySelector('[data-lg-next]');
    var clearBtn = form.querySelector('[data-lg-clear]');
    if (!map || !rows.length) return;

    function hasPos(r) {
      return r.x.value !== '' && r.y.value !== '' && !isNaN(parseFloat(r.x.value)) && !isNaN(parseFloat(r.y.value));
    }

    function place(r) {
      if (!hasPos(r)) {
        if (r.pin) { r.pin.parentNode.removeChild(r.pin); r.pin = null; }
        return;
      }
      if (!r.pin) {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = 'lg-pin t' + r.team;
        b.textContent = r.label.split(' ').join('\n');
        b.setAttribute('aria-label', r.label + ' position. Drag or use the arrow keys to move.');
        b._row = r;
        map.appendChild(b);
        r.pin = b;
      }
      r.pin.style.left = clamp(parseFloat(r.x.value)) + '%';
      r.pin.style.top = clamp(parseFloat(r.y.value)) + '%';
    }

    function refreshNext() {
      if (!nextLabel) return;
      var next = rows.filter(function (r) { return !hasPos(r); })[0];
      nextLabel.textContent = next ? 'Next circle: ' + next.label : 'Every position is placed';
    }

    function setPos(r, x, y) {
      r.x.value = round1(clamp(x));
      r.y.value = round1(clamp(y));
      place(r);
    }

    function refreshAll() { rows.forEach(place); refreshNext(); }

    // Typing in the fields moves the circle.
    rows.forEach(function (r) {
      ['input', 'change'].forEach(function (ev) {
        r.x.addEventListener(ev, function () { place(r); refreshNext(); });
        r.y.addEventListener(ev, function () { place(r); refreshNext(); });
      });
    });

    // Click on an empty part of the map: drop the next unplaced circle there.
    map.addEventListener('click', function (e) {
      if (e.target.closest('.lg-pin')) return;
      var next = rows.filter(function (r) { return !hasPos(r); })[0];
      if (!next) return;
      var box = map.getBoundingClientRect();
      setPos(next, (e.clientX - box.left) / box.width * 100, (e.clientY - box.top) / box.height * 100);
      refreshNext();
    });

    // Drag a circle.
    var drag = null;
    map.addEventListener('pointerdown', function (e) {
      var pin = e.target.closest('.lg-pin');
      if (!pin) return;
      e.preventDefault();
      pin.focus();
      if (pin.setPointerCapture) pin.setPointerCapture(e.pointerId);
      drag = pin;
    });
    map.addEventListener('pointermove', function (e) {
      if (!drag) return;
      var box = map.getBoundingClientRect();
      setPos(drag._row, (e.clientX - box.left) / box.width * 100, (e.clientY - box.top) / box.height * 100);
    });
    ['pointerup', 'pointercancel'].forEach(function (t) {
      map.addEventListener(t, function () { drag = null; });
    });

    // Arrow keys nudge the focused circle by 1% (Shift: 5%).
    map.addEventListener('keydown', function (e) {
      var pin = e.target.closest('.lg-pin');
      if (!pin || e.key.indexOf('Arrow') !== 0) return;
      e.preventDefault();
      var r = pin._row, step = e.shiftKey ? 5 : 1;
      var x = parseFloat(r.x.value), y = parseFloat(r.y.value);
      if (e.key === 'ArrowLeft') x -= step;
      if (e.key === 'ArrowRight') x += step;
      if (e.key === 'ArrowUp') y -= step;
      if (e.key === 'ArrowDown') y += step;
      setPos(r, x, y);
    });

    if (clearBtn) {
      clearBtn.addEventListener('click', function () {
        rows.forEach(function (r) { r.x.value = ''; r.y.value = ''; });
        refreshAll();
      });
    }

    refreshAll();
  }

  function enhancePeakToggle() {
    var toggle = document.querySelector('[data-lg-peak-toggle]');
    var wrap = document.querySelector('[data-lg-peak-wrap]');
    if (!toggle || !wrap) return;
    function sync() { wrap.hidden = toggle.checked; }
    toggle.addEventListener('change', sync);
    sync();
  }

  document.addEventListener('DOMContentLoaded', function () {
    Array.prototype.forEach.call(document.querySelectorAll('[data-lg-spots]'), enhanceSpots);
    enhancePeakToggle();
  });
})();
