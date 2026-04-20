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

  const current = (location.pathname.split('/').pop() || 'index.html').toLowerCase();
  const container = document.querySelector('[data-nav]');
  if (!container) return;

  const html = sections.map(section => {
    const items = section.items.map(item => {
      const active = item.href === current ? ' class="active"' : '';
      return `<a href="${item.href}"${active}>${item.label}</a>`;
    }).join('');
    return `<div class="nav-section">${section.label}</div>${items}`;
  }).join('');

  container.innerHTML = html;
})();
