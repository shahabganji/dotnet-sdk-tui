/*
  dsm docs buddy — Stax, the animated SDK-stack mascot at the bottom-right of the docs pages.
  Loaded by site.js on docs pages only (never on the landing page). Styles live in site.css (.dsm-buddy).
  Stax waves now and then, follows the pointer with its eyes and leans towards it; click it for a tip.
*/
(function () {
  'use strict';

  var FONT = "font-family=\"'Segoe UI','Helvetica Neue',Arial,sans-serif\"";
  var INK = '#062E29';
  var LIME = '#C8E64D';
  var LIMB = '#0E6F61';

  // ── Shared parts ───────────────────────────────────────
  function defs(u) {
    return '<defs>' +
      '<linearGradient id="' + u + 'g0" x1="1" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#0E6B5E"/><stop offset="1" stop-color="#0B4F47"/></linearGradient>' +
      '<linearGradient id="' + u + 'g1" x1="1" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#139A85"/><stop offset="1" stop-color="#0E6F61"/></linearGradient>' +
      '<linearGradient id="' + u + 'g2" x1="1" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2FC7A9"/><stop offset="1" stop-color="#17A58E"/></linearGradient>' +
      '<linearGradient id="' + u + 'g3" x1="1" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#C8E64D"/><stop offset="1" stop-color="#7CC94D"/></linearGradient>' +
      '<linearGradient id="' + u + 'g4" x1="1" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F3FBD8"/><stop offset=".55" stop-color="#D9F09A"/><stop offset="1" stop-color="#B5DC6A"/></linearGradient>' +
      '<linearGradient id="' + u + 'limb" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#17A58E"/><stop offset="1" stop-color="#0B5E52"/></linearGradient>' +
      '<filter id="' + u + 'blur" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="4"/></filter>' +
      '</defs>';
  }

  function shadow(u, cx, cy, rx) {
    return '<g class="bd-shadow"><ellipse cx="' + cx + '" cy="' + cy + '" rx="' + rx + '" ry="' + (rx / 6) +
      '" fill="#000" opacity=".28" filter="url(#' + u + 'blur)"/></g>';
  }

  // Three-finger claw hand at (x, y), pointing along `angle` (0 = straight down).
  function claw(x, y, angle) {
    var f = '';
    [-42, 0, 42].forEach(function (a) {
      f += '<rect x="-3.2" y="3" width="6.4" height="12" rx="3.2" fill="' + LIME + '" stroke="' + INK + '" stroke-width="2.2" transform="rotate(' + a + ')"/>';
    });
    return '<g transform="translate(' + x + ' ' + y + ') rotate(' + angle + ')">' + f +
      '<circle r="8" fill="' + LIME + '" stroke="' + INK + '" stroke-width="2.6"/></g>';
  }

  // Segmented arm from shoulder (sx, sy) to an elbow and a wrist, finished with a claw.
  function arm(cls, sx, sy, ex, ey, wx, wy, angle, u) {
    var seg = function (x1, y1, x2, y2) {
      return '<path d="M' + x1 + ' ' + y1 + 'L' + x2 + ' ' + y2 + '" stroke="' + INK + '" stroke-width="13" stroke-linecap="round"/>' +
        '<path d="M' + x1 + ' ' + y1 + 'L' + x2 + ' ' + y2 + '" stroke="url(#' + u + 'limb)" stroke-width="8" stroke-linecap="round"/>';
    };
    return '<g class="bd-arm ' + cls + '" style="transform-origin:' + sx + 'px ' + sy + 'px">' + seg(sx, sy, ex, ey) + seg(ex, ey, wx, wy) +
      '<circle cx="' + ex + '" cy="' + ey + '" r="5.4" fill="' + LIME + '" stroke="' + INK + '" stroke-width="2"/>' +
      claw(wx, wy, angle) + '</g>';
  }

  // Chunky robot leg: thigh, knee, a shin that widens downwards and a boot with a lime toe cap.
  // (x, top) is the hip, `bottom` the sole, `dir` -1/1 turns the toe outwards.
  function leg(cls, x, top, bottom, dir) {
    var knee = top + (bottom - top) * 0.36, sole = bottom, ankle = bottom - 13, toe = x + dir * 4;
    return '<g class="bd-leg ' + cls + '">' +
      '<rect x="' + (x - 7.5) + '" y="' + top + '" width="15" height="' + (knee - top + 4) + '" rx="6" fill="' + LIMB + '" stroke="' + INK + '" stroke-width="2.6"/>' +
      '<path d="M' + (x - 11) + ' ' + knee + 'h22l4 ' + (ankle - knee) + 'h-30z" fill="#17A58E" stroke="' + INK + '" stroke-width="2.6" stroke-linejoin="round"/>' +
      '<path d="M' + (x - 6) + ' ' + (knee + 5) + 'l-2 ' + (ankle - knee - 9) + '" stroke="#7DEBD3" stroke-width="3" stroke-linecap="round" opacity=".7"/>' +
      '<circle cx="' + x + '" cy="' + knee + '" r="6" fill="' + LIME + '" stroke="' + INK + '" stroke-width="2.2"/>' +
      '<path d="M' + (toe - 21) + ' ' + sole + 'q-2 -16 12 -17h18q13 1 12 17z" fill="' + LIMB + '" stroke="' + INK + '" stroke-width="2.6" stroke-linejoin="round"/>' +
      '<path d="M' + (toe + dir * 5 - 10) + ' ' + sole + 'q0 -11 10 -11q10 0 10 11z" fill="' + LIME + '" stroke="' + INK + '" stroke-width="2.2"/>' +
      '</g>';
  }

  function eyes(x1, x2, y, s) {
    var e = function (x) {
      return '<ellipse cx="' + x + '" cy="' + y + '" rx="' + 5.6 * s + '" ry="' + 8.4 * s + '" fill="#062822"/>' +
        '<circle cx="' + (x + 2 * s) + '" cy="' + (y - 3.6 * s) + '" r="' + 2 * s + '" fill="#fff"/>';
    };
    return '<g class="bd-eyes" style="transform-origin:100px ' + y + 'px"><g class="bd-pupils">' + e(x1) + e(x2) + '</g></g>';
  }

  // ── Stax: the SDK tile stack, brought to life ─────────
  var EDGES = ['#062E29', '#094A41', '#0F7465', '#5E9A2F', '#A9E8D8'];
  function tiles(u) {
    var faces = ['g0', 'g1', 'g2', 'g3', 'g4'], s = '';
    var T = function (y) { return 'translate(0 ' + y + ') scale(1 .545) rotate(-45)'; };
    var tile = function (fill, y) { return '<rect x="-55" y="-55" width="110" height="110" rx="24" fill="' + fill + '" transform="' + T(y) + '"/>'; };
    for (var i = 4; i >= 0; i--) {
      var y = 15 * i, k = 4 - i;
      s += tile(EDGES[k], y + 9) + '<rect x="-67.8" y="' + y + '" width="135.6" height="9" fill="' + EDGES[k] + '"/>';
      if (i === 0) {
        s += '<g transform="' + T(0) + '"><rect x="-55" y="-55" width="110" height="110" rx="24" fill="url(#' + u + faces[k] + ')"/>' +
          '<rect x="-46" y="-46" width="92" height="92" rx="17" fill="none" stroke="#fff" stroke-opacity=".75" stroke-width="3"/>' +
          '<text x="0" y="14" text-anchor="middle" ' + FONT + ' font-size="40" font-weight="800" letter-spacing="-1" fill="#0B3B34">dsm</text></g>';
      } else s += tile('url(#' + u + faces[k] + ')', y);
    }
    return s;
  }

  function stax(u) {
    return defs(u) + shadow(u, 100, 192, 50) +
      '<g class="bd-figure"><g class="bd-legs">' + leg('bd-leg-l', 80, 118, 190, -1) + leg('bd-leg-r', 120, 118, 190, 1) + '</g>' +
      '<g class="bd-body">' +
      arm('bd-arm-l', 54, 92, 36, 114, 30, 138, 14, u) +
      arm('bd-arm-r', 146, 92, 164, 114, 170, 138, -14, u) +
      '<g transform="translate(100 50) scale(.76)">' + tiles(u) +
      '<rect x="-44" y="40" width="88" height="34" rx="17" fill="#EAFBF5" stroke="' + INK + '" stroke-width="3.5"/></g>' +
      eyes(84.8, 115.2, 93, 1.05) +
      '</g></g>';
  }

  var NAME = 'Stax';
  var HELLO = "Hi! I'm Stax 👋";

  var TIPS = [
    'Press <kbd>F6</kbd> in dsm to switch the theme.',
    'Press <kbd>/</kbd> to live-search the SDK catalog.',
    'Select a version and press <kbd>i</kbd> to install it.',
    'Press <kbd>Shift</kbd>+<kbd>M</kbd> to migrate all unmanaged SDKs.',
    'Press <kbd>q</kbd> to quit dsm.',
    'Search these docs with <kbd>⌘ K</kbd>.',
  ];

  var uidSeq = 0;
  function svg() {
    var u = 'bd' + (++uidSeq) + '-';
    return '<svg class="bd-svg" viewBox="0 0 200 200" aria-hidden="true">' + stax(u) + '</svg>';
  }

  // Eyes follow the pointer and the whole figure leans slightly towards it.
  function follow(el) {
    var pupils = el.querySelector('.bd-pupils'), figure = el.querySelector('.bd-figure');
    var still = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;
    window.addEventListener('pointermove', function (e) {
      var r = el.getBoundingClientRect();
      var dx = e.clientX - (r.left + r.width / 2), dy = e.clientY - (r.top + r.height * 0.4);
      var d = Math.max(1, Math.hypot(dx, dy));
      pupils.style.transform = 'translate(' + (dx / d * 3.5).toFixed(2) + 'px,' + (dy / d * 2.5).toFixed(2) + 'px)';
      if (!still) figure.style.transform = 'rotate(' + (dx / d * Math.min(1, d / 400) * 4).toFixed(2) + 'deg)';
    }, { passive: true });
  }

  /**
   * Builds the buddy element. opts.inline renders it in normal flow (for galleries) instead of fixed.
   */
  function create(opts) {
    opts = opts || {};
    var el = document.createElement('div');
    el.className = 'dsm-buddy' + (opts.inline ? ' is-inline' : '');
    el.innerHTML =
      '<div class="dsm-buddy-bubble"><span class="bd-text" role="status" aria-live="polite"></span>' +
      '<button type="button" class="dsm-buddy-hush" aria-label="Hide hint" tabindex="-1">×</button></div>' +
      '<button type="button" class="dsm-buddy-figure" aria-label="' + NAME + ', the dsm mascot. Click for a tip.">' + svg() + '</button>';

    var bubble = el.querySelector('.dsm-buddy-bubble'), text = bubble.querySelector('.bd-text'), hush = bubble.querySelector('.dsm-buddy-hush');
    var timer = 0, left = 0, tip = Math.floor(Math.random() * TIPS.length);
    var hide = function () {
      clearTimeout(timer);
      el.classList.remove('is-talking');
      hush.tabIndex = -1;
    };
    var arm = function (ms) { clearTimeout(timer); left = ms; timer = setTimeout(hide, ms); };
    var say = function (html, ms) {
      text.innerHTML = html;
      el.classList.add('is-talking');
      hush.tabIndex = 0;
      arm(ms || 5000);
    };
    // The hint stays put while it's being read, and can be dismissed with ×, Esc or a click elsewhere.
    bubble.addEventListener('pointerenter', function () { clearTimeout(timer); });
    bubble.addEventListener('pointerleave', function () { if (el.classList.contains('is-talking')) arm(Math.max(left, 1500)); });
    hush.addEventListener('click', hide);
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && el.classList.contains('is-talking')) hide(); });
    document.addEventListener('pointerdown', function (e) { if (!el.contains(e.target)) hide(); });
    el.querySelector('.dsm-buddy-figure').addEventListener('click', function () {
      tip = (tip + 1) % TIPS.length;
      say(TIPS[tip]);
      el.classList.remove('is-excited');
      void el.offsetWidth;
      el.classList.add('is-excited');
      setTimeout(function () { el.classList.remove('is-excited'); }, 650);
    });
    follow(el);
    el.dsmSay = say;
    if (opts.greet) setTimeout(function () { say(HELLO, 3500); }, 900);
    return el;
  }

  // Shows Stax; greets once per session.
  function mount() {
    var get = function (k) { try { return sessionStorage.getItem(k); } catch (e) { return null; } };
    document.body.appendChild(create({ greet: get('dsm-buddy-greeted') !== '1' }));
    try { sessionStorage.setItem('dsm-buddy-greeted', '1'); } catch (e) { }
  }

  window.dsmBuddy = { create: create, svg: svg, mount: mount };
  if (document.currentScript && document.currentScript.hasAttribute('data-mount')) mount();
})();
