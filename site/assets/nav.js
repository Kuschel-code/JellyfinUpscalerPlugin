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

  const container = document.querySelector('[data-nav]');
  if (container) {
    const navHtml = sections.map(section => {
      const items = section.items.map(item => {
        const active = item.href === current ? ' class="active"' : '';
        return `<a href="${item.href}"${active}>${item.label}</a>`;
      }).join('');
      return `<div class="nav-section">${section.label}</div>${items}`;
    }).join('');

    const opts = langs.map(([c, n]) => `<option value="${c}">${n}</option>`).join('');
    const langHtml =
      '<div class="nav-section">Language</div>' +
      '<div class="lang-row"><select id="lang-select" class="lang-select notranslate" translate="no" aria-label="Site language">' +
      opts + '</select></div>';

    container.innerHTML = navHtml + langHtml;

    const sel = container.querySelector('#lang-select');
    if (sel) {
      sel.value = currentLang();
      sel.addEventListener('change', function () { setLang(this.value); });
    }
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

// Load the client-side Support Assistant on every page (separate IIFE so it runs
// even when a page has no [data-nav] container).
(function () {
  var s = document.createElement('script');
  s.src = 'assets/support-bot.js';
  s.defer = true;
  document.head.appendChild(s);
})();
