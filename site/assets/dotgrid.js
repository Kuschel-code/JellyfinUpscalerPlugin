/* ====================================================================
   dotgrid.js — cursor-reactive dot field (the signature background)
   --------------------------------------------------------------------
   A grid of dots that drift toward the pointer with a distance falloff
   ("follow the cursor, to a degree"), then ease back to their home
   position. Dots near the pointer brighten to electric blue and a soft
   blue spotlight tints the canvas — the "blau stich".

   - Self-contained: creates its own <canvas.dotfield> if absent.
   - rAF loop sleeps when the field is settled + pointer idle (battery).
   - Touch: follows the finger while dragging.
   - No pointer / coarse pointer: gentle autonomous drift so it still
     breathes instead of sitting dead.
   - prefers-reduced-motion: a single static render, no loop.
   Loaded site-wide via nav.js (mirrors the support-bot.js injection).
   ==================================================================== */
(function () {
  if (window.__dotfieldInit) return;          // guard against double-inject
  window.__dotfieldInit = true;

  var ACCENT = [59, 109, 255];                 // #3b6dff
  var BASE   = [120, 140, 190];                // resting dot colour (cool grey-blue)

  var prefersReduced = window.matchMedia &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var coarse = window.matchMedia &&
    window.matchMedia('(hover: none), (pointer: coarse)').matches;

  // --- canvas -------------------------------------------------------
  var canvas = document.querySelector('canvas.dotfield');
  if (!canvas) {
    canvas = document.createElement('canvas');
    canvas.className = 'dotfield';
    canvas.setAttribute('aria-hidden', 'true');
    if (document.body.firstChild) document.body.insertBefore(canvas, document.body.firstChild);
    else document.body.appendChild(canvas);
  }
  var ctx = canvas.getContext('2d');

  var dpr = 1, W = 0, H = 0;
  var dots = [];
  var spacing, radiusBase, influence, maxPull;

  // pointer state (in CSS px, canvas-local)
  var px = -9999, py = -9999, pActive = false;
  var lastMove = 0;

  function lerp(a, b, t) { return a + (b - a) * t; }

  function build() {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    W = canvas.clientWidth;
    H = canvas.clientHeight;
    canvas.width  = Math.round(W * dpr);
    canvas.height = Math.round(H * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

    // density + reach scale down on small / touch screens (perf + feel)
    var small = W < 760;
    spacing    = small ? 50 : 42;
    radiusBase = small ? 1.5 : 1.7;
    influence  = small ? 150 : 200;   // px radius of the cursor's pull
    maxPull    = small ? 26  : 34;    // px — how far a dot may travel ("to a degree")

    dots = [];
    var cols = Math.ceil(W / spacing) + 1;
    var rows = Math.ceil(H / spacing) + 1;
    var offX = (W - (cols - 1) * spacing) / 2;
    var offY = (H - (rows - 1) * spacing) / 2;
    for (var r = 0; r < rows; r++) {
      for (var c = 0; c < cols; c++) {
        var hx = offX + c * spacing;
        var hy = offY + r * spacing;
        dots.push({ hx: hx, hy: hy, x: hx, y: hy });
      }
    }
  }

  function paintSpotlight(cx, cy, strength) {
    if (strength <= 0) return;
    var rad = influence * 1.5;
    var g = ctx.createRadialGradient(cx, cy, 0, cx, cy, rad);
    g.addColorStop(0,   'rgba(59,109,255,' + (0.12 * strength).toFixed(3) + ')');
    g.addColorStop(0.5, 'rgba(59,109,255,' + (0.05 * strength).toFixed(3) + ')');
    g.addColorStop(1,   'rgba(59,109,255,0)');
    ctx.fillStyle = g;
    ctx.fillRect(cx - rad, cy - rad, rad * 2, rad * 2);
  }

  var settledFrames = 0;
  var idlePhase = 0;

  function frame(now) {
    ctx.clearRect(0, 0, W, H);

    // Idle / touch ambient: if the pointer hasn't moved (or there is none),
    // glide a soft virtual light around so the field keeps breathing.
    var idle = !pActive || (now - lastMove > 2600);
    var tx = px, ty = py, glow = pActive ? 1 : 0;
    if (idle && !prefersReduced) {
      idlePhase += 0.006;
      tx = W * (0.5 + 0.32 * Math.cos(idlePhase));
      ty = H * (0.5 + 0.30 * Math.sin(idlePhase * 0.8));
      glow = 0.55;
    }

    paintSpotlight(tx, ty, glow);

    var moved = 0;
    for (var i = 0; i < dots.length; i++) {
      var d = dots[i];
      var dx = tx - d.hx;
      var dy = ty - d.hy;
      var dist = Math.hypot(dx, dy);

      var targetX = d.hx, targetY = d.hy, prox = 0;
      if (dist < influence) {
        prox = 1 - dist / influence;          // 1 at centre → 0 at edge
        var ease = prox * prox;               // sharper falloff
        var pull = Math.min(maxPull, dist) * ease;
        if (dist > 0.01) {
          targetX = d.hx + (dx / dist) * pull;
          targetY = d.hy + (dy / dist) * pull;
        }
      }

      d.x = lerp(d.x, targetX, 0.14);
      d.y = lerp(d.y, targetY, 0.14);
      moved += Math.abs(d.x - targetX) + Math.abs(d.y - targetY);

      var rr = radiusBase + prox * 1.9;
      var a  = 0.16 + prox * 0.84;
      var col = prox > 0
        ? [Math.round(lerp(BASE[0], ACCENT[0] + 60, prox)),
           Math.round(lerp(BASE[1], ACCENT[1] + 50, prox)),
           Math.round(lerp(BASE[2], ACCENT[2], prox))]
        : BASE;

      ctx.beginPath();
      ctx.arc(d.x, d.y, rr, 0, 6.283185);
      ctx.fillStyle = 'rgba(' + col[0] + ',' + col[1] + ',' + col[2] + ',' + a.toFixed(3) + ')';
      ctx.fill();
    }

    // Sleep the loop once everything is parked and the pointer is gone.
    if (idle && moved < 0.5 && !coarse && !prefersReduced) {
      settledFrames++;
      if (settledFrames > 40) { running = false; return; }
    } else {
      settledFrames = 0;
    }
    rafId = requestAnimationFrame(frame);
  }

  var rafId = null, running = false;
  function wake() {
    if (running) return;
    running = true;
    settledFrames = 0;
    rafId = requestAnimationFrame(frame);
  }

  function onMove(x, y) {
    px = x; py = y; pActive = true;
    lastMove = performance.now();
    wake();
  }

  // --- events -------------------------------------------------------
  window.addEventListener('pointermove', function (e) {
    if (e.pointerType === 'touch') return;     // touch handled separately
    var r = canvas.getBoundingClientRect();
    onMove(e.clientX - r.left, e.clientY - r.top);
  }, { passive: true });

  window.addEventListener('pointerout', function (e) {
    if (e.pointerType === 'touch') return;
    pActive = false; lastMove = performance.now();
  }, { passive: true });

  window.addEventListener('touchmove', function (e) {
    if (!e.touches || !e.touches.length) return;
    var r = canvas.getBoundingClientRect();
    onMove(e.touches[0].clientX - r.left, e.touches[0].clientY - r.top);
  }, { passive: true });
  window.addEventListener('touchend', function () {
    pActive = false; lastMove = performance.now();
  }, { passive: true });

  var resizeTimer = null;
  window.addEventListener('resize', function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () { build(); wake(); }, 150);
  });

  // --- boot ---------------------------------------------------------
  build();
  if (prefersReduced) {
    // one static, calm render — no animation
    ctx.clearRect(0, 0, W, H);
    for (var i = 0; i < dots.length; i++) {
      var d = dots[i];
      ctx.beginPath();
      ctx.arc(d.hx, d.hy, radiusBase, 0, 6.283185);
      ctx.fillStyle = 'rgba(' + BASE[0] + ',' + BASE[1] + ',' + BASE[2] + ',0.18)';
      ctx.fill();
    }
  } else {
    wake();
  }
})();
