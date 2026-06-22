(function () {
  const sections = [
    {
      label: 'Get started',
      items: [
        { href: 'index.html',        label: 'Overview' },
        { href: 'installation.html', label: 'Installation' },
        { href: 'configuration.html',label: 'Configuration' }
      ]
    },
    {
      label: 'Reference',
      items: [
        { href: 'features.html',     label: 'Features' },
        { href: 'models.html',       label: 'Model catalog' },
        { href: 'hardware.html',     label: 'Hardware guide' },
        { href: 'architecture.html', label: 'Architecture' },
        { href: 'api.html',          label: 'REST API' }
      ]
    },
    {
      label: 'Operations',
      items: [
        { href: 'deployment.html',    label: 'Deployment' },
        { href: 'troubleshooting.html', label: 'Troubleshooting' },
        { href: 'security.html',      label: 'Security' }
      ]
    },
    {
      label: 'Project',
      items: [
        { href: 'roadmap.html',      label: 'Roadmap' },
        { href: 'contributing.html', label: 'Contributing' },
        { href: 'changelog.html',    label: 'Changelog' }
      ]
    }
  ];

  // Top world languages by number of speakers (native names). 'en' = original.
  const langs = [
    ['en', 'English'], ['zh-CN', '中文'], ['hi', 'हिन्दी'], ['es', 'Español'],
    ['fr', 'Français'], ['ar', 'العربية'], ['bn', 'বাংলা'], ['pt', 'Português'],
    ['ru', 'Русский'], ['ur', 'اردو'], ['id', 'Indonesia'], ['de', 'Deutsch'], ['ja', '日本語']
  ];

  const current = (location.pathname.split('/').pop() || 'index.html').toLowerCase();

  function currentLang() {
    const m = document.cookie.match(/(?:^|;\s*)googtrans=\/[^/]+\/([^;]+)/);
    return m ? decodeURIComponent(m[1]) : 'en';
  }
  function setLang(l) {
    const h = location.hostname, exp = 'expires=Thu, 01 Jan 1970 00:00:00 GMT';
    if (!l || l === 'en') {
      document.cookie = 'googtrans=;path=/;' + exp;
      document.cookie = 'googtrans=;path=/;domain=' + h + ';' + exp;
    } else {
      document.cookie = 'googtrans=/en/' + l + ';path=/';
      document.cookie = 'googtrans=/en/' + l + ';path=/;domain=' + h;
    }
    location.reload();
  }

  /* ==================================================================
     Hamburger + full-screen overlay navigation (replaces the sidebar).
     Built in JS so all pages get it from the shared asset — no per-page
     HTML edits. The legacy <nav class="nav" data-nav> is left empty and
     hidden via CSS.
     ================================================================== */

  // numbered link list, running 01..N across every section
  let n = 0;
  const groupsHtml = sections.map(section => {
    const links = section.items.map(item => {
      n++;
      const num = String(n).padStart(2, '0');
      const active = item.href === current ? ' aria-current="page"' : '';
      return `<a class="navov-link" href="${item.href}"${active}>` +
             `<span class="navov-num">${num}</span><span class="navov-text">${item.label}</span></a>`;
    }).join('');
    return `<div class="navov-group"><span class="navov-label">${section.label}</span>${links}</div>`;
  }).join('');

  const opts = langs.map(([c, nm]) => `<option value="${c}">${nm}</option>`).join('');

  const overlay = document.createElement('div');
  overlay.className = 'navov';
  overlay.id = 'site-nav';
  overlay.setAttribute('aria-hidden', 'true');
  overlay.innerHTML =
    '<div class="navov-scrim" data-close></div>' +
    '<div class="navov-panel" role="dialog" aria-modal="true" aria-label="Site navigation">' +
      '<nav class="navov-nav">' + groupsHtml + '</nav>' +
      '<div class="navov-foot">' +
        '<div class="navov-lang">' +
          '<span class="navov-label">Language</span>' +
          '<select id="lang-select" class="lang-select notranslate" translate="no" aria-label="Site language">' +
          opts + '</select>' +
        '</div>' +
        '<a class="navov-gh" href="https://github.com/Kuschel-code/JellyfinUpscalerPlugin" target="_blank" rel="noopener">GitHub &#8599;</a>' +
      '</div>' +
    '</div>';
  document.body.appendChild(overlay);

  // hamburger toggle, injected into the slim topbar
  const hamb = document.createElement('button');
  hamb.className = 'hamb';
  hamb.id = 'nav-toggle';
  hamb.type = 'button';
  hamb.setAttribute('aria-label', 'Open menu');
  hamb.setAttribute('aria-expanded', 'false');
  hamb.setAttribute('aria-controls', 'site-nav');
  hamb.innerHTML = '<span></span><span></span><span></span>';

  const bar = document.querySelector('.topbar-inner') || document.querySelector('.topbar');
  if (bar) bar.insertBefore(hamb, bar.firstChild);
  else document.body.appendChild(hamb);

  let open = false;
  function setOpen(v) {
    open = v;
    document.body.classList.toggle('nav-open', v);
    overlay.setAttribute('aria-hidden', v ? 'false' : 'true');
    hamb.setAttribute('aria-expanded', v ? 'true' : 'false');
    hamb.setAttribute('aria-label', v ? 'Close menu' : 'Open menu');
    if (v) {
      const first = overlay.querySelector('.navov-link');
      if (first) first.focus({ preventScroll: true });
    } else {
      hamb.focus({ preventScroll: true });
    }
  }
  hamb.addEventListener('click', () => setOpen(!open));
  overlay.addEventListener('click', e => { if (e.target.hasAttribute('data-close')) setOpen(false); });
  overlay.querySelectorAll('.navov-link').forEach(a => a.addEventListener('click', () => setOpen(false)));
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && open) setOpen(false); });

  // language selector lives inside the overlay
  const sel = overlay.querySelector('#lang-select');
  if (sel) {
    sel.value = currentLang();
    sel.addEventListener('change', function () { setLang(this.value); });
  }

  // Keep code blocks, the brand and version strings out of machine translation.
  function shieldFromTranslate() {
    document.querySelectorAll('pre, code, .brand-logo, .brand-version, .notranslate').forEach(function (el) {
      el.classList.add('notranslate');
      el.setAttribute('translate', 'no');
    });
  }
  shieldFromTranslate();

  // Google Website Translate — applies the language chosen above (cookie-driven,
  // so the choice persists across pages). The widget chrome is hidden via CSS.
  window.googleTranslateElementInit = function () {
    /* global google */
    new google.translate.TranslateElement({
      pageLanguage: 'en',
      includedLanguages: langs.map(l => l[0]).join(','),
      autoDisplay: false
    }, 'google_translate_element');
  };
  const gtDiv = document.createElement('div');
  gtDiv.id = 'google_translate_element';
  document.body.appendChild(gtDiv);
  const gtScript = document.createElement('script');
  gtScript.src = '//translate.google.com/translate_a/element.js?cb=googleTranslateElementInit';
  gtScript.defer = true;
  document.head.appendChild(gtScript);
})();

// Load the cursor-reactive dot-grid background on every page (the signature
// backdrop). Self-contained: dotgrid.js creates its own <canvas.dotfield>.
(function () {
  var s = document.createElement('script');
  s.src = 'assets/dotgrid.js';
  s.defer = true;
  document.head.appendChild(s);
})();

// Load the client-side Support Assistant on every page (separate IIFE so it runs
// even when a page has no [data-nav] container).
(function () {
  var s = document.createElement('script');
  s.src = 'assets/support-bot.js';
  s.defer = true;
  document.head.appendChild(s);
})();
