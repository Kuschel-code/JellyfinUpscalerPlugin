/* Support Assistant — client-side, no backend, no API key.
 * Answers from site/assets/support-kb.json (every support topic we've handled).
 * Self-contained: injects its own CSS + DOM. Loaded site-wide via nav.js.
 */
(function () {
  "use strict";
  if (window.__supportBotLoaded) return;
  window.__supportBotLoaded = true;

  var KB = null;
  var STOP = { the: 1, a: 1, an: 1, is: 1, are: 1, my: 1, i: 1, to: 1, of: 1, on: 1, in: 1, it: 1, and: 1, or: 1, for: 1, with: 1, how: 1, do: 1, does: 1, can: 1, why: 1, when: 1, me: 1, you: 1, not: 1, no: 1, get: 1, getting: 1 };

  function esc(s) {
    return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  // Minimal, safe markdown: escape first, then apply inline/blocks on escaped text.
  function md(text) {
    var parts = String(text).split(/```/);
    var out = "";
    for (var i = 0; i < parts.length; i++) {
      if (i % 2 === 1) { // inside a fenced code block
        out += "<pre><code>" + esc(parts[i].replace(/^\n/, "")) + "</code></pre>";
        continue;
      }
      var t = esc(parts[i]);
      t = t.replace(/`([^`]+)`/g, "<code>$1</code>");
      t = t.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
      t = t.replace(/(https?:\/\/[^\s)]+)/g, '<a href="$1" target="_blank" rel="noopener">$1</a>');
      t = t.replace(/\n/g, "<br>");
      out += t;
    }
    return out;
  }

  function tokenize(s) {
    return String(s).toLowerCase().split(/[^a-z0-9.#+]+/).filter(function (w) { return w.length > 1 && !STOP[w]; });
  }

  function score(entry, tokens, raw) {
    var hay = (entry.keywords || []).join(" ").toLowerCase();
    var title = (entry.title || "").toLowerCase();
    var s = 0;
    for (var i = 0; i < tokens.length; i++) {
      var t = tokens[i];
      if (hay.indexOf(t) !== -1) s += 3;
      if (title.indexOf(t) !== -1) s += 2;
    }
    // phrase boost: any multi-word keyword that appears verbatim in the query
    (entry.keywords || []).forEach(function (k) {
      if (k.indexOf(" ") !== -1 && raw.indexOf(k) !== -1) s += 5;
    });
    return s;
  }

  function search(query) {
    var raw = query.toLowerCase();
    var tokens = tokenize(query);
    if (!KB || !tokens.length) return [];
    return KB.entries
      .map(function (e) { return { e: e, s: score(e, tokens, raw) }; })
      .filter(function (r) { return r.s > 0; })
      .sort(function (a, b) { return b.s - a.s; })
      .map(function (r) { return r.e; });
  }

  // ---- DOM ----
  var panel, msgs, input;

  function addMsg(html, who) {
    var d = document.createElement("div");
    d.className = "sb-msg sb-" + who;
    d.innerHTML = html;
    msgs.appendChild(d);
    msgs.scrollTop = msgs.scrollHeight;
    return d;
  }

  function issueLinks(entry) {
    if (!entry.issues || !entry.issues.length) return "";
    var base = KB.repo + "/issues/";
    return '<div class="sb-rel">Related: ' + entry.issues.map(function (n) {
      return '<a href="' + base + n + '" target="_blank" rel="noopener">#' + n + "</a>";
    }).join(" ") + "</div>";
  }

  function answerFor(entry, alsoSee) {
    var html = "<strong>" + esc(entry.title) + "</strong><br>" + md(entry.answer) + issueLinks(entry);
    if (alsoSee && alsoSee.length) {
      html += '<div class="sb-rel">See also: ' + alsoSee.map(function (e) {
        return '<button class="sb-chip" data-id="' + e.id + '">' + esc(e.title) + "</button>";
      }).join(" ") + "</div>";
    }
    return html;
  }

  function showEntry(entry) {
    addMsg(answerFor(entry, []), "bot");
  }

  function respond(query) {
    var hits = search(query);
    if (!hits.length) {
      addMsg("I couldn't find that among our past issues. Try different words (e.g. \"GPU not used\", \"checksum\", \"api token\"), or open a new issue:<br><a href=\"" + KB.newIssueUrl + "\" target=\"_blank\" rel=\"noopener\">Open a GitHub issue</a>", "bot");
      return;
    }
    addMsg(answerFor(hits[0], hits.slice(1, 3)), "bot");
  }

  function chips(list) {
    return '<div class="sb-chips">' + list.map(function (e) {
      return '<button class="sb-chip" data-id="' + e.id + '">' + esc(e.title) + "</button>";
    }).join("") + "</div>";
  }

  function send() {
    var q = input.value.trim();
    if (!q) return;
    addMsg(esc(q), "user");
    input.value = "";
    setTimeout(function () { respond(q); }, 120);
  }

  function buildCSS() {
    var css = ""
      + ".sb-fab{position:fixed;right:20px;bottom:20px;z-index:9998;width:56px;height:56px;border-radius:50%;border:0;cursor:pointer;background:#22d3ee;color:#06283d;font-size:24px;box-shadow:0 6px 20px rgba(0,0,0,.35)}"
      + ".sb-fab:hover{filter:brightness(1.08)}"
      + ".sb-panel{position:fixed;right:20px;bottom:88px;z-index:9999;width:380px;max-width:calc(100vw - 32px);height:520px;max-height:calc(100vh - 120px);display:none;flex-direction:column;background:#0f172a;color:#e2e8f0;border:1px solid #1e293b;border-radius:14px;box-shadow:0 12px 40px rgba(0,0,0,.5);overflow:hidden;font:14px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}"
      + ".sb-panel.open{display:flex}"
      + ".sb-head{display:flex;align-items:center;gap:8px;padding:12px 14px;background:#111c33;border-bottom:1px solid #1e293b}"
      + ".sb-head b{font-size:14px}.sb-head .sb-sub{color:#7d8aa3;font-size:11px}"
      + ".sb-x{margin-left:auto;background:0;border:0;color:#7d8aa3;font-size:20px;cursor:pointer;line-height:1}"
      + ".sb-msgs{flex:1;overflow-y:auto;padding:14px;display:flex;flex-direction:column;gap:10px}"
      + ".sb-msg{padding:9px 12px;border-radius:10px;max-width:92%;word-wrap:break-word}"
      + ".sb-bot{background:#1e293b;align-self:flex-start;border-bottom-left-radius:3px}"
      + ".sb-user{background:#22d3ee;color:#06283d;align-self:flex-end;border-bottom-right-radius:3px}"
      + ".sb-msg code{background:#0b1220;padding:1px 5px;border-radius:4px;font-size:12px}"
      + ".sb-msg pre{background:#0b1220;padding:9px;border-radius:6px;overflow-x:auto;margin:6px 0}.sb-msg pre code{background:0;padding:0}"
      + ".sb-msg a{color:#22d3ee}"
      + ".sb-rel{margin-top:8px;font-size:12px;color:#7d8aa3}.sb-rel a{margin-right:6px}"
      + ".sb-chips{display:flex;flex-wrap:wrap;gap:6px;margin-top:8px}"
      + ".sb-chip{background:#0b1220;border:1px solid #334155;color:#cbd5e1;border-radius:999px;padding:5px 10px;font-size:12px;cursor:pointer;text-align:left}"
      + ".sb-chip:hover{border-color:#22d3ee;color:#fff}"
      + ".sb-foot{display:flex;gap:8px;padding:10px;border-top:1px solid #1e293b;background:#111c33}"
      + ".sb-foot input{flex:1;background:#0b1220;border:1px solid #334155;color:#e2e8f0;border-radius:8px;padding:9px 10px;font-size:14px}"
      + ".sb-foot input:focus{outline:0;border-color:#22d3ee}"
      + ".sb-foot button{background:#22d3ee;color:#06283d;border:0;border-radius:8px;padding:0 14px;font-weight:600;cursor:pointer}";
    var st = document.createElement("style");
    st.textContent = css;
    document.head.appendChild(st);
  }

  function build() {
    buildCSS();

    var fab = document.createElement("button");
    fab.className = "sb-fab";
    fab.setAttribute("aria-label", "Open support assistant");
    fab.innerHTML = "&#128172;"; // speech balloon
    document.body.appendChild(fab);

    panel = document.createElement("div");
    panel.className = "sb-panel";
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-label", "Support assistant");
    panel.innerHTML =
      '<div class="sb-head"><b>Support Assistant</b><span class="sb-sub">answers from every past issue</span><button class="sb-x" aria-label="Close">&times;</button></div>'
      + '<div class="sb-msgs"></div>'
      + '<div class="sb-foot"><input type="text" placeholder="Describe your problem..." aria-label="Your question"><button type="button">Ask</button></div>';
    document.body.appendChild(panel);

    msgs = panel.querySelector(".sb-msgs");
    input = panel.querySelector(".sb-foot input");
    var sendBtn = panel.querySelector(".sb-foot button");
    var closeBtn = panel.querySelector(".sb-x");
    var opened = false;

    function open() {
      panel.classList.add("open");
      if (!opened) {
        opened = true;
        if (KB) {
          addMsg(esc(KB.intro), "bot");
          var common = ["gpu-on-cpu", "install-checksum", "api-token", "nas-setup", "docker-unreachable", "choose-model"]
            .map(function (id) { return KB.entries.filter(function (e) { return e.id === id; })[0]; })
            .filter(Boolean);
          addMsg("Common topics:" + chips(common), "bot");
        } else {
          addMsg("Loading knowledge base...", "bot");
        }
      }
      setTimeout(function () { input.focus(); }, 50);
    }
    function close() { panel.classList.remove("open"); }

    fab.addEventListener("click", function () { panel.classList.contains("open") ? close() : open(); });
    closeBtn.addEventListener("click", close);
    sendBtn.addEventListener("click", send);
    input.addEventListener("keydown", function (e) { if (e.key === "Enter") send(); });
    // delegated clicks for topic chips
    panel.addEventListener("click", function (e) {
      var b = e.target.closest && e.target.closest(".sb-chip");
      if (!b) return;
      var entry = KB.entries.filter(function (x) { return x.id === b.getAttribute("data-id"); })[0];
      if (entry) { addMsg(esc(entry.title), "user"); setTimeout(function () { showEntry(entry); }, 100); }
    });
  }

  function load() {
    fetch("assets/support-kb.json", { cache: "no-cache" })
      .then(function (r) { return r.json(); })
      .then(function (data) { KB = data; })
      .catch(function () { KB = null; });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () { build(); load(); });
  } else { build(); load(); }
})();
