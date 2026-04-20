(function () {
  const nav = [
    { href: 'index.html', label: 'Home' },
    { href: 'installation.html', label: 'Install' },
    { href: 'configuration.html', label: 'Config' },
    { href: 'features.html', label: 'Features' },
    { href: 'models.html', label: 'Models' },
    { href: 'hardware.html', label: 'Hardware' },
    { href: 'architecture.html', label: 'Architecture' },
    { href: 'deployment.html', label: 'Deploy' },
    { href: 'api.html', label: 'API' },
    { href: 'troubleshooting.html', label: 'Troubleshoot' },
    { href: 'security.html', label: 'Security' },
    { href: 'roadmap.html', label: 'Roadmap' },
    { href: 'contributing.html', label: 'Contribute' },
    { href: 'changelog.html', label: 'Changelog' }
  ];

  const current = (location.pathname.split('/').pop() || 'index.html').toLowerCase();
  const container = document.querySelector('[data-nav]');
  if (!container) return;

  container.innerHTML = nav.map(item => {
    const active = item.href === current ? ' class="active"' : '';
    return `<a href="${item.href}"${active}>${item.label}</a>`;
  }).join('');
})();
