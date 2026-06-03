/* Support Assistant — client-side, no backend, no API key.
 * Answers from site/assets/support-kb.json (every support topic we've handled).
 * Self-contained: injects its own CSS + DOM. Loaded site-wide via nav.js.
 * Default UI language: English. UI is switchable via the language dropdown.
 * Answer bodies stay English for technical accuracy (a localized note is shown
 * when another language is selected). The panel is resizable (drag the top-left grip).
 */
(function () {
  "use strict";
  if (window.__supportBotLoaded) return;
  window.__supportBotLoaded = true;

  var KB = null;

  // UI strings per language. Default = English. Answer bodies remain English.
  var I18N = {
    en: { name: "English", title: "Support Assistant", sub: "answers from every past issue", placeholder: "Describe your problem...", ask: "Ask", intro: "Hi! I answer from every support topic we've handled — ask about install errors, GPU not used, Docker/NAS setup, API tokens, models, and more.", common: "Common topics:", related: "Related:", seeAlso: "See also:", noMatch: "I couldn't find that among our past issues. Try different words, or open a new issue:", openIssue: "Open a GitHub issue", note: "" },
    de: { name: "Deutsch", title: "Support-Assistent", sub: "Antworten aus allen bisherigen Issues", placeholder: "Beschreibe dein Problem...", ask: "Fragen", intro: "Hi! Ich antworte aus allen bisherigen Support-Themen — frag zu Installationsfehlern, GPU wird nicht genutzt, Docker/NAS-Setup, API-Tokens, Modellen und mehr.", common: "Häufige Themen:", related: "Verwandt:", seeAlso: "Siehe auch:", noMatch: "Dazu habe ich in unseren bisherigen Issues nichts gefunden. Versuch andere Begriffe oder öffne ein neues Issue:", openIssue: "GitHub-Issue öffnen", note: "(Antworten sind aus Genauigkeitsgründen auf Englisch.)" },
    es: { name: "Español", title: "Asistente de soporte", sub: "respuestas de cada incidencia anterior", placeholder: "Describe tu problema...", ask: "Preguntar", intro: "¡Hola! Respondo a partir de todos los temas de soporte tratados: errores de instalación, GPU no usada, configuración Docker/NAS, tokens de API, modelos y más.", common: "Temas frecuentes:", related: "Relacionado:", seeAlso: "Ver también:", noMatch: "No lo encontré entre nuestras incidencias anteriores. Prueba otras palabras o abre una nueva incidencia:", openIssue: "Abrir incidencia en GitHub", note: "(Las respuestas están en inglés por precisión.)" },
    fr: { name: "Français", title: "Assistant de support", sub: "réponses tirées de tous les tickets passés", placeholder: "Décrivez votre problème...", ask: "Demander", intro: "Bonjour ! Je réponds à partir de tous les sujets de support traités : erreurs d'installation, GPU non utilisé, configuration Docker/NAS, jetons API, modèles, etc.", common: "Sujets fréquents :", related: "Connexe :", seeAlso: "Voir aussi :", noMatch: "Je n'ai rien trouvé dans nos tickets passés. Essayez d'autres mots ou ouvrez un nouveau ticket :", openIssue: "Ouvrir un ticket GitHub", note: "(Les réponses sont en anglais par souci de précision.)" },
    it: { name: "Italiano", title: "Assistente di supporto", sub: "risposte da ogni problema precedente", placeholder: "Descrivi il tuo problema...", ask: "Chiedi", intro: "Ciao! Rispondo da tutti gli argomenti di supporto trattati: errori di installazione, GPU non usata, configurazione Docker/NAS, token API, modelli e altro.", common: "Argomenti comuni:", related: "Correlato:", seeAlso: "Vedi anche:", noMatch: "Non l'ho trovato tra i problemi precedenti. Prova altre parole o apri una nuova issue:", openIssue: "Apri una issue su GitHub", note: "(Le risposte sono in inglese per precisione.)" },
    pt: { name: "Português", title: "Assistente de suporte", sub: "respostas de todos os problemas anteriores", placeholder: "Descreva o seu problema...", ask: "Perguntar", intro: "Olá! Respondo a partir de todos os tópicos de suporte tratados: erros de instalação, GPU não usada, configuração Docker/NAS, tokens de API, modelos e mais.", common: "Tópicos comuns:", related: "Relacionado:", seeAlso: "Ver também:", noMatch: "Não encontrei isso nos problemas anteriores. Tente outras palavras ou abra um novo issue:", openIssue: "Abrir issue no GitHub", note: "(As respostas estão em inglês por precisão.)" },
    nl: { name: "Nederlands", title: "Support-assistent", sub: "antwoorden uit elk eerder probleem", placeholder: "Beschrijf je probleem...", ask: "Vraag", intro: "Hoi! Ik antwoord vanuit alle eerdere supportonderwerpen: installatiefouten, GPU niet gebruikt, Docker/NAS-setup, API-tokens, modellen en meer.", common: "Veelvoorkomende onderwerpen:", related: "Gerelateerd:", seeAlso: "Zie ook:", noMatch: "Ik kon dit niet vinden in eerdere problemen. Probeer andere woorden of open een nieuw issue:", openIssue: "GitHub-issue openen", note: "(Antwoorden zijn in het Engels voor nauwkeurigheid.)" },
    pl: { name: "Polski", title: "Asystent wsparcia", sub: "odpowiedzi z każdego wcześniejszego zgłoszenia", placeholder: "Opisz swój problem...", ask: "Zapytaj", intro: "Cześć! Odpowiadam na podstawie wszystkich tematów wsparcia: błędy instalacji, nieużywane GPU, konfiguracja Docker/NAS, tokeny API, modele i więcej.", common: "Częste tematy:", related: "Powiązane:", seeAlso: "Zobacz też:", noMatch: "Nie znalazłem tego w poprzednich zgłoszeniach. Spróbuj innych słów lub otwórz nowe zgłoszenie:", openIssue: "Otwórz zgłoszenie na GitHub", note: "(Odpowiedzi są po angielsku dla dokładności.)" }
  };

  var LANG = (function () { try { return localStorage.getItem("sb-lang") || "en"; } catch (e) { return "en"; } })();
  if (!I18N[LANG]) LANG = "en";
  function t(k) { return (I18N[LANG] && I18N[LANG][k]) || I18N.en[k]; }

  var STOP = { the: 1, a: 1, an: 1, is: 1, are: 1, my: 1, i: 1, to: 1, of: 1, on: 1, in: 1, it: 1, and: 1, or: 1, for: 1, with: 1, how: 1, do: 1, does: 1, can: 1, why: 1, when: 1, me: 1, you: 1, not: 1, no: 1, get: 1, getting: 1 };

  function esc(s) {
    return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  // Minimal, safe markdown: escape first, then apply inline/blocks on escaped text.
  function md(text) {
    var parts = String(text).split(/```/);
    var out = "";
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

  function tokenize(s) {
    return String(s).toLowerCase().split(/[^a-z0-9.#+]+/).filter(function (w) { return w.length > 1 && !STOP[w]; });
  }
  function score(entry, tokens, raw) {
    var hay = (entry.keywords || []).join(" ").toLowerCase(), title = (entry.title || "").toLowerCase(), s = 0;
    for (var i = 0; i < tokens.length; i++) { var tk = tokens[i]; if (hay.indexOf(tk) !== -1) s += 3; if (title.indexOf(tk) !== -1) s += 2; }
    (entry.keywords || []).forEach(function (k) { if (k.indexOf(" ") !== -1 && raw.indexOf(k) !== -1) s += 5; });
    return s;
  }
  function search(query) {
    var raw = query.toLowerCase(), tokens = tokenize(query);
    if (!KB || !tokens.length) return [];
    return KB.entries.map(function (e) { return { e: e, s: score(e, tokens, raw) }; })
      .filter(function (r) { return r.s > 0; }).sort(function (a, b) { return b.s - a.s; })
      .map(function (r) { return r.e; });
  }

  var panel, msgs, input, langSel, head;

  function addMsg(html, who) {
    var d = document.createElement("div");
    d.className = "sb-msg sb-" + who; d.innerHTML = html;
    msgs.appendChild(d); msgs.scrollTop = msgs.scrollHeight; return d;
  }
  function issueLinks(entry) {
    if (!entry.issues || !entry.issues.length) return "";
    var base = KB.repo + "/issues/";
    return '<div class="sb-rel">' + t("related") + " " + entry.issues.map(function (n) {
      return '<a href="' + base + n + '" target="_blank" rel="noopener">#' + n + "</a>";
    }).join(" ") + "</div>";
  }
  function answerFor(entry, alsoSee) {
    var html = "<strong>" + esc(entry.title) + "</strong><br>" + md(entry.answer);
    var note = t("note");
    if (note) html += '<div class="sb-note">' + esc(note) + "</div>";
    html += issueLinks(entry);
    if (alsoSee && alsoSee.length) {
      html += '<div class="sb-rel">' + t("seeAlso") + " " + alsoSee.map(function (e) {
        return '<button class="sb-chip" data-id="' + e.id + '">' + esc(e.title) + "</button>";
      }).join(" ") + "</div>";
    }
    return html;
  }
  function respond(query) {
    var hits = search(query);
    if (!hits.length) {
      addMsg(esc(t("noMatch")) + '<br><a href="' + KB.newIssueUrl + '" target="_blank" rel="noopener">' + esc(t("openIssue")) + "</a>", "bot");
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
    var q = input.value.trim(); if (!q) return;
    addMsg(esc(q), "user"); input.value = "";
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
      + ".sb-msg pre{background:#0b1220;padding:9px;border-radius:6px;overflow-x:auto;margin:6px 0}.sb-msg pre code{background:0;padding:0}"
      + ".sb-msg a{color:#22d3ee}"
      + ".sb-note{margin-top:6px;font-size:11px;color:#94a3b8;font-style:italic}"
      + ".sb-rel{margin-top:8px;font-size:12px;color:#7d8aa3}.sb-rel a{margin-right:6px}"
      + ".sb-chips{display:flex;flex-wrap:wrap;gap:6px;margin-top:8px}"
      + ".sb-chip{background:#0b1220;border:1px solid #334155;color:#cbd5e1;border-radius:999px;padding:5px 10px;font-size:12px;cursor:pointer;text-align:left}"
      + ".sb-chip:hover{border-color:#22d3ee;color:#fff}"
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
      var maxW = window.innerWidth - 40, maxH = window.innerHeight - 100;
      var w = Math.min(Math.max(sw + (sx - e.clientX), 300), maxW);
      var h = Math.min(Math.max(sh + (sy - e.clientY), 340), maxH);
      panel.style.width = w + "px"; panel.style.height = h + "px";
    }
    function up() {
      active = false;
      document.removeEventListener("pointermove", move); document.removeEventListener("pointerup", up);
      try { localStorage.setItem("sb-size", panel.style.width + "|" + panel.style.height); } catch (e) {}
    }
    grip.addEventListener("pointerdown", function (e) {
      active = true; sx = e.clientX; sy = e.clientY; sw = panel.offsetWidth; sh = panel.offsetHeight;
      document.addEventListener("pointermove", move); document.addEventListener("pointerup", up);
      e.preventDefault();
    });
  }

  function build() {
    buildCSS();

    var fab = document.createElement("button");
    fab.className = "sb-fab"; fab.setAttribute("aria-label", "Open support assistant"); fab.innerHTML = "&#128172;";
    document.body.appendChild(fab);

    panel = document.createElement("div");
    panel.className = "sb-panel"; panel.setAttribute("role", "dialog"); panel.setAttribute("aria-label", "Support assistant");
    var opts = Object.keys(I18N).map(function (k) {
      return '<option value="' + k + '"' + (k === LANG ? " selected" : "") + ">" + esc(I18N[k].name) + "</option>";
    }).join("");
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

    try {
      var saved = localStorage.getItem("sb-size");
      if (saved) { var sp = saved.split("|"); if (sp[0]) panel.style.width = sp[0]; if (sp[1]) panel.style.height = sp[1]; }
    } catch (e) {}

    enableResize(panel.querySelector(".sb-grip"));

    var opened = false;
    function open() {
      panel.classList.add("open");
      if (!opened) {
        opened = true;
        if (KB) {
          addMsg(esc(t("intro")), "bot");
          var common = ["gpu-on-cpu", "install-checksum", "api-token", "nas-setup", "docker-unreachable", "choose-model"]
            .map(function (id) { return KB.entries.filter(function (e) { return e.id === id; })[0]; }).filter(Boolean);
          addMsg(esc(t("common")) + chips(common), "bot");
        } else { addMsg("Loading knowledge base...", "bot"); }
      }
      setTimeout(function () { input.focus(); }, 50);
    }
    function close() { panel.classList.remove("open"); }

    fab.addEventListener("click", function () { panel.classList.contains("open") ? close() : open(); });
    panel.querySelector(".sb-x").addEventListener("click", close);
    panel.querySelector(".sb-foot button").addEventListener("click", send);
    input.addEventListener("keydown", function (e) { if (e.key === "Enter") send(); });
    langSel.addEventListener("change", function () {
      LANG = I18N[langSel.value] ? langSel.value : "en";
      try { localStorage.setItem("sb-lang", LANG); } catch (e) {}
      applyLangChrome();
    });
    panel.addEventListener("click", function (e) {
      var b = e.target.closest && e.target.closest(".sb-chip");
      if (!b) return;
      var entry = KB.entries.filter(function (x) { return x.id === b.getAttribute("data-id"); })[0];
      if (entry) { addMsg(esc(entry.title), "user"); setTimeout(function () { addMsg(answerFor(entry, []), "bot"); }, 100); }
    });
  }

  function load() {
    fetch("assets/support-kb.json", { cache: "no-cache" })
      .then(function (r) { return r.json(); }).then(function (d) { KB = d; }).catch(function () { KB = null; });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () { build(); load(); });
  } else { build(); load(); }
})();
