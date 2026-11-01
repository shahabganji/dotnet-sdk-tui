/*
  .NET SDK Manager — landing page behaviours (index.html only).
  Runs after site.js, which already wires the theme button, search, tabs,
  copy buttons, .mac-only reveal and the release banner.
*/
(function () {
  'use strict';

  function $(sel, ctx) { return (ctx || document).querySelector(sel); }
  function $all(sel, ctx) { return Array.prototype.slice.call((ctx || document).querySelectorAll(sel)); }
  function visible(el) { return el.style.display !== 'none'; }

  // Old links like index.html#install / #usage pointed at the docs home, which now lives in docs.html.
  var hash = decodeURIComponent(location.hash.slice(1));
  if (hash && !document.getElementById(hash)) {
    location.replace('docs.html' + location.hash);
    return;
  }

  var root = document.documentElement;
  var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ── Scroll-driven effects: header auto-hide, hero fade, back-to-top ──
  var header = $('.lp-header');
  var heroCopy = $('.hero-copy');
  var toTop = $('.to-top');
  var lastY = window.scrollY;
  var ticking = false;

  function onScroll() {
    var y = window.scrollY;

    if (header) {
      var goingDown = y > lastY;
      if (y < 120 || !goingDown) header.classList.remove('is-hidden');
      else if (y - lastY > 4) header.classList.add('is-hidden');
      if (header.contains(document.activeElement)) header.classList.remove('is-hidden');
    }

    if (heroCopy && !reduceMotion) {
      // ease out over the distance it takes the copy to leave the viewport
      var range = Math.max(heroCopy.offsetTop + heroCopy.offsetHeight, 360);
      var p = Math.min(Math.max(y / range, 0), 1);
      heroCopy.style.opacity = String(1 - Math.pow(p, 1.5));
      heroCopy.style.transform = 'translateY(' + (-p * 40).toFixed(1) + 'px) scale(' + (1 - p * 0.05).toFixed(3) + ')';
      heroCopy.style.filter = p > 0.05 ? 'blur(' + (p * 5).toFixed(1) + 'px)' : '';
      heroCopy.style.visibility = p >= 1 ? 'hidden' : '';
    }

    if (toTop) toTop.hidden = y < 700;

    lastY = y;
    ticking = false;
  }
  window.addEventListener('scroll', function () {
    if (!ticking) { ticking = true; requestAnimationFrame(onScroll); }
  }, { passive: true });
  onScroll();

  if (toTop) toTop.addEventListener('click', function () {
    window.scrollTo({ top: 0, behavior: reduceMotion ? 'auto' : 'smooth' });
  });

  // ── Live preview carousel ─────────────────────────────
  var win = $('[data-carousel]');
  if (win) {
    var tabs = $all('.win-tabs [role="tab"]', win);
    var slides = $all('.win-slide', win);
    var captions = $all('p', $('.win-captions', win).content);
    var caption = $('.win-caption', win);
    var bar = $('.win-progress span', win);
    var pauseBtn = $('.win-pause', win);
    var DURATION = 6000;
    var current = 0, start = 0, elapsed = 0, paused = reduceMotion, hovering = false, inView = true;

    var show = function (i) {
      current = i;
      tabs.forEach(function (t, j) {
        t.setAttribute('aria-selected', String(j === i));
        t.tabIndex = j === i ? 0 : -1;
      });
      slides.forEach(function (s, j) { s.classList.toggle('is-active', j === i); });
      if (captions[i]) caption.textContent = captions[i].textContent;
      elapsed = 0;
      start = performance.now();
    };
    var next = function () {
      var n = current;
      do { n = (n + 1) % tabs.length; } while (!visible(tabs[n]) && n !== current);
      show(n);
    };
    var setPaused = function (p) {
      paused = p;
      win.classList.toggle('is-paused', p);
      pauseBtn.setAttribute('aria-label', p ? 'Play slideshow' : 'Pause slideshow');
      pauseBtn.title = p ? 'Play' : 'Pause';
      start = performance.now() - elapsed;
    };
    var frame = function (now) {
      if (paused || hovering || !inView || document.hidden) {
        start = now - elapsed;
      } else {
        elapsed = now - start;
        if (elapsed >= DURATION) next();
      }
      bar.style.width = (Math.min(elapsed / DURATION, 1) * 100).toFixed(2) + '%';
      requestAnimationFrame(frame);
    };

    tabs.forEach(function (t, i) {
      t.addEventListener('click', function () { show(i); });
      t.addEventListener('keydown', function (e) {
        var d = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0;
        if (!d) return;
        var n = i;
        do { n = (n + d + tabs.length) % tabs.length; } while (!visible(tabs[n]));
        tabs[n].focus();
        show(n);
      });
    });
    pauseBtn.addEventListener('click', function () { setPaused(!paused); });
    $('.win-stage', win).addEventListener('mouseenter', function () { hovering = true; });
    $('.win-stage', win).addEventListener('mouseleave', function () { hovering = false; });
    if ('IntersectionObserver' in window) {
      new IntersectionObserver(function (es) { inView = es[0].isIntersecting; }).observe(win);
    }
    show(0);
    setPaused(paused);
    requestAnimationFrame(frame);
  }

  // ── Keycap marquee: duplicate each track so translateX(-50%) loops seamlessly ──
  $all('.key-track').forEach(function (track) {
    $all(':scope > *', track).forEach(function (el) {
      var clone = el.cloneNode(true);
      clone.setAttribute('aria-hidden', 'true');
      track.appendChild(clone);
    });
  });

  // ── Typing terminal ───────────────────────────────────
  var term = $('[data-typing]');
  function runTerminal() {
    var cmd = $all('.t-type', term);
    var out = $all('.term-out > div', term);
    var last = $('.term-last', term);
    var timers = [];
    var later = function (fn, ms) { timers.push(setTimeout(fn, ms)); };

    var type = function (el, done) {
      var text = el.getAttribute('data-text');
      var i = 0;
      var step = function () {
        el.textContent = text.slice(0, ++i);
        if (i < text.length) later(step, text.length > 40 ? 14 : 90);
        else later(done, 350);
      };
      step();
    };
    var cycle = function () {
      cmd.forEach(function (el) { el.textContent = ''; });
      out.forEach(function (el) { el.classList.remove('shown'); });
      last.classList.remove('shown');
      term.classList.add('is-typing');
      type(cmd[0], function () {
        out.forEach(function (el, i) { later(function () { el.classList.add('shown'); }, 120 + i * 260 + (i > 6 ? 500 : 0)); });
        later(function () {
          last.classList.add('shown');
          type(cmd[1], function () { later(cycle, 6000); });
        }, 120 + out.length * 260 + 900);
      });
    };
    cycle();
  }
  if (term) {
    if (reduceMotion || !('IntersectionObserver' in window)) {
      $all('.t-type', term).forEach(function (el) { el.textContent = el.getAttribute('data-text'); });
    } else {
      $all('.t-type', term).forEach(function (el) { el.textContent = ''; });
      term.classList.add('is-typing');
      var termIO = new IntersectionObserver(function (es) {
        if (!es[0].isIntersecting) return;
        termIO.disconnect();
        runTerminal();
      }, { threshold: 0.35 });
      termIO.observe(term);
    }
  }

  // ── Reveal on scroll ──────────────────────────────────
  var reveals = $all('.reveal');
  if (reduceMotion || !('IntersectionObserver' in window)) {
    reveals.forEach(function (el) { el.classList.add('is-visible'); });
  } else {
    var io = new IntersectionObserver(function (es) {
      es.forEach(function (e) {
        if (!e.isIntersecting) return;
        e.target.classList.add('is-visible');
        io.unobserve(e.target);
      });
    }, { threshold: 0.12, rootMargin: '0px 0px -8% 0px' });
    reveals.forEach(function (el) { io.observe(el); });
  }

  // ── Footer theme switch (kept in sync with the header toggle) ──
  var seg = $all('[data-theme-set]');
  function syncSeg() {
    var cur = root.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
    seg.forEach(function (b) { b.setAttribute('aria-checked', String(b.getAttribute('data-theme-set') === cur)); });
  }
  seg.forEach(function (b) {
    b.addEventListener('click', function () {
      var v = b.getAttribute('data-theme-set');
      root.setAttribute('data-theme', v);
      try { localStorage.setItem('theme', v); } catch (e) {}
    });
  });
  new MutationObserver(syncSeg).observe(root, { attributes: true, attributeFilter: ['data-theme'] });
  syncSeg();
})();
