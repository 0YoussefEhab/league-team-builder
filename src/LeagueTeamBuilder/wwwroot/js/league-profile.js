/* League profile page: rank select with badge images, lane level sliders (8f), champion picker.
   Everything here is progressive enhancement: without JS the form still posts (plain selects). */
(function () {
  'use strict';

  /* ---------- Rank select (docs/LEAGUE_BUILD_SPEC.md section 5) ---------- */
  function enhanceRank(root) {
    var select = root.querySelector('[data-lg-rank-select]');
    var div = root.querySelector('[data-lg-div]');
    if (!select) return;
    var labelEl = root.querySelector('.lg-lbl');
    var options = Array.prototype.map.call(select.options, function (o) {
      return { value: o.value, text: o.text, img: o.getAttribute('data-img') };
    });

    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'lg-rank-btn';
    btn.setAttribute('aria-haspopup', 'listbox');
    btn.setAttribute('aria-expanded', 'false');
    if (labelEl && labelEl.id) btn.setAttribute('aria-labelledby', labelEl.id + ' ' + select.id + '-btn');
    btn.id = select.id + '-btn';

    var list = document.createElement('ul');
    list.className = 'lg-rank-list';
    list.setAttribute('role', 'listbox');
    list.id = select.id + '-list';
    list.hidden = true;
    if (labelEl && labelEl.id) list.setAttribute('aria-labelledby', labelEl.id);
    btn.setAttribute('aria-controls', list.id);

    var items = options.map(function (o, i) {
      var li = document.createElement('li');
      li.className = 'lg-rank-opt';
      li.setAttribute('role', 'option');
      li.id = select.id + '-opt-' + i;
      li.dataset.value = o.value;
      li.innerHTML = '<img alt="" src="' + o.img + '"><span></span>';
      li.lastChild.textContent = o.text;
      list.appendChild(li);
      return li;
    });

    var active = 0;
    var typed = '';
    var typedTimer = null;

    function indexOfValue(v) {
      for (var i = 0; i < options.length; i++) if (options[i].value === String(v)) return i;
      return 0;
    }
    function paintButton() {
      var o = options[indexOfValue(select.value)];
      btn.innerHTML = '<img alt="" src="' + o.img + '"><span></span><span aria-hidden="true" style="margin-left:auto">&#9662;</span>';
      btn.children[1].textContent = o.text;
      items.forEach(function (li, i) {
        li.setAttribute('aria-selected', String(options[i].value === select.value));
      });
    }
    function syncDivision() {
      if (!div) return;
      var hasDiv = parseInt(select.value, 10) < 7; // 7 = Master; Master and above have no division
      div.disabled = !hasDiv;
      if (!hasDiv) div.value = '';
    }
    function setActive(i) {
      active = Math.max(0, Math.min(options.length - 1, i));
      items.forEach(function (li, n) { li.classList.toggle('is-active', n === active); });
      list.setAttribute('aria-activedescendant', items[active].id);
      items[active].scrollIntoView({ block: 'nearest' });
    }
    function open() {
      list.hidden = false;
      btn.setAttribute('aria-expanded', 'true');
      setActive(indexOfValue(select.value));
    }
    function close(focusBtn) {
      list.hidden = true;
      btn.setAttribute('aria-expanded', 'false');
      if (focusBtn) btn.focus();
    }
    function choose(i) {
      select.value = options[i].value;
      select.dispatchEvent(new Event('change', { bubbles: true }));
      paintButton();
      syncDivision();
      close(true);
    }

    btn.addEventListener('click', function () { list.hidden ? open() : close(false); });
    btn.addEventListener('keydown', function (e) {
      if (list.hidden) {
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); }
        return;
      }
      if (e.key === 'ArrowDown') { e.preventDefault(); setActive(active + 1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(active - 1); }
      else if (e.key === 'Home') { e.preventDefault(); setActive(0); }
      else if (e.key === 'End') { e.preventDefault(); setActive(options.length - 1); }
      else if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); choose(active); }
      else if (e.key === 'Escape') { e.preventDefault(); close(true); }
      else if (e.key === 'Tab') { close(false); }
      else if (e.key.length === 1) { // type-ahead
        typed += e.key.toLowerCase();
        clearTimeout(typedTimer);
        typedTimer = setTimeout(function () { typed = ''; }, 600);
        for (var i = 0; i < options.length; i++) {
          if (options[i].text.toLowerCase().indexOf(typed) === 0) { setActive(i); break; }
        }
      }
    });
    list.addEventListener('mousedown', function (e) { e.preventDefault(); });
    list.addEventListener('click', function (e) {
      var li = e.target.closest('.lg-rank-opt');
      if (li) choose(items.indexOf(li));
    });
    document.addEventListener('click', function (e) { if (!root.contains(e.target)) close(false); });

    select.hidden = true;
    select.parentNode.insertBefore(btn, select);
    root.appendChild(list);
    select.addEventListener('change', function () { paintButton(); syncDivision(); });
    paintButton();
    syncDivision();
  }

  /* ---------- Lane levels (Step 8f): sliders 1-5, live word, live pyramid, champion pickers ---------- */
  var LEVEL_WORDS = ['Barely', 'Basics', 'Kinda good', 'Strong', 'Main'];

  function enhanceLevels() {
    Array.prototype.forEach.call(document.querySelectorAll('[data-lg-levels]'), function (root) {
      var rows = Array.prototype.slice.call(root.querySelectorAll('[data-lg-sl]'));
      var pyr = document.querySelector('[data-lg-pyr="live"]');

      function levelOf(row) {
        var v = parseInt(row.querySelector('input[type=range]').value, 10);
        return isNaN(v) ? 1 : Math.min(5, Math.max(1, v));
      }

      // Same rule as LeagueLaneLevels.Pyramid: 4-5 Mains, 3 Kinda good, 1-2 Basics; highest level first, then lane order.
      function paintPyramid() {
        if (!pyr) return;
        var tiers = [[], [], []];
        rows.forEach(function (row, i) {
          var lv = levelOf(row);
          tiers[lv >= 4 ? 0 : lv === 3 ? 1 : 2].push({ name: row.getAttribute('data-lane-label'), level: lv, order: i });
        });
        var cls = ['lg-t1', 'lg-t2', 'lg-t3'];
        var labels = ['Mains', 'Kinda good', 'Basics'];
        pyr.textContent = '';
        tiers.forEach(function (list, t) {
          list.sort(function (a, b) { return b.level - a.level || a.order - b.order; });
          var tier = document.createElement('div');
          tier.className = 'lg-ptier ' + cls[t];
          var label = document.createElement('span');
          label.className = 'lg-ptl';
          label.textContent = labels[t];
          tier.appendChild(label);
          var lns = document.createElement('div');
          lns.className = 'lg-lns';
          if (!list.length) {
            var none = document.createElement('span');
            none.className = 'lg-pnone';
            none.textContent = 'none';
            lns.appendChild(none);
          }
          list.forEach(function (x) {
            var b = document.createElement('span');
            b.className = 'lg-lnb' + (x.level === 1 ? ' is-dim' : '');
            b.textContent = x.name;
            lns.appendChild(b);
          });
          tier.appendChild(lns);
          pyr.appendChild(tier);
        });
      }

      function paintRow(row) {
        var range = row.querySelector('input[type=range]');
        var lv = levelOf(row);
        range.style.setProperty('--p', ((lv - 1) * 25) + '%');
        var out = row.querySelector('output');
        if (out) out.textContent = LEVEL_WORDS[lv - 1];
        // Profile page only: a lane at level 1 is not played, so its champion picker is hidden.
        var champs = document.querySelector('[data-lg-champ-lane="' + row.getAttribute('data-lg-sl') + '"]');
        if (champs) champs.hidden = lv <= 1;
      }

      rows.forEach(function (row) {
        paintRow(row);
        row.querySelector('input[type=range]').addEventListener('input', function () {
          paintRow(row);
          paintPyramid();
        });
      });
      paintPyramid();
    });
  }

  /* ---------- Champion picker (docs/LEAGUE_BUILD_SPEC.md section 7) ---------- */
  function enhanceChampions() {
    var catalogEl = document.getElementById('lg-champ-catalog');
    if (!catalogEl) return;
    var catalog = [];
    try { catalog = JSON.parse(catalogEl.textContent) || []; } catch (e) { catalog = []; }

    Array.prototype.forEach.call(document.querySelectorAll('[data-lg-champ]'), function (root) {
      var field = root.getAttribute('data-field');
      var chips = root.querySelector('[data-lg-chips]');
      var input = root.querySelector('[data-lg-champ-input]');
      var list = root.querySelector('[data-lg-champ-list]');
      var matches = [];
      var active = -1;

      function selectedIds() {
        return Array.prototype.map.call(chips.querySelectorAll('input[type=hidden]'), function (h) { return h.value; });
      }
      function addChip(id, name) {
        if (selectedIds().indexOf(String(id)) !== -1) return;
        var chip = document.createElement('span');
        chip.className = 'lg-chip';
        chip.setAttribute('data-id', id);
        var hidden = document.createElement('input');
        hidden.type = 'hidden'; hidden.name = field; hidden.value = id;
        var label = document.createElement('span');
        label.textContent = name;
        var x = document.createElement('button');
        x.type = 'button'; x.className = 'lg-chip-x'; x.setAttribute('aria-label', 'Remove ' + name);
        x.innerHTML = '&times;';
        chip.appendChild(hidden); chip.appendChild(label); chip.appendChild(x);
        chips.appendChild(chip);
      }
      function closeList() {
        list.hidden = true; list.innerHTML = '';
        input.setAttribute('aria-expanded', 'false');
        input.removeAttribute('aria-activedescendant');
        active = -1;
      }
      function paintActive() {
        Array.prototype.forEach.call(list.children, function (li, i) { li.classList.toggle('is-active', i === active); });
        if (active >= 0 && list.children[active]) {
          input.setAttribute('aria-activedescendant', list.children[active].id);
          list.children[active].scrollIntoView({ block: 'nearest' });
        }
      }
      function render() {
        var q = input.value.trim().toLowerCase();
        var taken = selectedIds();
        matches = catalog.filter(function (c) {
          return taken.indexOf(String(c.i)) === -1 && (q === '' || c.n.toLowerCase().indexOf(q) !== -1);
        }).slice(0, 40);
        list.innerHTML = '';
        if (!matches.length) {
          var empty = document.createElement('li');
          empty.className = 'lg-champ-empty';
          empty.textContent = 'No matching champion';
          list.appendChild(empty);
        }
        matches.forEach(function (c, i) {
          var li = document.createElement('li');
          li.className = 'lg-champ-opt';
          li.setAttribute('role', 'option');
          li.id = field + '-opt-' + i;
          li.dataset.id = c.i;
          li.textContent = c.n;
          list.appendChild(li);
        });
        list.hidden = false;
        input.setAttribute('aria-expanded', 'true');
        active = matches.length ? 0 : -1;
        paintActive();
      }
      function pick(i) {
        var c = matches[i];
        if (!c) return;
        addChip(c.i, c.n);
        input.value = '';
        closeList();
        input.focus();
      }

      input.addEventListener('input', render);
      input.addEventListener('focus', render);
      input.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowDown') { e.preventDefault(); if (list.hidden) render(); else { active = Math.min(matches.length - 1, active + 1); paintActive(); } }
        else if (e.key === 'ArrowUp') { e.preventDefault(); active = Math.max(0, active - 1); paintActive(); }
        else if (e.key === 'Enter') { if (!list.hidden && active >= 0) { e.preventDefault(); pick(active); } }
        else if (e.key === 'Escape') { closeList(); }
      });
      list.addEventListener('mousedown', function (e) { e.preventDefault(); });
      list.addEventListener('click', function (e) {
        var li = e.target.closest('.lg-champ-opt');
        if (li) pick(Array.prototype.indexOf.call(list.children, li));
      });
      chips.addEventListener('click', function (e) {
        var x = e.target.closest('.lg-chip-x');
        if (!x) return;
        var chip = x.closest('.lg-chip');
        chip.parentNode.removeChild(chip);
        input.focus();
      });
      document.addEventListener('click', function (e) { if (!root.contains(e.target)) closeList(); });
    });
  }

  document.addEventListener('DOMContentLoaded', function () {
    Array.prototype.forEach.call(document.querySelectorAll('[data-lg-rank]'), enhanceRank);
    enhanceLevels();
    enhanceChampions();
  });
})();
