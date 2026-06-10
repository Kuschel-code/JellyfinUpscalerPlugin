/* Support Assistant — client-side, no backend, no API key.
 * KB answers from site/assets/support-kb.json + LIVE GitHub Issues search
 * (public, CORS-enabled api.github.com — no token). Self-contained: injects
 * its own CSS + DOM. Loaded site-wide via nav.js.
 * Features: KB keyword match, live GitHub issue search on miss, pre-filled
 * new-issue, per-topic docs deep-links, code-copy buttons, typing indicator,
 * persistent chat history, 8-language UI (answers stay English).
 */
(function () {
  "use strict";
  if (window.__supportBotLoaded) return;
  window.__supportBotLoaded = true;

  var REPO = "Kuschel-code/JellyfinUpscalerPlugin";
  var NEWISSUE = "https://github.com/" + REPO + "/issues/new";
  var KB = null;

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

  var I18N = {
    en: { name: "English", title: "Support Assistant", sub: "answers from every past issue", placeholder: "Describe your problem...", ask: "Ask", intro: "Hi! I answer from every support topic we've handled, and I can search all GitHub issues live. Ask about install errors, GPU not used, Docker/NAS setup, API tokens, models, and more.", common: "Common topics:", related: "Related:", seeAlso: "See also:", noMatch: "No quick match in our topics — searching GitHub...", openIssue: "Open a GitHub issue", note: "", searchAll: "🔎 Search all GitHub issues", searching: "Searching GitHub...", ghFound: "Matching issues on GitHub:", ghNone: "No matching issues on GitHub.", ghError: "GitHub search is busy (rate limit) — try again shortly.", openDocs: "📖 Docs", copy: "Copy", copied: "Copied!", prefill: "Open a pre-filled issue" },
    de: { name: "Deutsch", title: "Support-Assistent", sub: "Antworten aus allen bisherigen Issues", placeholder: "Beschreibe dein Problem...", ask: "Fragen", intro: "Hi! Ich antworte aus allen bisherigen Support-Themen und kann alle GitHub-Issues live durchsuchen. Frag zu Installationsfehlern, GPU wird nicht genutzt, Docker/NAS-Setup, API-Tokens, Modellen und mehr.", common: "Häufige Themen:", related: "Verwandt:", seeAlso: "Siehe auch:", noMatch: "Kein schneller Treffer in unseren Themen — durchsuche GitHub...", openIssue: "GitHub-Issue öffnen", note: "(Antworten sind aus Genauigkeitsgründen auf Englisch.)", searchAll: "🔎 Alle GitHub-Issues durchsuchen", searching: "Durchsuche GitHub...", ghFound: "Passende Issues auf GitHub:", ghNone: "Keine passenden Issues auf GitHub.", ghError: "GitHub-Suche ausgelastet (Rate-Limit) — gleich nochmal versuchen.", openDocs: "📖 Doku", copy: "Kopieren", copied: "Kopiert!", prefill: "Vorausgefülltes Issue öffnen" },
    es: { name: "Español", title: "Asistente de soporte", sub: "respuestas de cada incidencia anterior", placeholder: "Describe tu problema...", ask: "Preguntar", intro: "¡Hola! Respondo de todos los temas tratados y puedo buscar en todas las incidencias de GitHub en vivo. Pregunta por errores de instalación, GPU no usada, Docker/NAS, tokens de API, modelos y más.", common: "Temas frecuentes:", related: "Relacionado:", seeAlso: "Ver también:", noMatch: "Sin coincidencia rápida — buscando en GitHub...", openIssue: "Abrir incidencia en GitHub", note: "(Las respuestas están en inglés por precisión.)", searchAll: "🔎 Buscar en todas las incidencias de GitHub", searching: "Buscando en GitHub...", ghFound: "Incidencias coincidentes en GitHub:", ghNone: "Sin incidencias coincidentes en GitHub.", ghError: "Búsqueda de GitHub saturada (límite) — reintenta en un momento.", openDocs: "📖 Docs", copy: "Copiar", copied: "¡Copiado!", prefill: "Abrir incidencia precompletada" },
    fr: { name: "Français", title: "Assistant de support", sub: "réponses tirées de tous les tickets passés", placeholder: "Décrivez votre problème...", ask: "Demander", intro: "Bonjour ! Je réponds à partir de tous les sujets traités et je peux chercher dans tous les tickets GitHub en direct. Posez vos questions : erreurs d'installation, GPU non utilisé, Docker/NAS, jetons API, modèles, etc.", common: "Sujets fréquents :", related: "Connexe :", seeAlso: "Voir aussi :", noMatch: "Pas de correspondance rapide — recherche sur GitHub...", openIssue: "Ouvrir un ticket GitHub", note: "(Les réponses sont en anglais par souci de précision.)", searchAll: "🔎 Rechercher dans tous les tickets GitHub", searching: "Recherche sur GitHub...", ghFound: "Tickets correspondants sur GitHub :", ghNone: "Aucun ticket correspondant sur GitHub.", ghError: "Recherche GitHub saturée (limite) — réessayez bientôt.", openDocs: "📖 Docs", copy: "Copier", copied: "Copié !", prefill: "Ouvrir un ticket pré-rempli" },
    it: { name: "Italiano", title: "Assistente di supporto", sub: "risposte da ogni problema precedente", placeholder: "Descrivi il tuo problema...", ask: "Chiedi", intro: "Ciao! Rispondo da tutti gli argomenti trattati e posso cercare in tutte le issue GitHub in tempo reale. Chiedi di errori di installazione, GPU non usata, Docker/NAS, token API, modelli e altro.", common: "Argomenti comuni:", related: "Correlato:", seeAlso: "Vedi anche:", noMatch: "Nessuna corrispondenza rapida — ricerca su GitHub...", openIssue: "Apri una issue su GitHub", note: "(Le risposte sono in inglese per precisione.)", searchAll: "🔎 Cerca in tutte le issue GitHub", searching: "Ricerca su GitHub...", ghFound: "Issue corrispondenti su GitHub:", ghNone: "Nessuna issue corrispondente su GitHub.", ghError: "Ricerca GitHub occupata (limite) — riprova tra poco.", openDocs: "📖 Docs", copy: "Copia", copied: "Copiato!", prefill: "Apri una issue precompilata" },
    pt: { name: "Português", title: "Assistente de suporte", sub: "respostas de todos os problemas anteriores", placeholder: "Descreva o seu problema...", ask: "Perguntar", intro: "Olá! Respondo de todos os tópicos tratados e posso pesquisar todos os issues do GitHub ao vivo. Pergunte sobre erros de instalação, GPU não usada, Docker/NAS, tokens de API, modelos e mais.", common: "Tópicos comuns:", related: "Relacionado:", seeAlso: "Ver também:", noMatch: "Sem correspondência rápida — a pesquisar no GitHub...", openIssue: "Abrir issue no GitHub", note: "(As respostas estão em inglês por precisão.)", searchAll: "🔎 Pesquisar em todos os issues do GitHub", searching: "A pesquisar no GitHub...", ghFound: "Issues correspondentes no GitHub:", ghNone: "Nenhum issue correspondente no GitHub.", ghError: "Pesquisa do GitHub ocupada (limite) — tente novamente em breve.", openDocs: "📖 Docs", copy: "Copiar", copied: "Copiado!", prefill: "Abrir issue pré-preenchido" },
    nl: { name: "Nederlands", title: "Support-assistent", sub: "antwoorden uit elk eerder probleem", placeholder: "Beschrijf je probleem...", ask: "Vraag", intro: "Hoi! Ik antwoord vanuit alle eerdere onderwerpen en kan alle GitHub-issues live doorzoeken. Vraag over installatiefouten, GPU niet gebruikt, Docker/NAS, API-tokens, modellen en meer.", common: "Veelvoorkomende onderwerpen:", related: "Gerelateerd:", seeAlso: "Zie ook:", noMatch: "Geen snelle match — GitHub doorzoeken...", openIssue: "GitHub-issue openen", note: "(Antwoorden zijn in het Engels voor nauwkeurigheid.)", searchAll: "🔎 Doorzoek alle GitHub-issues", searching: "GitHub doorzoeken...", ghFound: "Overeenkomende issues op GitHub:", ghNone: "Geen overeenkomende issues op GitHub.", ghError: "GitHub-zoeken is druk (limiet) — probeer zo opnieuw.", openDocs: "📖 Docs", copy: "Kopiëren", copied: "Gekopieerd!", prefill: "Vooraf ingevuld issue openen" },
    pl: { name: "Polski", title: "Asystent wsparcia", sub: "odpowiedzi z każdego wcześniejszego zgłoszenia", placeholder: "Opisz swój problem...", ask: "Zapytaj", intro: "Cześć! Odpowiadam ze wszystkich tematów wsparcia i mogę przeszukać wszystkie zgłoszenia GitHub na żywo. Pytaj o błędy instalacji, nieużywane GPU, Docker/NAS, tokeny API, modele i więcej.", common: "Częste tematy:", related: "Powiązane:", seeAlso: "Zobacz też:", noMatch: "Brak szybkiego dopasowania — przeszukuję GitHub...", openIssue: "Otwórz zgłoszenie na GitHub", note: "(Odpowiedzi są po angielsku dla dokładności.)", searchAll: "🔎 Przeszukaj wszystkie zgłoszenia GitHub", searching: "Przeszukiwanie GitHub...", ghFound: "Pasujące zgłoszenia na GitHub:", ghNone: "Brak pasujących zgłoszeń na GitHub.", ghError: "Wyszukiwarka GitHub zajęta (limit) — spróbuj za chwilę.", openDocs: "📖 Dokumentacja", copy: "Kopiuj", copied: "Skopiowano!", prefill: "Otwórz wstępnie wypełnione zgłoszenie" }
  };

  var LANG = (function () { try { return localStorage.getItem("sb-lang") || "en"; } catch (e) { return "en"; } })();
  if (!I18N[LANG]) LANG = "en";
  function t(k) { return (I18N[LANG] && I18N[LANG][k] != null) ? I18N[LANG][k] : I18N.en[k]; }

  var STOP = { the: 1, a: 1, an: 1, is: 1, are: 1, my: 1, i: 1, to: 1, of: 1, on: 1, in: 1, it: 1, and: 1, or: 1, for: 1, with: 1, how: 1, do: 1, does: 1, can: 1, why: 1, when: 1, me: 1, you: 1, not: 1, no: 1, get: 1, getting: 1 };

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
      .filter(function (r) { return r.s > 0; }).sort(function (a, b) { return b.s - a.s; }).map(function (r) { return r.e; });
  }

  // --- GitHub live search (public, CORS, no token) ---
  var ghCache = {};
  function searchGitHub(query, cb) {
    var key = query.toLowerCase();
    if (ghCache[key]) { cb(ghCache[key]); return; }
    var q = encodeURIComponent("repo:" + REPO + " is:issue " + query);
    fetch("https://api.github.com/search/issues?q=" + q + "&per_page=5&sort=updated", { headers: { Accept: "application/vnd.github+json" } })
      .then(function (r) { if (!r.ok) throw r.status; return r.json(); })
      .then(function (d) {
        var items = (d.items || []).map(function (it) { return { n: it.number, t: it.title, s: it.state, u: it.html_url }; });
        ghCache[key] = items; cb(items);
      })
      .catch(function () { cb(null); });
  }
  function issueUrl(q) {
    var body = "**Problem:**\n" + (q || "") + "\n\n**Plugin version:** v1.7.12\n**Docker image tag:** (e.g. docker7-intel)\n**Hardware / GPU:** \n**Jellyfin version:** \n**/gpu-verify output:** \n**Relevant logs:** ";
    return NEWISSUE + "?title=" + encodeURIComponent((q || "Support request").slice(0, 80)) + "&body=" + encodeURIComponent(body);
  }
  function issueCta(q) { return '<div class="sb-rel"><a href="' + esc(issueUrl(q)) + '" target="_blank" rel="noopener">' + esc(t("prefill")) + "</a></div>"; }
  function ghSearchChip() { return '<div class="sb-rel"><button class="sb-ghsearch" type="button">' + esc(t("searchAll")) + "</button></div>"; }

  function renderGh(items, query) {
    if (items === null) return '<div class="sb-note">' + esc(t("ghError")) + "</div>" + issueCta(query);
    if (!items.length) return '<div class="sb-note">' + esc(t("ghNone")) + "</div>" + issueCta(query);
    var html = "<strong>" + esc(t("ghFound")) + "</strong>";
    items.forEach(function (it) {
      var badge = '<span class="sb-badge sb-' + (it.s === "open" ? "op" : "cl") + '">' + esc(it.s) + "</span>";
      html += '<div class="sb-gh"><a href="' + esc(it.u) + '" target="_blank" rel="noopener">#' + it.n + " " + esc(it.t) + "</a> " + badge + "</div>";
    });
    return html + issueCta(query);
  }

  // ---- DOM ----
  var panel, msgs, input, langSel, head, lastQuery = "";

  function persist() {
    try {
      var arr = [].slice.call(msgs.children).map(function (d) { return { w: d.classList.contains("sb-user") ? "user" : "bot", h: d.innerHTML }; }).slice(-40);
      localStorage.setItem("sb-hist", JSON.stringify(arr));
    } catch (e) {}
  }
  function addMsg(html, who, skipPersist) {
    var d = document.createElement("div");
    d.className = "sb-msg sb-" + who; d.innerHTML = html;
    msgs.appendChild(d); msgs.scrollTop = msgs.scrollHeight;
    if (!skipPersist) persist();
    return d;
  }

  function issueLinks(entry) {
    var bits = [];
    if (DOC_MAP[entry.id]) bits.push('<a href="' + DOC_MAP[entry.id] + '" target="_blank" rel="noopener">' + esc(t("openDocs")) + "</a>");
    if (entry.issues && entry.issues.length) {
      bits.push(t("related") + " " + entry.issues.map(function (n) {
        return '<a href="https://github.com/' + REPO + "/issues/" + n + '" target="_blank" rel="noopener">#' + n + "</a>";
      }).join(" "));
    }
    return bits.length ? '<div class="sb-rel">' + bits.join(" &nbsp;·&nbsp; ") + "</div>" : "";
  }
  function answerFor(entry, alsoSee) {
    var html = "<strong>" + esc(entry.title) + "</strong><br>" + md(entry.answer);
    var note = t("note"); if (note) html += '<div class="sb-note">' + esc(note) + "</div>";
    html += issueLinks(entry);
    if (alsoSee && alsoSee.length) {
      html += '<div class="sb-rel">' + t("seeAlso") + " " + alsoSee.map(function (e) {
        return '<button class="sb-chip" data-id="' + e.id + '">' + esc(e.title) + "</button>";
      }).join(" ") + "</div>";
    }
    return html + ghSearchChip();
  }
  function chips(list) {
    return '<div class="sb-chips">' + list.map(function (e) { return '<button class="sb-chip" data-id="' + e.id + '">' + esc(e.title) + "</button>"; }).join("") + "</div>";
  }

  function doGitHub(query) {
    var typing = addMsg('<span class="sb-typing">' + esc(t("searching")) + "</span>", "bot", true);
    searchGitHub(query, function (items) { typing.innerHTML = renderGh(items, query); msgs.scrollTop = msgs.scrollHeight; persist(); });
  }
  function respond(query) {
    var hits = search(query);
    if (hits.length) { addMsg(answerFor(hits[0], hits.slice(1, 3)), "bot"); return; }
    addMsg(esc(t("noMatch")), "bot");
    doGitHub(query);
  }
  function send() {
    var q = input.value.trim(); if (!q) return;
    lastQuery = q; addMsg(esc(q), "user"); input.value = "";
    setTimeout(function () { respond(q); }, 120);
  }

  function buildCSS() {
    var css = ""
      + ".sb-fab{position:fixed;right:20px;bottom:20px;z-index:9998;width:56px;height:56px;border-radius:50%;border:0;cursor:pointer;background:#22d3ee;color:#06283d;font-size:24px;box-shadow:0 6px 20px rgba(0,0,0,.35)}"
      + ".sb-fab:hover{filter:brightness(1.08)}"
      + ".sb-panel{position:fixed;right:20px;bottom:88px;z-index:9999;width:380px;height:520px;max-width:calc(100vw - 32px);max-height:calc(100vh - 120px);min-width:300px;min-height:340px;display:none;flex-direction:column;background:#0f172a;color:#e2e8f0;border:1px solid #1e293b;border-radius:14px;box-shadow:0 12px 40px rgba(0,0,0,.5);overflow:hidden;font:14px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}"
      + ".sb-panel.open{display:flex}"
      + ".sb-grip{position:absolute;top:0;left:0;width:20px;height:20px;cursor:nwse-resize;z-index:3;background:linear-gradient(135deg,#22d3ee 0,#22d3ee 38%,transparent 38%);border-top-left-radius:14px;opacity:.55}"
      + ".sb-grip:hover{opacity:1}"
      + ".sb-head{display:flex;align-items:center;gap:8px;padding:12px 14px 12px 22px;background:#111c33;border-bottom:1px solid #1e293b}"
      + ".sb-head-t{display:flex;flex-direction:column;min-width:0}"
      + ".sb-head b{font-size:14px}.sb-head .sb-sub{color:#7d8aa3;font-size:11px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}"
      + ".sb-lang{margin-left:auto;background:#0b1220;color:#cbd5e1;border:1px solid #334155;border-radius:6px;font-size:11px;padding:3px 4px;cursor:pointer}"
      + ".sb-x{background:0;border:0;color:#7d8aa3;font-size:20px;cursor:pointer;line-height:1}"
      + ".sb-msgs{flex:1;overflow-y:auto;padding:14px;display:flex;flex-direction:column;gap:10px}"
      + ".sb-msg{padding:9px 12px;border-radius:10px;max-width:92%;word-wrap:break-word}"
      + ".sb-bot{background:#1e293b;align-self:flex-start;border-bottom-left-radius:3px}"
      + ".sb-user{background:#22d3ee;color:#06283d;align-self:flex-end;border-bottom-right-radius:3px}"
      + ".sb-msg code{background:#0b1220;padding:1px 5px;border-radius:4px;font-size:12px}"
      + ".sb-msg pre{position:relative;background:#0b1220;padding:9px;border-radius:6px;overflow-x:auto;margin:6px 0}.sb-msg pre code{background:0;padding:0}"
      + ".sb-msg a{color:#22d3ee}"
      + ".sb-copy{position:absolute;top:6px;right:6px;background:#22d3ee;color:#06283d;border:0;border-radius:5px;font-size:10px;padding:2px 7px;cursor:pointer;opacity:.85}.sb-copy:hover{opacity:1}"
      + ".sb-note{margin-top:6px;font-size:11px;color:#94a3b8;font-style:italic}"
      + ".sb-gh{margin:4px 0;font-size:13px}"
      + ".sb-badge{font-size:10px;padding:1px 7px;border-radius:999px;margin-left:4px;vertical-align:middle}"
      + ".sb-op{background:#16351f;color:#7ee2a8;border:1px solid #2f6b43}.sb-cl{background:#3a1f2b;color:#f0a0b8;border:1px solid #6b2f47}"
      + ".sb-typing{opacity:.7}"
      + ".sb-rel{margin-top:8px;font-size:12px;color:#7d8aa3}.sb-rel a{margin-right:2px}"
      + ".sb-chips{display:flex;flex-wrap:wrap;gap:6px;margin-top:8px}"
      + ".sb-chip,.sb-ghsearch{background:#0b1220;border:1px solid #334155;color:#cbd5e1;border-radius:999px;padding:5px 10px;font-size:12px;cursor:pointer;text-align:left}"
      + ".sb-chip:hover,.sb-ghsearch:hover{border-color:#22d3ee;color:#fff}"
      + ".sb-foot{display:flex;gap:8px;padding:10px;border-top:1px solid #1e293b;background:#111c33}"
      + ".sb-foot input{flex:1;background:#0b1220;border:1px solid #334155;color:#e2e8f0;border-radius:8px;padding:9px 10px;font-size:14px}"
      + ".sb-foot input:focus{outline:0;border-color:#22d3ee}"
      + ".sb-foot button{background:#22d3ee;color:#06283d;border:0;border-radius:8px;padding:0 14px;font-weight:600;cursor:pointer}";
    var st = document.createElement("style"); st.textContent = css; document.head.appendChild(st);
  }

  function applyLangChrome() {
    head.querySelector("b").textContent = t("title");
    head.querySelector(".sb-sub").textContent = t("sub");
    input.placeholder = t("placeholder");
    panel.querySelector(".sb-foot button").textContent = t("ask");
  }
  function enableResize(grip) {
    var sx, sy, sw, sh, active = false;
    function move(e) {
      if (!active) return;
      var w = Math.min(Math.max(sw + (sx - e.clientX), 300), window.innerWidth - 40);
      var h = Math.min(Math.max(sh + (sy - e.clientY), 340), window.innerHeight - 100);
      panel.style.width = w + "px"; panel.style.height = h + "px";
    }
    function up() { active = false; document.removeEventListener("pointermove", move); document.removeEventListener("pointerup", up); try { localStorage.setItem("sb-size", panel.style.width + "|" + panel.style.height); } catch (e) {} }
    grip.addEventListener("pointerdown", function (e) { active = true; sx = e.clientX; sy = e.clientY; sw = panel.offsetWidth; sh = panel.offsetHeight; document.addEventListener("pointermove", move); document.addEventListener("pointerup", up); e.preventDefault(); });
  }

  function build() {
    buildCSS();
    var fab = document.createElement("button");
    fab.className = "sb-fab"; fab.setAttribute("aria-label", "Open support assistant"); fab.innerHTML = "&#128172;";
    document.body.appendChild(fab);

    panel = document.createElement("div");
    panel.className = "sb-panel"; panel.setAttribute("role", "dialog"); panel.setAttribute("aria-label", "Support assistant");
    var opts = Object.keys(I18N).map(function (k) { return '<option value="' + k + '"' + (k === LANG ? " selected" : "") + ">" + esc(I18N[k].name) + "</option>"; }).join("");
    panel.innerHTML =
      '<div class="sb-grip" title="Drag to resize" aria-hidden="true"></div>'
      + '<div class="sb-head"><div class="sb-head-t"><b></b><span class="sb-sub"></span></div>'
      + '<select class="sb-lang" aria-label="Language">' + opts + "</select>"
      + '<button class="sb-x" aria-label="Close">&times;</button></div>'
      + '<div class="sb-msgs"></div>'
      + '<div class="sb-foot"><input type="text" aria-label="Your question"><button type="button"></button></div>';
    document.body.appendChild(panel);

    head = panel.querySelector(".sb-head");
    msgs = panel.querySelector(".sb-msgs");
    input = panel.querySelector(".sb-foot input");
    langSel = panel.querySelector(".sb-lang");
    applyLangChrome();

    try { var saved = localStorage.getItem("sb-size"); if (saved) { var sp = saved.split("|"); if (sp[0]) panel.style.width = sp[0]; if (sp[1]) panel.style.height = sp[1]; } } catch (e) {}
    enableResize(panel.querySelector(".sb-grip"));

    function restore() {
      try { var arr = JSON.parse(localStorage.getItem("sb-hist") || "[]"); if (arr.length) { arr.forEach(function (m) { addMsg(m.h, m.w, true); }); return true; } } catch (e) {}
      return false;
    }
    var opened = false;
    function open() {
      panel.classList.add("open");
      if (!opened) {
        opened = true;
        if (!restore()) {
          if (KB) {
            addMsg(esc(t("intro")), "bot", true);
            var common = ["gpu-on-cpu", "install-checksum", "api-token", "nas-setup", "docker-unreachable", "choose-model"]
              .map(function (id) { return KB.entries.filter(function (e) { return e.id === id; })[0]; }).filter(Boolean);
            addMsg(esc(t("common")) + chips(common), "bot", true);
          } else { addMsg("Loading...", "bot", true); }
        }
      }
      setTimeout(function () { input.focus(); }, 50);
    }
    function close() { panel.classList.remove("open"); }

    fab.addEventListener("click", function () { panel.classList.contains("open") ? close() : open(); });
    panel.querySelector(".sb-x").addEventListener("click", close);
    panel.querySelector(".sb-foot button").addEventListener("click", send);
    input.addEventListener("keydown", function (e) { if (e.key === "Enter") send(); });
    langSel.addEventListener("change", function () { LANG = I18N[langSel.value] ? langSel.value : "en"; try { localStorage.setItem("sb-lang", LANG); } catch (e) {} applyLangChrome(); });

    // Delegated clicks: topic chips, GitHub-search button, code copy.
    panel.addEventListener("click", function (e) {
      var chip = e.target.closest && e.target.closest(".sb-chip");
      if (chip) {
        var entry = KB && KB.entries.filter(function (x) { return x.id === chip.getAttribute("data-id"); })[0];
        if (entry) { addMsg(esc(entry.title), "user"); setTimeout(function () { addMsg(answerFor(entry, []), "bot"); }, 100); }
        return;
      }
      if (e.target.closest && e.target.closest(".sb-ghsearch")) { if (lastQuery) doGitHub(lastQuery); return; }
      var cp = e.target.closest && e.target.closest(".sb-copy");
      if (cp) {
        var pre = cp.closest("pre"); var code = pre && pre.querySelector("code");
        var txt = code ? code.textContent : (pre ? pre.textContent : "");
        if (navigator.clipboard) navigator.clipboard.writeText(txt);
        cp.textContent = t("copied"); setTimeout(function () { cp.textContent = t("copy"); }, 1200);
      }
    });

    // Inject copy buttons into any code block (delegation handles the click).
    var mo = new MutationObserver(function () {
      msgs.querySelectorAll("pre:not([data-cp])").forEach(function (pre) {
        pre.setAttribute("data-cp", "1");
        var b = document.createElement("button"); b.className = "sb-copy"; b.type = "button"; b.textContent = t("copy");
        pre.appendChild(b);
      });
    });
    mo.observe(msgs, { childList: true, subtree: true });
  }

  function load() {
    fetch("assets/support-kb.json", { cache: "no-cache" }).then(function (r) { return r.json(); }).then(function (d) { KB = d; }).catch(function () { KB = null; });
  }

  if (document.readyState === "loading") { document.addEventListener("DOMContentLoaded", function () { build(); load(); }); }
  else { build(); load(); }
})();
