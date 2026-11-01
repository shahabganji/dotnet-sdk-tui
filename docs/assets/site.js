/*
  .NET SDK Manager docs — shared behaviour.
  The sidebar is rendered from NAV below (single source of truth for every page).
  Each docs page sets <body data-page="docs|guide|guide-brew">; the landing page uses data-page="home".
*/
(function () {
  'use strict';

  var REPO = 'shahabganji/dotnet-sdk-tui';

  // ── Site map ──────────────────────────────────────────
  // topic: a top-level card in the sidebar. groups: the tree shown when that topic is active.
  var NAV = [
    {
      id: 'overview', title: 'Overview', icon: 'bi-book', page: 'docs',
      groups: [
        { title: 'Welcome', items: [
          { title: 'Introduction', page: 'docs', hash: 'overview' },
          { title: 'Choose your path', page: 'docs', hash: 'paths' },
          { title: 'Features', page: 'docs', hash: 'features' }
        ] },
        { title: 'Get started', items: [
          { title: 'Installation', page: 'docs', hash: 'install' },
          { title: 'Prerequisites', page: 'docs', hash: 'prerequisites' },
          { title: 'Troubleshooting', page: 'docs', hash: 'troubleshooting' }
        ] },
        { title: 'Guides', items: [
          { title: '.NET SDKs', page: 'guide', badge: 'Tour', badgeKind: 'tip' },
          { title: 'Homebrew', page: 'guide-brew', badge: 'macOS', badgeKind: 'note', macOnly: true }
        ] },
        { title: 'Reference', items: [
          { title: 'Keyboard shortcuts', page: 'docs', hash: 'usage' },
          { title: 'Search syntax', page: 'docs', hash: 'search-syntax' },
          { title: 'Command line', page: 'docs', hash: 'cli' }
        ] },
        { title: 'Contribute', items: [
          { title: 'Build from source', page: 'docs', hash: 'build' },
          { title: 'Contributing', page: 'docs', hash: 'contributing' }
        ] }
      ]
    },
    {
      id: 'dotnet', title: '.NET guide', icon: 'bi-terminal', page: 'guide',
      groups: [
        { title: 'Before you start', items: [
          { title: 'Start here', page: 'guide', hash: 'start-here' },
          { title: 'Lifecycle icons', page: 'guide', hash: 'lifecycle' }
        ] },
        { title: 'First steps', items: [
          { title: 'Launch dsm', page: 'guide', hash: 'launch' },
          { title: 'Install dotnetup', page: 'guide', hash: 'dotnetup' },
          { title: 'Migrate existing SDKs', page: 'guide', hash: 'migrate' }
        ] },
        { title: 'Manage SDKs', items: [
          { title: 'Install a .NET SDK', page: 'guide', hash: 'install-sdk' },
          { title: 'Search for older versions', page: 'guide', hash: 'search' },
          { title: 'Install from search', page: 'guide', hash: 'install-search' },
          { title: 'Manage workloads', page: 'guide', hash: 'workloads' },
          { title: 'Uninstall an SDK', page: 'guide', hash: 'uninstall' }
        ] }
      ]
    },
    {
      id: 'brew', title: 'Homebrew guide', icon: 'bi-cup-hot', page: 'guide-brew', macOnly: true,
      groups: [
        { title: 'Homebrew workspace', items: [
          { title: 'Reading the list', page: 'guide-brew', hash: 'legend' },
          { title: 'Open the workspace', page: 'guide-brew', hash: 'open' },
          { title: 'Search & install', page: 'guide-brew', hash: 'search' },
          { title: 'Upgrade & uninstall', page: 'guide-brew', hash: 'manage' }
        ] }
      ]
    },
    { id: 'github', title: 'GitHub', icon: 'bi-github', external: 'https://github.com/' + REPO }
  ];

  var root = document.documentElement;
  var page = document.body.getAttribute('data-page') || 'docs';
  var isMac = (function () {
    var ua = navigator.userAgent || '';
    var plat = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '';
    return /Mac/i.test(plat + ' ' + ua) && !/iPhone|iPad|iPod/i.test(ua);
  })();
  var isWindows = /Win/i.test((navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '');

  function $(sel, ctx) { return (ctx || document).querySelector(sel); }
  function $all(sel, ctx) { return Array.prototype.slice.call((ctx || document).querySelectorAll(sel)); }
  function esc(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function hrefFor(item) {
    if (item.external) return item.external;
    var base = item.page === page ? '' : item.page + '.html';
    if (item.hash) return base + '#' + item.hash;
    return base || '#';
  }
  function store(key, value) { try { if (value === null) localStorage.removeItem(key); else localStorage.setItem(key, value); } catch (e) {} }
  function load(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }

  // ── Theme ─────────────────────────────────────────────
  $all('.theme-btn').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var next = root.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
      root.setAttribute('data-theme', next);
      store('theme', next);
    });
  });

  // ── Sidebar ───────────────────────────────────────────
  var currentTopic = NAV.filter(function (t) { return t.page === page; })[0] || NAV[0];
  var sidebar = $('#sidebar');

  if (sidebar) {
    var html = '<ul class="topics">';
    NAV.forEach(function (t) {
      var cls = t.macOnly ? ' class="mac-only" style="display:none"' : '';
      var current = t === currentTopic ? ' aria-current="page"' : '';
      var ext = t.external ? ' target="_blank" rel="noopener"' : '';
      var href = t.external ? t.external : (t.page === page ? '#' : t.page + '.html');
      html += '<li' + cls + '><a href="' + href + '"' + current + ext + '>' +
        '<span class="topic-icon"><i class="bi ' + t.icon + '"></i></span>' + esc(t.title) +
        (t.external ? '<i class="bi bi-box-arrow-up-right ext"></i>' : '') + '</a></li>';
    });
    html += '</ul>';
    html += '<label class="sidebar-filter"><i class="bi bi-funnel"></i>' +
      '<input type="search" placeholder="Filter by title…" aria-label="Filter navigation by title"></label>';
    html += '<nav aria-label="' + esc(currentTopic.title) + '">';
    (currentTopic.groups || []).forEach(function (g) {
      html += '<details class="nav-group" open><summary>' + esc(g.title) + '<i class="bi bi-chevron-right"></i></summary><ul class="nav-items">';
      g.items.forEach(function (it) {
        var cls = it.macOnly ? ' class="mac-only" style="display:none"' : '';
        html += '<li' + cls + '><a href="' + hrefFor(it) + '" data-page="' + it.page + '" data-hash="' + (it.hash || '') + '">' +
          esc(it.title) + (it.badge ? ' <span class="badge ' + (it.badgeKind || 'tip') + '">' + esc(it.badge) + '</span>' : '') + '</a></li>';
      });
      html += '</ul></details>';
    });
    html += '<p class="nav-empty" hidden>No matching pages.</p></nav>';
    sidebar.innerHTML = html;

    // Filter by title
    var filter = $('.sidebar-filter input', sidebar);
    filter.addEventListener('input', function () {
      var q = filter.value.trim().toLowerCase();
      var any = false;
      $all('.nav-group', sidebar).forEach(function (g) {
        var groupHit = g.querySelector('summary').textContent.toLowerCase().indexOf(q) !== -1;
        var shown = 0;
        $all('.nav-items li', g).forEach(function (li) {
          if (li.classList.contains('mac-only') && !isMac) return;
          var hit = !q || groupHit || li.textContent.toLowerCase().indexOf(q) !== -1;
          li.hidden = !hit;
          if (hit) shown++;
        });
        g.hidden = shown === 0;
        if (q && shown) g.open = true;
        if (shown) any = true;
      });
      $('.nav-empty', sidebar).hidden = any;
    });
  }

  // Mobile drawer + desktop collapse
  var menuBtn = $('.menu-btn');
  if (menuBtn) {
    menuBtn.addEventListener('click', function () {
      var open = !root.hasAttribute('data-sidebar-open');
      root.toggleAttribute('data-sidebar-open', open);
      menuBtn.setAttribute('aria-expanded', String(open));
    });
  }
  var backdrop = $('.sidebar-backdrop');
  if (backdrop) backdrop.addEventListener('click', function () { root.removeAttribute('data-sidebar-open'); });
  if (sidebar) sidebar.addEventListener('click', function (e) {
    if (e.target.closest('a')) root.removeAttribute('data-sidebar-open');
  });
  var collapseBtn = $('.sidebar-toggle');
  if (collapseBtn) {
    collapseBtn.addEventListener('click', function () {
      var collapsed = !root.hasAttribute('data-sidebar-collapsed');
      root.toggleAttribute('data-sidebar-collapsed', collapsed);
      store('sidebar-collapsed', collapsed ? '1' : null);
    });
  }

  // ── Platform gating (Homebrew is macOS-only) ──────────
  if (isMac) $all('.mac-only').forEach(function (el) { el.style.display = ''; });

  // ── Content enhancements ──────────────────────────────
  var content = $('.sl-content');

  function slugify(text) {
    return text.toLowerCase().replace(/[^\w\s-]/g, '').trim().replace(/\s+/g, '-').replace(/-+/g, '-');
  }

  // Headings → ids, anchors, and TOC entries
  var tocEntries = [];
  if (content) {
    $all('h2, h3', content).forEach(function (h) {
      if (h.closest('.hero-card, .callout, .card, .spotlight, .no-toc')) return;
      var step = h.closest('.step');
      var target = h;
      if (step && step.id && h.parentElement.classList.contains('step-header')) target = step;
      if (!target.id) {
        var base = slugify(h.textContent) || 'section';
        var id = base, n = 2;
        while (document.getElementById(id)) id = base + '-' + n++;
        target.id = id;
      }
      var a = document.createElement('a');
      a.className = 'anchor';
      a.href = '#' + target.id;
      a.setAttribute('aria-label', 'Link to this section');
      a.innerHTML = '<i class="bi bi-link-45deg"></i>';
      h.appendChild(a);
      var label = h.getAttribute('data-toc') || h.textContent.trim();
      tocEntries.push({ id: target.id, title: label, depth: h.tagName === 'H3' ? 3 : 2, el: target });
    });
  }

  function tocHtml() {
    return tocEntries.map(function (e) {
      return '<li><a href="#' + e.id + '" class="depth-' + e.depth + '">' + esc(e.title) + '</a></li>';
    }).join('');
  }
  var tocList = $('.sl-toc .toc-list');
  if (tocList) tocList.innerHTML = '<li><a href="#_top" class="depth-2" data-top>Overview</a></li>' + tocHtml();

  var mobileToc = $('.mobile-toc');
  if (mobileToc && tocEntries.length) {
    mobileToc.hidden = false;
    $('.toc-list', mobileToc).innerHTML = tocHtml();
    mobileToc.addEventListener('click', function (e) { if (e.target.closest('a')) mobileToc.open = false; });
  }
  $all('a[href="#_top"]').forEach(function (a) {
    a.addEventListener('click', function (e) { e.preventDefault(); window.scrollTo({ top: 0, behavior: 'smooth' }); history.replaceState(null, '', location.pathname); });
  });

  // Wrap tables for horizontal scroll + rounded border
  if (content) $all('table', content).forEach(function (t) {
    if (t.parentElement.classList.contains('table-wrap')) return;
    var w = document.createElement('div');
    w.className = 'table-wrap';
    t.parentNode.insertBefore(w, t);
    w.appendChild(t);
  });

  // Code frames + copy buttons
  $all('pre').forEach(function (pre) {
    var frame = pre.closest('.code-frame');
    if (!frame) {
      frame = document.createElement('div');
      frame.className = 'code-frame';
      pre.parentNode.insertBefore(frame, pre);
      frame.appendChild(pre);
    }
    if (frame.hasAttribute('data-nocopy')) return;
    var btn = document.createElement('button');
    btn.className = 'copy-btn';
    btn.type = 'button';
    btn.title = 'Copy to clipboard';
    btn.setAttribute('aria-label', 'Copy to clipboard');
    btn.innerHTML = '<i class="bi bi-copy"></i>';
    btn.addEventListener('click', function () {
      var text = pre.innerText.replace(/\n+$/, '');
      var done = function () {
        btn.classList.add('copied');
        btn.innerHTML = '<i class="bi bi-check2"></i>';
        setTimeout(function () { btn.classList.remove('copied'); btn.innerHTML = '<i class="bi bi-copy"></i>'; }, 1500);
      };
      if (navigator.clipboard) navigator.clipboard.writeText(text).then(done); else done();
    });
    frame.appendChild(btn);
  });

  // Tabs (synced by data-sync key; defaults to the visitor's OS when tabs declare data-os)
  function selectTab(tabs, idx) {
    var buttons = $all('.tab-list [role="tab"]', tabs);
    var panels = $all(':scope > .tab-panel', tabs);
    buttons.forEach(function (b, i) {
      var on = i === idx;
      b.setAttribute('aria-selected', String(on));
      b.tabIndex = on ? 0 : -1;
      if (panels[i]) panels[i].hidden = !on;
    });
  }
  $all('.tabs').forEach(function (tabs) {
    var buttons = $all('.tab-list [role="tab"]', tabs);
    var key = tabs.getAttribute('data-sync');
    var saved = key && load('tabs:' + key);
    var idx = 0;
    buttons.forEach(function (b, i) {
      if (saved ? b.textContent.trim() === saved : (b.getAttribute('data-os') === 'windows' && isWindows)) idx = i;
    });
    selectTab(tabs, idx);
    buttons.forEach(function (b, i) {
      b.addEventListener('click', function () {
        if (key) {
          store('tabs:' + key, b.textContent.trim());
          $all('.tabs[data-sync="' + key + '"]').forEach(function (other) {
            $all('.tab-list [role="tab"]', other).forEach(function (ob, j) {
              if (ob.textContent.trim() === b.textContent.trim()) selectTab(other, j);
            });
          });
        } else {
          selectTab(tabs, i);
        }
      });
      b.addEventListener('keydown', function (e) {
        var d = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0;
        if (!d) return;
        var n = (i + d + buttons.length) % buttons.length;
        buttons[n].click();
        buttons[n].focus();
      });
    });
  });

  // Copy-link page action
  $all('[data-copy-link]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var label = btn.innerHTML;
      var done = function () { btn.innerHTML = '<i class="bi bi-check2"></i> Link copied'; setTimeout(function () { btn.innerHTML = label; }, 1500); };
      if (navigator.clipboard) navigator.clipboard.writeText(location.href.split('#')[0]).then(done); else done();
    });
  });

  // ── Scrollspy ─────────────────────────────────────────
  var sideLinks = $all('.nav-items a[data-page="' + page + '"]');
  var tocLinks = $all('.toc-list a');
  var steps = $all('.step[id]');
  var mtCurrent = $('.mobile-toc .mt-current');

  var spyIds = {};
  tocEntries.forEach(function (e) { spyIds[e.id] = e.el; });
  sideLinks.forEach(function (a) {
    var h = a.getAttribute('data-hash');
    if (h && document.getElementById(h)) spyIds[h] = document.getElementById(h);
  });
  var spyTargets = Object.keys(spyIds).map(function (id) { return spyIds[id]; })
    .sort(function (a, b) { return a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1; });

  var lastSpy = null;
  function spy() {
    var line = parseFloat(getComputedStyle(root).scrollPaddingTop) || 100;
    var current = null;
    for (var i = 0; i < spyTargets.length; i++) {
      if (spyTargets[i].getBoundingClientRect().top - line <= 8) current = spyTargets[i];
      else break;
    }
    if (!current && spyTargets.length) current = spyTargets[0];
    if ((window.innerHeight + window.scrollY) >= document.documentElement.scrollHeight - 4 && spyTargets.length) {
      current = spyTargets[spyTargets.length - 1];
    }
    var id = current ? current.id : null;
    if (id === lastSpy) return;
    lastSpy = id;

    // Deepest sidebar entry that contains the current position
    var sideId = null;
    if (current) {
      for (var j = spyTargets.indexOf(current); j >= 0; j--) {
        var cand = spyTargets[j].id;
        if (sideLinks.some(function (a) { return a.getAttribute('data-hash') === cand; })) { sideId = cand; break; }
      }
    }
    sideLinks.forEach(function (a) {
      var h = a.getAttribute('data-hash');
      var on = h ? h === sideId : !sideId && !id;
      if (!h && !sideLinks.some(function (x) { return x.getAttribute('data-hash'); })) on = true;
      if (on) a.setAttribute('aria-current', 'true'); else a.removeAttribute('aria-current');
    });

    var tocId = null;
    if (current) {
      for (var k = spyTargets.indexOf(current); k >= 0; k--) {
        if (tocEntries.some(function (e) { return e.id === spyTargets[k].id; })) { tocId = spyTargets[k].id; break; }
      }
    }
    tocLinks.forEach(function (a) {
      var on = tocId ? a.getAttribute('href') === '#' + tocId : a.hasAttribute('data-top');
      if (on) a.setAttribute('aria-current', 'true'); else a.removeAttribute('aria-current');
    });
    if (mtCurrent) {
      var entry = tocEntries.filter(function (e) { return e.id === tocId; })[0];
      mtCurrent.textContent = entry ? entry.title : 'Overview';
    }

    var stepEl = current && current.closest ? (current.classList.contains('step') ? current : current.closest('.step')) : null;
    steps.forEach(function (s) { s.classList.toggle('is-current', s === stepEl); });

    // Keep the active sidebar entry visible
    var active = $('.nav-items a[aria-current="true"]');
    if (active && sidebar) {
      var r = active.getBoundingClientRect(), sr = sidebar.getBoundingClientRect();
      if (r.top < sr.top + 40 || r.bottom > sr.bottom - 20) active.scrollIntoView({ block: 'nearest' });
    }
  }
  var ticking = false;
  window.addEventListener('scroll', function () {
    if (ticking) return;
    ticking = true;
    requestAnimationFrame(function () { ticking = false; spy(); });
  }, { passive: true });
  window.addEventListener('resize', spy);
  spy();

  // ── Search dialog ─────────────────────────────────────
  var dialog = $('#search-dialog');
  var searchInput = dialog && $('input', dialog);
  var results = dialog && $('.search-results', dialog);
  var index = [];

  NAV.forEach(function (t) {
    if (t.macOnly && !isMac) return;
    if (t.external) return;
    index.push({ title: t.title, path: 'Docs', href: hrefFor({ page: t.page }), icon: t.icon });
    (t.groups || []).forEach(function (g) {
      g.items.forEach(function (it) {
        if (it.macOnly && !isMac) return;
        index.push({ title: it.title, path: t.title + ' › ' + g.title, href: hrefFor(it), icon: it.hash ? 'bi-hash' : 'bi-file-earmark-text' });
      });
    });
  });
  var known = {};
  index.forEach(function (e) { known[e.href] = true; });
  tocEntries.forEach(function (e) {
    if (known['#' + e.id]) return;
    index.push({ title: e.title, path: currentTopic.title + ' › On this page', href: '#' + e.id, icon: 'bi-hash' });
  });

  var activeIdx = 0, shown = [];
  function highlight(text, q) {
    if (!q) return esc(text);
    var i = text.toLowerCase().indexOf(q);
    if (i < 0) return esc(text);
    return esc(text.slice(0, i)) + '<mark>' + esc(text.slice(i, i + q.length)) + '</mark>' + esc(text.slice(i + q.length));
  }
  function renderResults() {
    var q = searchInput.value.trim().toLowerCase();
    var words = q.split(/\s+/).filter(Boolean);
    shown = index.filter(function (e) {
      var hay = (e.title + ' ' + e.path).toLowerCase();
      return words.every(function (w) { return hay.indexOf(w) !== -1; });
    }).slice(0, 30);
    activeIdx = 0;
    if (!shown.length) { results.innerHTML = '<li class="search-empty">No results for “' + esc(searchInput.value) + '”</li>'; return; }
    results.innerHTML = shown.map(function (e, i) {
      return '<li><a href="' + e.href + '" class="' + (i === 0 ? 'active' : '') + '"><i class="bi ' + e.icon + '"></i>' +
        '<span><span class="sr-title">' + highlight(e.title, words[0]) + '</span><br><span class="sr-path">' + esc(e.path) + '</span></span></a></li>';
    }).join('');
  }
  function setActiveResult(i) {
    var links = $all('a', results);
    if (!links.length) return;
    activeIdx = (i + links.length) % links.length;
    links.forEach(function (a, j) { a.classList.toggle('active', j === activeIdx); });
    links[activeIdx].scrollIntoView({ block: 'nearest' });
  }
  function openSearch() {
    if (!dialog) return;
    dialog.hidden = false;
    searchInput.value = '';
    renderResults();
    setTimeout(function () { searchInput.focus(); }, 0);
    document.body.style.overflow = 'hidden';
  }
  function closeSearch() {
    if (!dialog) return;
    dialog.hidden = true;
    document.body.style.overflow = '';
  }
  if (dialog) {
    $all('.search-btn').forEach(function (b) { b.addEventListener('click', openSearch); });
    searchInput.addEventListener('input', renderResults);
    searchInput.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') { e.preventDefault(); setActiveResult(activeIdx + 1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); setActiveResult(activeIdx - 1); }
      else if (e.key === 'Enter') { var a = $all('a', results)[activeIdx]; if (a) { e.preventDefault(); a.click(); } }
    });
    results.addEventListener('click', function (e) { if (e.target.closest('a')) closeSearch(); });
    dialog.addEventListener('click', function (e) { if (e.target === dialog) closeSearch(); });
    document.addEventListener('keydown', function (e) {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); dialog.hidden ? openSearch() : closeSearch(); }
      else if (e.key === '/' && dialog.hidden && !/input|textarea/i.test((document.activeElement || {}).tagName || '')) { e.preventDefault(); openSearch(); }
      else if (e.key === 'Escape' && !dialog.hidden) closeSearch();
    });
    var kbdHint = $('.search-btn kbd');
    if (kbdHint && !isMac) kbdHint.textContent = 'Ctrl K';
  }

  // ── "New release" announcement banner ─────────────────
  var banner = $('.sl-banner');
  if (banner && window.fetch && location.protocol !== 'file:') {
    var cached = null;
    try { cached = JSON.parse(sessionStorage.getItem('dsm-latest-release') || 'null'); } catch (e) {}
    var show = function (rel) {
      if (!rel || !rel.tag || load('banner-dismissed') === rel.tag) return;
      $('.banner-version', banner).textContent = 'dsm ' + rel.tag;
      $('.banner-link', banner).href = rel.url;
      banner.hidden = false;
      $('.banner-close', banner).addEventListener('click', function () {
        banner.hidden = true;
        store('banner-dismissed', rel.tag);
      });
    };
    if (cached) show(cached);
    else fetch('https://api.github.com/repos/' + REPO + '/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (j) {
        if (!j || !j.tag_name) return;
        var rel = { tag: j.tag_name, url: j.html_url };
        try { sessionStorage.setItem('dsm-latest-release', JSON.stringify(rel)); } catch (e) {}
        show(rel);
      })
      .catch(function () {});
  }
})();
