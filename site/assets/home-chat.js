/* Homepage inline AI console — index.html only.
 * Google-style centered chatbox. Reuses the support-bot KB + Worker logic
 * (tokenize / scoreEntry / search / md), but renders inline into the page
 * instead of a floating panel. Self-contained, no dependency on support-bot.js.
 *
 * Flow: load support-kb.json -> keyword score the query -> if a confident KB
 * hit, render that answer; otherwise POST {question, context} to the Cloudflare
 * Worker (Claude) and render {answer} with an "AI" badge. On worker error/empty,
 * show a graceful message + a pre-filled GitHub issue link.
 *
 * CORS note: the Worker only allows Origin https://kuschel-code.github.io and
 * localhost:8080, so live AI answers work on the deployed site / localhost:8080.
 * From file:// the Worker returns 403 -> the chat falls back to the GitHub path.
 */
(function () {
  "use strict";
  if (window.__homeChatLoaded) return;
  window.__homeChatLoaded = true;

  var form = document.getElementById("hc-form");
  if (!form) return; // not the homepage

  var REPO = "Kuschel-code/JellyfinUpscalerPlugin";
  var NEWISSUE = "https://github.com/" + REPO + "/issues/new";
  var WORKER = "https://upscaler-support-chat.weltraumaffe02.workers.dev";
  var ISSUES_URL = "https://github.com/" + REPO + "/issues";
  var KB = null;

  // Maps KB entry id -> deep-link doc page (mirrors support-bot.js DOC_MAP).
  var DOC_MAP = {
    "install-checksum": "installation.html", "no-newer-version": "installation.html",
    "not-supported-abi": "installation.html", "docker-unreachable": "troubleshooting.html",
    "gpu-on-cpu": "hardware.html", "intel-arc-wsl2": "hardware.html",
    "onnx-reshape-fp16": "troubleshooting.html", "api-token": "configuration.html",
    "non-admin-users": "security.html", "aspect-ratio-43": "troubleshooting.html",
    "amd-vulkan": "hardware.html", "nvidia-error": "hardware.html",
    "realtime-webgl": "features.html", "job-stuck-95": "troubleshooting.html",
    "choose-model": "models.html", "nas-setup": "deployment.html",
    "select-library": "configuration.html", "old-docker-tag": "deployment.html",
    "getting-started": "installation.html"
  };

  var STOP = { the: 1, a: 1, an: 1, is: 1, are: 1, my: 1, i: 1, to: 1, of: 1, on: 1, in: 1, it: 1, and: 1, or: 1, for: 1, with: 1, how: 1, do: 1, does: 1, can: 1, why: 1, when: 1, me: 1, you: 1, not: 1, no: 1, get: 1, getting: 1, should: 1, which: 1, use: 1 };

  // --- helpers (lifted from support-bot.js) ---
  function esc(s) { return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }

  function md(text) {
    var parts = String(text).split(/```/), out = "";
    for (var i = 0; i < parts.length; i++) {
      if (i % 2 === 1) { out += "<pre><code>" + esc(parts[i].replace(/^\n/, "")) + "</code></pre>"; continue; }
      var x = esc(parts[i]);
      x = x.replace(/`([^`]+)`/g, "<code>$1</code>");
      x = x.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
      x = x.replace(/(https?:\/\/[^\s)]+)/g, '<a href="$1" target="_blank" rel="noopener">$1</a>');
      x = x.replace(/\n/g, "<br>");
      out += x;
    }
    return out;
  }

  function tokenize(s) { return String(s).toLowerCase().split(/[^a-z0-9.#+]+/).filter(function (w) { return w.length > 1 && !STOP[w]; }); }
  function scoreEntry(e, tokens, raw) {
    var hay = (e.keywords || []).join(" ").toLowerCase(), title = (e.title || "").toLowerCase(), s = 0;
    for (var i = 0; i < tokens.length; i++) { var tk = tokens[i]; if (hay.indexOf(tk) !== -1) s += 3; if (title.indexOf(tk) !== -1) s += 2; }
    (e.keywords || []).forEach(function (k) { if (k.indexOf(" ") !== -1 && raw.indexOf(k) !== -1) s += 5; });
    return s;
  }
  function search(query) {
    var raw = query.toLowerCase(), tokens = tokenize(query);
    if (!KB || !tokens.length) return [];
    return KB.entries.map(function (e) { return { e: e, s: scoreEntry(e, tokens, raw) }; })
      .filter(function (r) { return r.s > 0; }).sort(function (a, b) { return b.s - a.s; });
  }

  // context for the Worker — top KB entries' title+answer (mirrors haikuContext).
  function workerContext(query) {
    if (!KB || !KB.entries) return "";
    var ranked = KB.entries.map(function (e) { return { e: e, s: scoreEntry(e, tokenize(query), query.toLowerCase()) }; })
      .sort(function (a, b) { return b.s - a.s; });
    var out = "All support topics: " + KB.entries.map(function (e) { return e.title; }).join("; ") + "\n\n";
    for (var i = 0; i < ranked.length && out.length < 5200; i++) {
      out += "## " + ranked[i].e.title + "\n" + ranked[i].e.answer + "\n\n";
    }
    return out.slice(0, 5800);
  }

  function issueUrl(q) {
    var body = "**Problem:**\n" + (q || "") + "\n\n**Plugin version:** v1.8.2\n**Docker image tag:** (e.g. docker7-intel)\n**Hardware / GPU:** \n**Jellyfin version:** \n**/gpu-verify output:** \n**Relevant logs:** ";
    return NEWISSUE + "?title=" + encodeURIComponent((q || "Support request").slice(0, 80)) + "&body=" + encodeURIComponent(body);
  }

  // --- DOM refs ---
  var consoleEl = document.getElementById("home-console");
  var input = document.getElementById("hc-input");
  var sendBtn = document.getElementById("hc-send");
  var convo = document.getElementById("hc-convo");
  var suggest = document.getElementById("hc-suggest");

  function activate() { consoleEl.classList.add("has-msgs"); }

  function addEl(html, who) {
    var d = document.createElement("div");
    d.className = "hc-msg hc-msg-" + who;
    d.innerHTML = html;
    convo.appendChild(d);
    d.scrollIntoView({ behavior: "smooth", block: "nearest" });
    return d;
  }

  function srcLabel(kind, text) {
    return '<span class="hc-srclabel hc-src-' + kind + '">' + esc(text) + "</span>";
  }

  function relLinks(entry) {
    var bits = [];
    if (DOC_MAP[entry.id]) bits.push('<a href="' + DOC_MAP[entry.id] + '">Open docs</a>');
    if (entry.issues && entry.issues.length) {
      bits.push("Related: " + entry.issues.slice(0, 4).map(function (n) {
        return '<a href="https://github.com/' + REPO + "/issues/" + n + '" target="_blank" rel="noopener">#' + n + "</a>";
      }).join(" "));
    }
    return bits.length ? '<div class="hc-rel">' + bits.join(" &nbsp;·&nbsp; ") + "</div>" : "";
  }

  function renderKb(entry) {
    return srcLabel("kb", "Knowledge base") +
      "<strong>" + esc(entry.title) + "</strong><br>" + md(entry.answer) + relLinks(entry);
  }
  function renderAi(answer) {
    return srcLabel("ai", "AI") + md(answer);
  }
  function renderError() {
    return srcLabel("err", "Offline") +
      "I couldn't reach the AI assistant right now and found no close match in the knowledge base. " +
      "You can browse or open an issue on GitHub:<br>" +
      '<a href="' + esc(ISSUES_URL) + '" target="_blank" rel="noopener">GitHub issues</a> &nbsp;·&nbsp; ' +
      '<a href="' + esc(issueUrl(lastQuery)) + '" target="_blank" rel="noopener">Open a pre-filled issue</a>';
  }

  function typingEl() {
    return addEl(srcLabel("ai", "AI") + '<span class="hc-typing"><span></span><span></span><span></span></span>', "bot");
  }

  var busy = false, lastQuery = "";

  function askWorker(query, cb) {
    fetch(WORKER, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ question: query, context: workerContext(query) })
    })
      .then(function (r) { if (!r.ok) throw r.status; return r.json(); })
      .then(function (d) { cb(d && d.answer ? String(d.answer) : null); })
      .catch(function () { cb(null); });
  }

  // A KB answer needs a CONFIDENT match (a multi-word phrase hit, or several
  // keyword hits). Vague / off-topic / meta queries ("write in German", "hi",
  // "does it work with X") score low and go to the AI instead of returning an
  // irrelevant KB entry.
  var KB_MIN_SCORE = 6;
  function respond(query) {
    var hits = search(query);
    if (hits.length && hits[0].s >= KB_MIN_SCORE && KB) {
      // confident KB hit -> answer immediately from the knowledge base.
      addEl(renderKb(hits[0].e), "bot");
      finish();
      return;
    }
    // no KB hit -> ask the Worker (Claude), graceful fallback on failure.
    var t = typingEl();
    askWorker(query, function (answer) {
      if (answer) { t.innerHTML = renderAi(answer); }
      else { t.innerHTML = renderError(); }
      t.scrollIntoView({ behavior: "smooth", block: "nearest" });
      finish();
    });
  }

  function finish() { busy = false; sendBtn.disabled = false; }

  function submit(query) {
    var q = (query || "").trim();
    if (!q || busy) return;
    busy = true; sendBtn.disabled = true;
    lastQuery = q;
    activate();
    addEl(esc(q), "user");
    input.value = "";
    setTimeout(function () { respond(q); }, 120);
  }

  // --- wire events ---
  form.addEventListener("submit", function (e) {
    e.preventDefault();
    submit(input.value);
  });

  suggest.addEventListener("click", function (e) {
    var chip = e.target.closest && e.target.closest(".hc-chip");
    if (chip) submit(chip.textContent);
  });

  // --- load KB ---
  fetch("assets/support-kb.json", { cache: "no-cache" })
    .then(function (r) { return r.json(); })
    .then(function (d) { KB = d; })
    .catch(function () { KB = null; });
})();
