/* home-fx.js — index.html only.
 * Three purposeful, hand-built motions for the Editorial-Brutalist homepage:
 *   1. scroll reveal   — IntersectionObserver adds .is-in (clip/slide), staggered.
 *   2. cursor tilt      — the before/after figure + feature cards tilt toward the
 *                         pointer (subtle rotateX/Y), settle back on leave.
 *   3. before/after     — draggable vermilion divider over the synthetic SVG
 *                         scene; auto-sweeps once on load, then user-controlled.
 * All motion is gated behind prefers-reduced-motion: no-preference and uses
 * transform/opacity only. No dependencies, no build.
 */
(function () {
  "use strict";
  if (window.__homeFxLoaded) return;
  window.__homeFxLoaded = true;

  var reduce = window.matchMedia &&
    window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  /* ---------- 1. scroll reveal ---------- */
  (function reveals() {
    var els = [].slice.call(document.querySelectorAll(".reveal, .reveal-line"));
    if (!els.length) return;
    if (reduce || !("IntersectionObserver" in window)) {
      els.forEach(function (el) { el.classList.add("is-in"); });
      return;
    }
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry, i) {
        if (!entry.isIntersecting) return;
        var el = entry.target;
        // small stagger between siblings entering together
        setTimeout(function () { el.classList.add("is-in"); }, i * 70);
        io.unobserve(el);
      });
    }, { threshold: 0.18, rootMargin: "0px 0px -8% 0px" });
    els.forEach(function (el) { io.observe(el); });
  })();

  /* ---------- 2. cursor-aware tilt ---------- */
  (function tilt() {
    if (reduce) return;
    var nodes = [].slice.call(document.querySelectorAll(".tilt"));
    var fig = document.getElementById("ba"); // tilt the slider scene itself
    if (fig) nodes.push(fig);
    if (!nodes.length) return;
    var supportsHover = !window.matchMedia ||
      window.matchMedia("(hover: hover)").matches;
    if (!supportsHover) return;

    nodes.forEach(function (el) {
      var raf = 0;
      var MAXX = el === fig ? 5 : 4;   // deg
      var MAXY = el === fig ? 7 : 5;   // deg
      function onMove(e) {
        if (raf) return;
        raf = requestAnimationFrame(function () {
          raf = 0;
          var r = el.getBoundingClientRect();
          var px = (e.clientX - r.left) / r.width - 0.5;  // -0.5..0.5
          var py = (e.clientY - r.top) / r.height - 0.5;
          el.style.transform =
            "rotateX(" + (-py * MAXX).toFixed(2) + "deg) " +
            "rotateY(" + (px * MAXY).toFixed(2) + "deg)";
        });
      }
      function reset() {
        if (raf) { cancelAnimationFrame(raf); raf = 0; }
        el.style.transform = "";
      }
      el.addEventListener("pointermove", onMove);
      el.addEventListener("pointerleave", reset);
    });
  })();

  /* ---------- 3. before / after slider ---------- */
  (function beforeAfter() {
    var ba = document.getElementById("ba");
    if (!ba) return;
    var dragging = false;

    function setSplit(pct) {
      pct = Math.max(2, Math.min(98, pct));
      ba.style.setProperty("--split", pct + "%");
      ba.setAttribute("aria-valuenow", Math.round(pct));
    }
    function pctFromEvent(e) {
      var r = ba.getBoundingClientRect();
      var x = (e.touches ? e.touches[0].clientX : e.clientX) - r.left;
      return (x / r.width) * 100;
    }

    function down(e) {
      dragging = true;
      ba.classList.add("dragging");
      setSplit(pctFromEvent(e));
      e.preventDefault();
    }
    function move(e) { if (dragging) setSplit(pctFromEvent(e)); }
    function up() { dragging = false; ba.classList.remove("dragging"); }

    ba.addEventListener("pointerdown", down);
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);

    // accessibility: focusable slider + arrow-key control
    ba.setAttribute("tabindex", "0");
    ba.setAttribute("role", "slider");
    ba.setAttribute("aria-label", "Before / after upscale comparison");
    ba.setAttribute("aria-valuemin", "0");
    ba.setAttribute("aria-valuemax", "100");
    ba.addEventListener("keydown", function (e) {
      var cur = parseFloat(ba.style.getPropertyValue("--split")) || 50;
      if (e.key === "ArrowLeft") { setSplit(cur - 4); e.preventDefault(); }
      else if (e.key === "ArrowRight") { setSplit(cur + 4); e.preventDefault(); }
    });

    /* one-time auto-sweep on first reveal */
    if (reduce) { setSplit(50); return; }
    function sweep() {
      var start = null;
      // center -> reveal source -> reveal upscaled -> settle center
      var keys = [
        { t: 0,    v: 50 },
        { t: 700,  v: 14 },
        { t: 1500, v: 84 },
        { t: 2200, v: 50 }
      ];
      var last = keys[keys.length - 1].t;
      function ease(p) { return p < 0.5 ? 2 * p * p : 1 - Math.pow(-2 * p + 2, 2) / 2; }
      function frame(ts) {
        if (start === null) start = ts;
        var el = ts - start;
        if (dragging) return; // user took over
        var seg = keys.length - 2;
        for (var i = 0; i < keys.length - 1; i++) {
          if (el >= keys[i].t && el <= keys[i + 1].t) { seg = i; break; }
        }
        var a = keys[seg], b = keys[seg + 1];
        var p = Math.min(1, Math.max(0, (el - a.t) / (b.t - a.t)));
        setSplit(a.v + (b.v - a.v) * ease(p));
        if (el < last) requestAnimationFrame(frame);
      }
      requestAnimationFrame(frame);
    }

    var fig = document.getElementById("ed-figure");
    if (fig && "IntersectionObserver" in window) {
      var swept = false;
      var io = new IntersectionObserver(function (ents) {
        ents.forEach(function (en) {
          if (en.isIntersecting && !swept) { swept = true; setTimeout(sweep, 350); io.disconnect(); }
        });
      }, { threshold: 0.4 });
      io.observe(fig);
    } else {
      setTimeout(sweep, 600);
    }
  })();
})();
