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

  // --- live release facts (fetched once from the plugin feed; the feed is the
  // source of truth, so the AI can never quote a stale "latest" version) ---
  var LATEST = null; // { version, date, abi, changelog }
  function loadLatest() {
    fetch("https://raw.githubusercontent.com/" + REPO + "/main/repository-jellyfin.json", { cache: "no-cache" })
      .then(function (r) { return r.json(); })
      .then(function (d) {
        var v = d && d[0] && d[0].versions && d[0].versions[0];
        if (v && v.version) {
          LATEST = {
            version: String(v.version),
            date: String(v.timestamp || "").slice(0, 10),
            abi: String(v.targetAbi || ""),
            changelog: String(v.changelog || "").split(" | ")[0].slice(0, 900)
          };
        }
      })
      .catch(function () {
        // fallback: GitHub releases API
        fetch("https://api.github.com/repos/" + REPO + "/releases/latest")
          .then(function (r) { return r.json(); })
          .then(function (d) {
            if (d && d.tag_name) {
              LATEST = { version: String(d.tag_name).replace(/^v/, ""), date: String(d.published_at || "").slice(0, 10), abi: "", changelog: String(d.body || "").slice(0, 900) };
            }
          })
          .catch(function () { LATEST = null; });
      });
  }
  loadLatest();

  // --- importable-models catalog (OpenModelDB) ---------------------------
  // site/models-import.json is the same data the "Importable models" page
  // renders. Loading it here lets the assistant answer questions about any of
  // the ~660 community models (license, size, download) instead of guessing.
  var IMPORT = null; // { generated, direct: [...], convCount }
  function loadImport() {
    fetch("models-import.json", { cache: "no-cache" })
      .then(function (r) { return r.json(); })
      .then(function (d) {
        if (d && d.direct_onnx) {
          IMPORT = {
            generated: d.generated || "",
            direct: d.direct_onnx,
            convCount: (d.requires_conversion || []).length
          };
        }
      })
      .catch(function () { IMPORT = null; });
  }
  loadImport();

  var IMPORT_STOPWORDS = { models: 1, upscale: 1, upscaler: 1, license: 1, download: 1, openmodeldb: 1, convert: 1, "import": 1, imports: 1 };
  function normId(s) { return String(s).toLowerCase().replace(/[^a-z0-9]+/g, ""); }
  // Match catalog entries whose id/name contains a specific-enough query token
  // (>=6 chars, not a generic word). Longest token wins; up to `limit` hits.
  function searchImport(query, limit) {
    if (!IMPORT) return [];
    var tokens = String(query).toLowerCase().split(/[^a-z0-9]+/).filter(function (t) {
      return t.length >= 6 && !IMPORT_STOPWORDS[t];
    });
    if (!tokens.length) return [];
    var hits = [];
    for (var i = 0; i < IMPORT.direct.length; i++) {
      var m = IMPORT.direct[i];
      var hay = normId(m.id) + " " + normId(m.name);
      var best = 0;
      for (var t = 0; t < tokens.length; t++) {
        if (hay.indexOf(tokens[t]) !== -1 && tokens[t].length > best) best = tokens[t].length;
      }
      if (best > 0) hits.push({ m: m, s: best });
    }
    hits.sort(function (a, b) { return b.s - a.s; });
    return hits.slice(0, limit || 3);
  }
  function importHuman(bytes) {
    if (!bytes) return "?";
    var mb = bytes / (1024 * 1024);
    return mb >= 1 ? mb.toFixed(1) + " MB" : (bytes / 1024).toFixed(0) + " KB";
  }
  // compact catalog excerpt for the Worker prompt (kept small; context is capped)
  function importContext(query) {
    if (!IMPORT) return "";
    var out = "IMPORTABLE COMMUNITY MODELS (OpenModelDB, page models-import.html, refreshed weekly): " +
      IMPORT.direct.length + " ready-to-use ONNX + " + IMPORT.convCount +
      " convertible. Import: plugin config page card 'Import Community Model' (v1.8.3.6+) downloads, sha256-verifies and registers the model automatically; manual alternative: POST /models/upload on the AI service or docs/MODEL-HOSTING.md. NC license = non-commercial.\n";
    var hits = searchImport(query, 3);
    for (var i = 0; i < hits.length; i++) {
      var m = hits[i].m;
      out += "- " + m.name + " (" + m.scale + "x, " + m.architecture + ", " +
        (m.license || "license unclear") + ", " + importHuman(m.size_bytes) + ") " + m.omdb_url + "\n";
    }
    return out.slice(0, 900) + "\n";
  }
  function renderImportHit(m) {
    var nc = /NC/i.test(m.license || "");
    var lic = m.license ? esc(m.license) + (nc ? " — <strong>non-commercial</strong>" : "") : "license unclear — verify with the author";
    var txt = "<strong>" + esc(m.name) + "</strong> — importable community model (OpenModelDB)<br>" +
      esc(String(m.scale)) + "× · <code>" + esc(m.architecture) + "</code> · " + importHuman(m.size_bytes) +
      " · License: " + lic + "<br>" +
      "Import today: download the ONNX, verify sha256" +
      (m.sha256 ? " (<code>" + esc(m.sha256.slice(0, 12)) + "…</code>)" : "") +
      " — easiest: import it directly on the plugin config page (card <em>Import Community Model</em>, v1.8.3.6+). Manual alternative: upload it via <code>POST /models/upload</code> on your AI service — either way it appears in the plugin's model list immediately.";
    return srcLabel("kb", "Importable model") + txt +
      '<div class="hc-rel">' +
      (/^https?:\/\//i.test(m.download_url || "") ? '<a href="' + esc(m.download_url) + '" target="_blank" rel="noopener">Download ONNX</a> &nbsp;·&nbsp; ' : "") +
      '<a href="' + esc(m.omdb_url) + '" target="_blank" rel="noopener">OpenModelDB</a> &nbsp;·&nbsp; ' +
      '<a href="models-import.html">All importable models</a></div>';
  }

  // authoritative live facts, prepended to every Worker prompt
  function currentFacts() {
    var out = "CURRENT FACTS (live, authoritative - always prefer these over anything below):\n";
    if (LATEST) {
      out += "- Latest plugin release: v" + LATEST.version + (LATEST.date ? " (" + LATEST.date + ")" : "") +
        (LATEST.abi ? ", targetAbi " + LATEST.abi : "") +
        ". Users on older versions should update via Jellyfin Dashboard -> Plugins -> Catalog.\n";
      if (LATEST.changelog) out += "- What's new: " + LATEST.changelog + "\n";
    }
    out += "- Docker images: docker.io/kuscheltier/jellyfin-ai-upscaler - rolling tags docker7 (NVIDIA), docker7-cpu, docker7-intel, docker7-amd, docker7-vulkan, docker7-apple (auto-update targets); ':latest' = NVIDIA variant; version pins vX.Y.Z-<variant>. The AI service listens on port 5000. The PLUGIN updates through the Jellyfin catalog; the DOCKER image updates via docker pull.\n";
    return out + "\n";
  }

  // conversation memory for follow-up questions (last few turns, truncated)
  var HISTORY = [];
  function remember(q, a) {
    HISTORY.push({ q: String(q).slice(0, 240), a: String(a).replace(/<[^>]+>/g, " ").slice(0, 320) });
    if (HISTORY.length > 4) HISTORY.shift();
  }

  // context for the Worker — live facts + import catalog + conversation + KB.
  function workerContext(query) {
    var out = currentFacts() + importContext(query);
    if (HISTORY.length) {
      out += "CONVERSATION SO FAR (for follow-up questions):\n";
      for (var h = 0; h < HISTORY.length; h++) {
        out += "User: " + HISTORY[h].q + "\nAssistant: " + HISTORY[h].a + "\n";
      }
      out += "\n";
    }
    if (!KB || !KB.entries) return out.slice(0, 5900);
    var ranked = KB.entries.map(function (e) { return { e: e, s: scoreEntry(e, tokenize(query), query.toLowerCase()) }; })
      .sort(function (a, b) { return b.s - a.s; });
    out += "All support topics: " + KB.entries.map(function (e) { return e.title; }).join("; ") + "\n\n";
    for (var i = 0; i < ranked.length && out.length < 5300; i++) {
      out += "## " + ranked[i].e.title + "\n" + ranked[i].e.answer + "\n\n";
    }
    return out.slice(0, 5900);
  }

  function issueUrl(q) {
    var ver = LATEST ? "v" + LATEST.version : "(your version)";
    var body = "**Problem:**\n" + (q || "") + "\n\n**Plugin version:** " + ver + "\n**Docker image tag:** (e.g. docker7-intel)\n**Hardware / GPU:** \n**Jellyfin version:** \n**/gpu-verify output:** \n**Relevant logs:** ";
    return NEWISSUE + "?title=" + encodeURIComponent((q || "Support request").slice(0, 80)) + "&body=" + encodeURIComponent(body);
  }

  // --- log analysis -------------------------------------------------
  // Pasted logs are detected, matched against known failure signatures first
  // (instant, deterministic), otherwise distilled + sent to the AI.
  var LOG_PATTERNS = [
    { re: /cannot write to .*read-only|could not inject player script|access to the path .*index\.html.*denied/i, kb: "player-button-missing" },
    { re: /sha256 mismatch/i, kb: "model-sha256-mismatch" },
    { re: /checksum (mismatch|failed|did not match)|package .*checksum/i, kb: "install-checksum" },
    { re: /not supported.*abi|targetabi|abi.*mismatch/i, kb: "not-supported-abi" },
    { re: /azureexecutionprovider(?![\s\S]*cudaexecutionprovider)/i, kb: "gpu-on-cpu" },
    { re: /ai service unreachable|connection refused.*:5000|unable to connect.*(docker|:5000)|econnrefused.*5000/i, kb: "docker-unreachable" },
    { re: /missingmethodexception|typeloadexception/i, kb: "jellyfin-12" },
    { re: /invalid_graph|invalidgraph/i, kb: "models-self-host" },
    { re: /reshape.*(fp16|input)|invalid_argument.*onnx/i, kb: "onnx-reshape-fp16" },
    { re: /ffmpeg.*(exited|crash|code 1)|no such file.*ffmpeg/i, kb: "ffmpeg-crash" }
  ];
  function looksLikeLog(q) {
    var lines = q.split("\n");
    if (lines.length >= 3 && /\[(WRN|ERR|INF|WARN|ERROR|FTL)\]|\d{2}:\d{2}:\d{2}|Exception|Traceback/i.test(q)) return true;
    return q.length > 350 && /error|exception|failed|denied|refused/i.test(q);
  }
  function matchLogPatterns(q) {
    var hits = [];
    for (var i = 0; i < LOG_PATTERNS.length && hits.length < 2; i++) {
      if (LOG_PATTERNS[i].re.test(q)) {
        var id = LOG_PATTERNS[i].kb;
        var entry = KB && KB.entries && KB.entries.filter(function (e) { return e.id === id; })[0];
        if (entry && hits.indexOf(entry) === -1) hits.push(entry);
      }
    }
    return hits;
  }
  // keep the interesting lines (errors first) and fit the Worker's question cap
  function distillLog(q) {
    var lines = q.split("\n");
    var bad = lines.filter(function (l) { return /\[(WRN|ERR|FTL|WARN|ERROR)\]|error|exception|failed|denied|refused|mismatch/i.test(l); });
    var picked = (bad.length ? bad : lines).slice(0, 18).join("\n");
    return picked.slice(0, 1500);
  }

  // --- GitHub issue links -------------------------------------------
  // "https://github.com/<this repo>/issues/75" (or "issue #75") in a message:
  // fetch the issue via GitHub's CORS-enabled API and analyze it - known log
  // signatures in the issue body answer instantly, everything else goes to the
  // AI with the issue content as context.
  function detectIssueRef(q) {
    var m = q.match(new RegExp("github\\.com/" + REPO.replace(/[/.]/g, "\\$&") + "/issues/(\\d+)", "i")) ||
            q.match(/\bissue\s*#?(\d+)\b/i) ||
            q.trim().match(/^#(\d+)$/);   // bare "#75" only as the whole message
    return m ? parseInt(m[1], 10) : null;
  }
  function fetchIssue(num, cb) {
    var api = "https://api.github.com/repos/" + REPO + "/issues/" + num;
    fetch(api).then(function (r) { if (!r.ok) throw r.status; return r.json(); })
      .then(function (issue) {
        if (!issue || !issue.title) { cb(null); return; }
        if (issue.comments > 0) {
          fetch(api + "/comments?per_page=2&sort=created&direction=desc")
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (cs) { cb(issue, cs || []); })
            .catch(function () { cb(issue, []); });
        } else { cb(issue, []); }
      })
      .catch(function () { cb(null); });
  }
  function renderIssueCard(issue) {
    var state = issue.state === "open" ? "open" : "closed";
    return srcLabel("kb", "GitHub issue") +
      "<strong>#" + issue.number + " · " + state + " — " + esc(issue.title) + "</strong>" +
      '<div class="hc-rel"><a href="' + esc(issue.html_url) + '" target="_blank" rel="noopener">View on GitHub</a></div>';
  }
  function issueDigest(issue, comments) {
    var out = "GITHUB ISSUE #" + issue.number + " (" + issue.state + "): " + issue.title + "\n" +
      String(issue.body || "").slice(0, 1000);
    (comments || []).forEach(function (c) {
      out += "\nCOMMENT by " + (c.user && c.user.login || "?") + ": " + String(c.body || "").slice(0, 450);
    });
    return out.slice(0, 1750);
  }

  // deterministic answer for "what's the latest version?" style questions
  function isVersionQuestion(q) {
    if (q.length > 90 || looksLikeLog(q)) return false;
    return /(latest|newest|current|aktuellste?|neueste?|new)\s+(version|release)|version.*(latest|newest|current|out|available)|what'?s new|which version|welche version|neue version/i.test(q.toLowerCase());
  }
  function renderVersion() {
    if (!LATEST) return null;
    var txt = "**Latest release: v" + LATEST.version + "**" + (LATEST.date ? " (" + LATEST.date + ")" : "") + "\n\n" +
      (LATEST.changelog ? LATEST.changelog + "\n\n" : "") +
      "**Update:** Jellyfin Dashboard → Plugins → Catalog → AI Upscaler → Update, then restart Jellyfin. " +
      "Docker images: `docker pull kuscheltier/jellyfin-ai-upscaler:docker7-<variant>`.";
    return srcLabel("kb", "Live release info") + md(txt) +
      '<div class="hc-rel"><a href="https://github.com/' + REPO + '/releases" target="_blank" rel="noopener">All releases</a></div>';
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

  function askWorker(query, cb, extraContext) {
    fetch(WORKER, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ question: query, context: ((extraContext || "") + workerContext(query)).slice(0, 5900) })
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
    // 1) version questions -> answered live from the release feed (never stale)
    if (isVersionQuestion(query)) {
      var v = renderVersion();
      if (v) {
        addEl(v, "bot");
        remember(query, "Latest release is v" + LATEST.version + (LATEST.date ? " (" + LATEST.date + ")" : ""));
        finish();
        return;
      }
      // feed not loaded (yet/offline) -> fall through to the AI
    }
    // 2) GitHub issue link/reference -> fetch it and analyze
    var issueNum = looksLikeLog(query) ? null : detectIssueRef(query);
    if (issueNum) {
      var ti = typingEl();
      fetchIssue(issueNum, function (issue, comments) {
        if (!issue) {
          ti.innerHTML = srcLabel("err", "Not found") +
            "I couldn't load issue #" + issueNum + " from GitHub (it may not exist, or the API rate limit is reached). " +
            '<a href="https://github.com/' + REPO + '/issues/' + issueNum + '" target="_blank" rel="noopener">Try opening it directly</a>.';
          finish();
          return;
        }
        ti.innerHTML = renderIssueCard(issue);
        var issueText = issue.title + "\n" + String(issue.body || "");
        var kbHits = matchLogPatterns(issueText);
        var extra = query.replace(/https?:\/\/\S+/g, " ").replace(/\bissue\s*#?\d+\b/ig, " ").trim();
        if (kbHits.length && extra.length < 12) {
          // the issue body carries a known failure signature and the user asked
          // nothing beyond the link -> answer deterministically from the KB.
          kbHits.forEach(function (e) { addEl(renderKb(e), "bot"); });
          remember("issue #" + issue.number, kbHits.map(function (e) { return e.title; }).join("; "));
          finish();
          return;
        }
        var t2 = typingEl();
        var q2 = "Help with this GitHub issue of the plugin. Explain the problem and the fix." +
          (extra ? " The user also asks: " + extra.slice(0, 220) : "") + "\n\n" + issueDigest(issue, comments);
        askWorker(q2, function (answer) {
          if (answer) { t2.innerHTML = renderAi(answer); remember("issue #" + issue.number + (extra ? " - " + extra.slice(0, 120) : ""), answer); }
          else { t2.innerHTML = renderError(); }
          t2.scrollIntoView({ behavior: "smooth", block: "nearest" });
          finish();
        });
      });
      return;
    }
    // 3) pasted log -> match known failure signatures first (instant, exact)
    if (looksLikeLog(query)) {
      var logHits = matchLogPatterns(query);
      if (logHits.length) {
        var html = srcLabel("kb", "Log analysis") +
          "<strong>Found " + (logHits.length === 1 ? "a known signature" : logHits.length + " known signatures") + " in your log:</strong>";
        addEl(html, "bot");
        logHits.forEach(function (e) { addEl(renderKb(e), "bot"); });
        remember("(pasted log)", logHits.map(function (e) { return e.title; }).join("; "));
        finish();
        return;
      }
      // unknown log -> distill the interesting lines and let the AI analyze it
      var tl = typingEl();
      var distilled = "Analyze this Jellyfin AI Upscaler log excerpt. Identify the problem and give a concrete fix:\n" + distillLog(query);
      askWorker(distilled, function (answer) {
        if (answer) { tl.innerHTML = renderAi(answer); remember("(pasted log)", answer); }
        else { tl.innerHTML = renderError(); }
        tl.scrollIntoView({ behavior: "smooth", block: "nearest" });
        finish();
      });
      return;
    }
    // 4) confident KB hit -> answer immediately from the knowledge base.
    var hits = search(query);
    if (hits.length && hits[0].s >= KB_MIN_SCORE && KB) {
      addEl(renderKb(hits[0].e), "bot");
      remember(query, hits[0].e.title);
      finish();
      return;
    }
    // 4b) a specific importable-model name (no KB hit) -> answer from the
    // OpenModelDB catalog directly. Threshold 8 = a distinctly model-shaped
    // token ("ultrasharpv2", "animejanai"), so generic wording still reaches
    // the AI with the catalog excerpt as context instead.
    var importHits = searchImport(query, 2);
    if (importHits.length && importHits[0].s >= 8) {
      importHits.forEach(function (h) { addEl(renderImportHit(h.m), "bot"); });
      remember(query, "importable model: " + importHits.map(function (h) { return h.m.name; }).join("; "));
      finish();
      return;
    }
    // 5) otherwise ask the Worker (LLM), graceful fallback on failure.
    var t = typingEl();
    askWorker(query, function (answer) {
      if (answer) { t.innerHTML = renderAi(answer); remember(query, answer); }
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
    input.style.height = "";                    // collapse an expanded log box
    form.classList.remove("hc-multiline");      // regardless of send path
    setTimeout(function () { respond(q); }, 120);
  }

  // --- wire events ---
  form.addEventListener("submit", function (e) {
    e.preventDefault();
    submit(input.value);
  });

  // textarea behaviour: Enter sends, Shift+Enter = newline (for pasting logs).
  // While typing normally the field stays a fixed single line (exactly like the
  // old <input>); it only expands when the content actually contains newlines
  // (a pasted log). Keying growth off the CONTENT - not off measured heights -
  // avoids feedback loops (style changes -> new height -> class flips back).
  function autoGrow() {
    var multi = input.value.indexOf("\n") !== -1;
    form.classList.toggle("hc-multiline", multi);
    if (multi) {
      input.style.height = "auto";
      input.style.height = Math.min(input.scrollHeight, 180) + "px";
    } else {
      input.style.height = ""; // back to the CSS single-line height
    }
  }
  input.addEventListener("input", autoGrow);
  input.addEventListener("keydown", function (e) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      submit(input.value);
      input.style.height = "";
      form.classList.remove("hc-multiline");
    }
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
