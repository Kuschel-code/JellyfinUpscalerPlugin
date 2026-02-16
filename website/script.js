/* ========================================
   App State
======================================== */
let currentLang = localStorage.getItem('lang') || 'en';
let currentPage = 'home';
const MANIFEST_URL = 'https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/manifest.json';
const flags = { en: '🇬🇧', de: '🇩🇪', fr: '🇫🇷', zh: '🇨🇳', ru: '🇷🇺', ja: '🇯🇵' };

/* ========================================
   SVG Icons
======================================== */
const icons = {
    home: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>',
    installation: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>',
    configuration: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>',
    features: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>',
    troubleshooting: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>',
    dockerTags: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="7" width="20" height="14" rx="2"/><path d="M12 11v-4"/><path d="M8 7v-2"/><path d="M16 7v-2"/></svg>',
    changelog: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>',
    copy: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>',
    check: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>',
    checkCircle: '<svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>',
    warning: '<svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
    chevronDown: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>',
    arrow: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>',
    terminal: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/></svg>',
    docker: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="7" width="20" height="14" rx="2"/><path d="M12 11v-4"/></svg>',
    ssh: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/></svg>',
    sshSetup: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="M7 15l3-3-3-3"/><line x1="13" y1="15" x2="17" y2="15"/></svg>',
    gpu: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><line x1="9" y1="1" x2="9" y2="4"/><line x1="15" y1="1" x2="15" y2="4"/><line x1="9" y1="20" x2="9" y2="23"/><line x1="15" y1="20" x2="15" y2="23"/><line x1="20" y1="9" x2="23" y2="9"/><line x1="20" y1="14" x2="23" y2="14"/><line x1="1" y1="9" x2="4" y2="9"/><line x1="1" y1="14" x2="4" y2="14"/></svg>',
    ai: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/><path d="M2 12l10 5 10-5"/></svg>',
    ui: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><line x1="3" y1="9" x2="21" y2="9"/><line x1="9" y1="21" x2="9" y2="9"/></svg>',
    key: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"/></svg>',
    link: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/></svg>',
    folder: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>'
};

/* ========================================
   Helpers
======================================== */
function t() { return i18n[currentLang]; }

function copyText(text, btn) {
    navigator.clipboard.writeText(text);
    btn.innerHTML = icons.check;
    btn.classList.add('copied');
    setTimeout(() => { btn.innerHTML = icons.copy; btn.classList.remove('copied'); }, 2000);
}

function escapeHtml(s) {
    const div = document.createElement('div');
    div.textContent = s;
    return div.innerHTML;
}

/* ========================================
   Navigation
======================================== */
const navItems = [
    { id: 'home', icon: 'home' },
    { id: 'installation', icon: 'installation' },
    { id: 'sshSetup', icon: 'sshSetup' },
    { id: 'configuration', icon: 'configuration' },
    { id: 'features', icon: 'features' },
    { id: 'troubleshooting', icon: 'troubleshooting' },
    { id: 'dockerTags', icon: 'dockerTags' },
    { id: 'changelog', icon: 'changelog' }
];

function renderNav() {
    const nav = document.getElementById('sidebarNav');
    nav.innerHTML = navItems.map(item =>
        `<a href="#${item.id}" class="nav-link ${currentPage === item.id ? 'active' : ''}" data-page="${item.id}">
            ${icons[item.icon]}
            <span>${t().nav[item.id]}</span>
        </a>`
    ).join('');
}

/* ========================================
   Page Renderers
======================================== */

// HOME
function renderHome() {
    const h = t().hero;
    const f = t().features;
    return `
        <div class="hero-section fade-in">
            <div class="hero-badge">${escapeHtml(h.badge)}</div>
            <h1 class="hero-title">${escapeHtml(h.title1)}<br><span class="gradient">${escapeHtml(h.title2)}</span></h1>
            <p class="hero-subtitle">${escapeHtml(h.subtitle)}</p>
            <div class="hero-buttons">
                <a href="#installation" class="btn-primary">${escapeHtml(h.getStarted)} ${icons.arrow}</a>
                <a href="https://github.com/Kuschel-code/JellyfinUpscalerPlugin" target="_blank" class="btn-secondary">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>
                    ${escapeHtml(h.viewGithub)}
                </a>
            </div>
            <div class="hero-stats">
                <div class="stat-item"><div class="stat-value">5</div><div class="stat-label">${escapeHtml(h.stats.gpus)}</div></div>
                <div class="stat-item"><div class="stat-value">1.6 MB</div><div class="stat-label">${escapeHtml(h.stats.size)}</div></div>
                <div class="stat-item"><div class="stat-value">4x</div><div class="stat-label">${escapeHtml(h.stats.upscale)}</div></div>
                <div class="stat-item"><div class="stat-value">MIT</div><div class="stat-label">${escapeHtml(h.stats.license)}</div></div>
            </div>
        </div>

        <div class="page-header fade-in delay-2" style="margin-top:60px">
            <div class="page-tag">${escapeHtml(f.tag)}</div>
            <h2 class="page-title">${escapeHtml(f.title1)} <span class="gradient">${escapeHtml(f.title2)}</span></h2>
        </div>
        <div class="features-grid">
            ${[
            { k: 'docker', ic: icons.docker },
            { k: 'ssh', ic: icons.ssh },
            { k: 'gpu', ic: icons.gpu },
            { k: 'ai', ic: icons.ai },
            { k: 'ui', ic: icons.ui }
        ].map((item, i) => `
                <div class="feature-card fade-in delay-${i + 1}">
                    <div class="feature-icon">${item.ic}</div>
                    <h3>${escapeHtml(f[item.k].title)}</h3>
                    <p>${escapeHtml(f[item.k].desc)}</p>
                </div>
            `).join('')}
        </div>
        <div class="page-footer fade-in delay-5">${t().footer.copyright}</div>
    `;
}

// INSTALLATION
function renderInstallation() {
    const inst = t().installation;
    const cmds = {
        hub: 'docker pull kuscheltier/jellyfin-ai-upscaler:latest\ndocker run -d \\\n  --name jellyfin-ai-upscaler \\\n  -p 5000:5000 -p 2222:22 \\\n  kuscheltier/jellyfin-ai-upscaler:1.5.2',
        local: 'git clone https://github.com/Kuschel-code/JellyfinUpscalerPlugin\ncd JellyfinUpscalerPlugin/docker-ai-service\ndocker build -t jellyfin-ai-upscaler .',
        gpu: 'docker run -d \\\n  --name jellyfin-ai-upscaler \\\n  --gpus all \\\n  -p 5000:5000 -p 2222:22 \\\n  kuscheltier/jellyfin-ai-upscaler:1.5.2',
        intel: 'docker run -d \\\n  --name jellyfin-ai-upscaler \\\n  --device=/dev/dri \\\n  -p 5000:5000 -p 2222:22 \\\n  kuscheltier/jellyfin-ai-upscaler:1.5.2-intel',
        amd: 'docker run -d \\\n  --name jellyfin-ai-upscaler \\\n  --device=/dev/kfd --device=/dev/dri \\\n  -p 5000:5000 -p 2222:22 \\\n  kuscheltier/jellyfin-ai-upscaler:1.5.2-amd'
    };

    return `
        <div class="page-header fade-in">
            <div class="page-tag">${escapeHtml(inst.tag)}</div>
            <h2 class="page-title">${escapeHtml(inst.title1)} <span class="gradient">${escapeHtml(inst.title2)}</span></h2>
        </div>

        <div class="warning-box fade-in delay-1">
            <div class="warning-icon">${icons.warning}</div>
            <div>
                <h3>${escapeHtml(inst.warning)}</h3>
                <p>${escapeHtml(inst.warningText)}</p>
            </div>
        </div>

        <div class="step-section fade-in delay-2">
            <div class="step-header">
                <div class="step-number">1</div>
                <h2>${icons.docker} ${escapeHtml(inst.step1)}</h2>
            </div>
            <div style="margin-left:56px">
                ${codeBlock(inst.recommended, inst.optionA, cmds.hub, 'green')}
                ${codeBlock(null, inst.optionB, cmds.local)}
                ${codeBlock('NVIDIA GPU', inst.withGpu, cmds.gpu, 'purple')}
                ${codeBlock('Intel GPU', inst.withIntel || 'With Intel GPU', cmds.intel, '#0071c5')}
                ${codeBlock('AMD GPU', inst.withAmd || 'With AMD GPU', cmds.amd, '#ed1c24')}
                <div class="tip-box">
                    <span class="tip-label">${escapeHtml(inst.tip)}</span> ${escapeHtml(inst.tipText)}
                    <code>http://YOUR_SERVER_IP:5000</code>
                </div>
            </div>
        </div>

        <div class="step-section fade-in delay-3">
            <div class="step-header">
                <div class="step-number">2</div>
                <h2>${icons.installation} ${escapeHtml(inst.step2)}</h2>
            </div>
            <div style="margin-left:56px">
                <div class="ordered-steps">
                    <div class="ordered-step">
                        <div class="ordered-step-num">1</div>
                        <div class="ordered-step-content">
                            <h4>${escapeHtml(inst.addRepo)}</h4>
                            <p>${escapeHtml(inst.addRepoPath)}</p>
                            <div class="inline-code">
                                <span style="flex:1;overflow-x:auto">${escapeHtml(MANIFEST_URL)}</span>
                                <button class="inline-copy-btn" onclick="copyText('${MANIFEST_URL}', this)">${icons.copy}</button>
                            </div>
                        </div>
                    </div>
                    <div class="ordered-step">
                        <div class="ordered-step-num">2</div>
                        <div class="ordered-step-content">
                            <h4>${escapeHtml(inst.installPlugin)}</h4>
                            <p>${escapeHtml(inst.installPluginPath)}</p>
                        </div>
                    </div>
                    <div class="ordered-step">
                        <div class="ordered-step-num">3</div>
                        <div class="ordered-step-content">
                            <h4>${escapeHtml(inst.restartJellyfin)}</h4>
                            <p>${escapeHtml(inst.restartText)}</p>
                        </div>
                    </div>
                    <div class="ordered-step">
                        <div class="ordered-step-num">4</div>
                        <div class="ordered-step-content">
                            <h4>${escapeHtml(inst.configureUrl)}</h4>
                            <p>${escapeHtml(inst.configureUrlText)} <code style="padding:2px 8px;background:var(--bg-950);border-radius:4px;color:#c4b5fd;font-size:12px">http://YOUR_SERVER_IP:5000</code></p>
                        </div>
                    </div>
                </div>
            </div>
        </div>

        <div class="success-box fade-in delay-4">
            <div class="success-icon">${icons.checkCircle}</div>
            <div>
                <h3>${escapeHtml(inst.done)}</h3>
                <p>${escapeHtml(inst.doneText)}</p>
            </div>
        </div>

        <div class="nav-buttons fade-in delay-5">
            <a href="#configuration" class="btn-primary">${escapeHtml(t().nav.configuration)} ${icons.arrow}</a>
            <a href="#troubleshooting" class="btn-secondary">${escapeHtml(t().nav.troubleshooting)} ${icons.arrow}</a>
        </div>
        <div class="page-footer">${t().footer.copyright}</div>
    `;
}

function codeBlock(badge, label, code, badgeColor) {
    const id = 'cb-' + Math.random().toString(36).substr(2, 6);
    return `
        <div class="code-block">
            <div class="code-header">
                <div class="code-label">
                    ${badge ? `<span class="code-badge ${badgeColor || ''}">${escapeHtml(badge)}</span>` : ''}
                    <span>${escapeHtml(label)}</span>
                </div>
                <button class="copy-btn" onclick="copyText(\`${code.replace(/`/g, '\\`')}\`, this)">${icons.copy}</button>
            </div>
            <div class="code-content">${escapeHtml(code)}</div>
        </div>
    `;
}

// CONFIGURATION
function renderConfiguration() {
    const c = t().configuration;
    const f = c.fields;

    const groups = [
        {
            title: c.basic, icon: icons.configuration, rows: [
                { label: f.enable, type: 'toggle' },
                { label: f.serviceUrl, value: 'http://localhost:5000' },
                { label: f.model, value: 'FSRCNN' },
                { label: f.scale, value: '2x' },
                { label: f.quality, value: 'High' }
            ]
        },
        {
            title: c.hardware, icon: icons.gpu, rows: [
                { label: f.hwAccel, value: 'CUDA' },
                { label: f.maxVram, value: '4096' },
                { label: f.cpuThreads, value: '4' }
            ]
        },
        {
            title: c.remote, icon: icons.ssh, rows: [
                { label: f.enableRemote, type: 'toggle' },
                { label: f.remoteHost, value: '192.168.1.100' },
                { label: f.sshPort, value: '2222' },
                { label: f.sshUser, value: 'root' },
                { label: f.sshKey, value: '/root/.ssh/id_rsa' },
                { label: f.localPath, value: '/media' },
                { label: f.remotePath, value: '/media' }
            ]
        },
        {
            title: c.ui, icon: icons.ui, rows: [
                { label: f.showButton, type: 'toggle' },
                { label: f.buttonPos, value: 'Bottom Right' },
                { label: f.notifications, type: 'toggle' }
            ]
        },
        {
            title: c.advanced, icon: icons.configuration, rows: [
                { label: f.comparison, type: 'toggle' },
                { label: f.metrics, type: 'toggle' },
                { label: f.cache, type: 'toggle' },
                { label: f.cacheSize, value: '512' }
            ]
        }
    ];

    return `
        <div class="page-header fade-in">
            <div class="page-tag">${escapeHtml(c.tag)}</div>
            <h2 class="page-title">${escapeHtml(c.title1)} <span class="gradient">${escapeHtml(c.title2)}</span></h2>
        </div>
        <div class="config-sections">
            ${groups.map((g, gi) => `
                <div class="config-group fade-in delay-${gi + 1}">
                    <div class="config-group-header">${g.icon} ${escapeHtml(g.title)}</div>
                    <div class="config-list">
                        ${g.rows.map(r => `
                            <div class="config-row">
                                <span class="config-label">${escapeHtml(r.label)}</span>
                                ${r.type === 'toggle'
            ? '<button class="config-toggle" disabled></button>'
            : `<span class="config-value">${escapeHtml(r.value)}</span>`}
                            </div>
                        `).join('')}
                    </div>
                </div>
            `).join('')}
        </div>
        <div class="page-footer">${t().footer.copyright}</div>
    `;
}

// FEATURES
function renderFeatures() {
    const f = t().features;
    return `
        <div class="page-header fade-in">
            <div class="page-tag">${escapeHtml(f.tag)}</div>
            <h2 class="page-title">${escapeHtml(f.title1)} <span class="gradient">${escapeHtml(f.title2)}</span></h2>
        </div>
        <div class="features-grid">
            ${[
            { k: 'docker', ic: icons.docker },
            { k: 'ssh', ic: icons.ssh },
            { k: 'gpu', ic: icons.gpu },
            { k: 'ai', ic: icons.ai },
            { k: 'ui', ic: icons.ui }
        ].map((item, i) => `
                <div class="feature-card fade-in delay-${i + 1}">
                    <div class="feature-icon">${item.ic}</div>
                    <h3>${escapeHtml(f[item.k].title)}</h3>
                    <p>${escapeHtml(f[item.k].desc)}</p>
                </div>
            `).join('')}
        </div>
        <div class="page-footer">${t().footer.copyright}</div>
    `;
}

// TROUBLESHOOTING
function renderTroubleshooting() {
    const ts = t().troubleshooting;
    return `
        <div class="page-header fade-in">
            <div class="page-tag">${escapeHtml(ts.tag)}</div>
            <h2 class="page-title">${escapeHtml(ts.title1)} <span class="gradient">${escapeHtml(ts.title2)}</span></h2>
        </div>
        <div class="accordion fade-in delay-1">
            ${ts.problems.map((p, i) => `
                <div class="accordion-item" data-idx="${i}">
                    <button class="accordion-trigger" onclick="toggleAccordion(${i})">
                        <div class="accordion-dot"></div>
                        <div class="accordion-trigger-text">
                            <h3>${escapeHtml(p.title)}</h3>
                            <p>${escapeHtml(p.desc)}</p>
                        </div>
                        <span class="accordion-chevron">${icons.chevronDown}</span>
                    </button>
                    <div class="accordion-body">
                        <div class="solution-title">${escapeHtml(ts.solution)}</div>
                        <ol class="solution-list">
                            ${p.solutions.map((s, si) => `
                                <li><span class="solution-num">${si + 1}</span> ${escapeHtml(s)}</li>
                            `).join('')}
                        </ol>
                        ${p.commands ? `
                            <div class="cmd-section">
                                <div class="cmd-title">${icons.terminal} ${escapeHtml(ts.commands)}</div>
                                ${p.commands.map(cmd => `
                                    <div class="code-block" style="margin-bottom:8px">
                                        <div class="code-header">
                                            <span style="color:var(--text-400);font-size:12px">${escapeHtml(cmd.label)}</span>
                                            <button class="copy-btn" onclick="copyText('${cmd.code.replace(/'/g, "\\'")}', this)">${icons.copy}</button>
                                        </div>
                                        <div class="code-content" style="padding:12px 20px">${escapeHtml(cmd.code)}</div>
                                    </div>
                                `).join('')}
                            </div>
                        ` : ''}
                    </div>
                </div>
            `).join('')}
        </div>
        <div class="help-box fade-in delay-3">
            <h3>${escapeHtml(ts.needHelp)}</h3>
            <div class="help-links">
                <a href="https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues" target="_blank" class="help-link">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>
                    ${escapeHtml(ts.githubIssues)}
                </a>
                <a href="https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki" target="_blank" class="help-link">
                    ${escapeHtml(ts.githubWiki)}
                </a>
            </div>
        </div>
        <div class="page-footer">${t().footer.copyright}</div>
    `;
}

function toggleAccordion(idx) {
    const items = document.querySelectorAll('.accordion-item');
    items.forEach((item, i) => {
        if (i === idx) item.classList.toggle('open');
        else item.classList.remove('open');
    });
}

// DOCKER TAGS
function renderDockerTags() {
    const dt = t().dockerTags;
    return `
        <div class="page-header fade-in">
            <div class="page-tag">${escapeHtml(dt.tag)}</div>
            <h2 class="page-title">${escapeHtml(dt.title1)} <span class="gradient">${escapeHtml(dt.title2)}</span></h2>
        </div>
        <div class="docker-grid">
            ${dt.cards.map((card, i) => `
                <div class="docker-card fade-in delay-${i + 1}" style="--card-color:${card.color}">
                    <div style="position:absolute;top:0;left:0;right:0;height:3px;background:${card.color};"></div>
                    <div class="docker-brand" style="color:${card.color}">${escapeHtml(card.brand)}</div>
                    <div class="docker-tech">${escapeHtml(card.tech)}</div>
                    <div class="docker-tag">kuscheltier/jellyfin-ai-upscaler${card.tag}</div>
                    <div class="docker-models">${escapeHtml(card.models)}</div>
                    <div class="docker-rating">${'★'.repeat(card.rating).split('').map(() => '<span class="star">★</span>').join('')}${'★'.repeat(5 - card.rating).split('').map(() => '<span class="star empty">★</span>').join('')}</div>
                </div>
            `).join('')}
        </div>
        <div class="docker-pull-section fade-in delay-5">
            <h3>Quick Pull</h3>
            ${codeBlock('NVIDIA', 'Default Tag', 'docker pull kuscheltier/jellyfin-ai-upscaler:1.5.2', 'green')}
        </div>
        <div class="page-footer">${t().footer.copyright}</div>
    `;
}

// CHANGELOG
function renderChangelog() {
    const cl = t().changelog;
    const typeMap = { Hotfix: 'hotfix', Feature: 'feature', Major: 'major', 'Majeur': 'major', 'Мажорный': 'major', 'メジャー': 'major', '重大': 'major', Stable: 'stable', 'Stabil': 'stable', 'Correctif': 'hotfix', 'Исправ.': 'hotfix', '修正': 'hotfix', '修复': 'hotfix' };
    return `
        <div class="page-header fade-in">
            <div class="page-tag">${escapeHtml(cl.tag)}</div>
            <h2 class="page-title">${escapeHtml(cl.title1)} <span class="gradient">${escapeHtml(cl.title2)}</span></h2>
        </div>
        <div class="changelog-list">
            ${cl.versions.map((v, i) => `
                <div class="changelog-item fade-in delay-${Math.min(i + 1, 5)}">
                    <div class="changelog-meta">
                        <div class="changelog-ver">${escapeHtml(v.ver)}</div>
                        <div class="changelog-date">${escapeHtml(v.date)}</div>
                    </div>
                    <div class="changelog-dot"></div>
                    <div class="changelog-content">
                        <span class="changelog-type ${typeMap[v.type] || 'feature'}">${escapeHtml(v.type)}</span>
                        <ul class="changelog-items">
                            ${v.items.map(item => `<li>${escapeHtml(item)}</li>`).join('')}
                        </ul>
                    </div>
                </div>
            `).join('')}
        </div>
        <div class="page-footer">${t().footer.copyright}</div>
    `;
}

// SSH SETUP
function renderSshSetup() {
    const s = t().sshSetup;
    const cmds = {
        startContainer: 'docker run -d \\\
  --name jellyfin-ai-upscaler \\\
  --gpus all \\\
  -p 5000:5000 \\\
  -p 2222:22 \\\
  kuscheltier/jellyfin-ai-upscaler:1.5.2',
        genKey: 'ssh-keygen -t ed25519 -C "jellyfin-upscaler" -f ~/.ssh/jellyfin_upscaler',
        genKeyWin: 'ssh-keygen -t ed25519 -C "jellyfin-upscaler" -f %USERPROFILE%\\.ssh\\jellyfin_upscaler',
        copyKey: 'docker cp ~/.ssh/jellyfin_upscaler.pub jellyfin-ai-upscaler:/root/.ssh/authorized_keys',
        copyKeyWin: 'docker cp %USERPROFILE%\\.ssh\\jellyfin_upscaler.pub jellyfin-ai-upscaler:/root/.ssh/authorized_keys',
        fixPerms: 'docker exec jellyfin-ai-upscaler chmod 600 /root/.ssh/authorized_keys',
        testSsh: 'ssh -i ~/.ssh/jellyfin_upscaler -p 2222 root@localhost',
        testSshWin: 'ssh -i %USERPROFILE%\\.ssh\\jellyfin_upscaler -p 2222 root@localhost',
        testHealth: 'curl http://localhost:5000/health',
        checkSshd: 'docker exec jellyfin-ai-upscaler ps aux | grep sshd',
        removeHostKey: 'ssh-keygen -R "[localhost]:2222"'
    };

    return `
        <div class="page-header fade-in">
            <div class="page-tag">${escapeHtml(s.tag)}</div>
            <h2 class="page-title">${escapeHtml(s.title1)} <span class="gradient">${escapeHtml(s.title2)}</span></h2>
            <p style="color:var(--text-400);margin-top:12px;max-width:640px;line-height:1.6">${escapeHtml(s.intro)}</p>
        </div>

        <div class="warning-box fade-in delay-1">
            <div class="warning-icon">${icons.warning}</div>
            <div>
                <h3>${escapeHtml(s.prereqTitle)}</h3>
                <p>${escapeHtml(s.prereqText)}</p>
            </div>
        </div>

        <!-- Step 1: Docker Container -->
        <div class="step-section fade-in delay-2">
            <div class="step-header">
                <div class="step-number">1</div>
                <h2>${icons.docker} ${escapeHtml(s.step1.title)}</h2>
            </div>
            <div style="margin-left:56px">
                <p style="color:var(--text-400);margin-bottom:16px;font-size:14px">${escapeHtml(s.step1.desc)}</p>
                ${codeBlock(null, s.step1.cmdLabel, cmds.startContainer)}
                <div class="tip-box" style="margin-top:12px">
                    <span class="tip-label">${escapeHtml(s.step1.tip)}</span>
                    <span style="color:var(--text-400);font-size:13px">${escapeHtml(s.step1.tipText)}</span>
                </div>
            </div>
        </div>

        <!-- Step 2: Generate SSH Key -->
        <div class="step-section fade-in delay-3">
            <div class="step-header">
                <div class="step-number">2</div>
                <h2>${icons.key} ${escapeHtml(s.step2.title)}</h2>
            </div>
            <div style="margin-left:56px">
                <p style="color:var(--text-400);margin-bottom:16px;font-size:14px">${escapeHtml(s.step2.desc)}</p>
                ${codeBlock('Linux / macOS', s.step2.cmdLabel, cmds.genKey, 'green')}
                ${codeBlock('Windows', 'PowerShell / CMD', cmds.genKeyWin, 'purple')}
                <div class="tip-box" style="margin-top:12px">
                    <span class="tip-label">${escapeHtml(s.step2.tip)}</span>
                    <span style="color:var(--text-400);font-size:13px">${escapeHtml(s.step2.tipText)}</span>
                </div>
            </div>
        </div>

        <!-- Step 3: Copy Key into Container -->
        <div class="step-section fade-in delay-4">
            <div class="step-header">
                <div class="step-number">3</div>
                <h2>${icons.copy} ${escapeHtml(s.step3.title)}</h2>
            </div>
            <div style="margin-left:56px">
                <p style="color:var(--text-400);margin-bottom:16px;font-size:14px">${escapeHtml(s.step3.desc)}</p>
                ${codeBlock('Linux / macOS', s.step3.cmdLabel, cmds.copyKey, 'green')}
                ${codeBlock('Windows', 'PowerShell / CMD', cmds.copyKeyWin, 'purple')}
                <p style="color:var(--text-400);margin:12px 0 8px;font-size:13px">${escapeHtml(s.step3.fixPerms)}</p>
                ${codeBlock(null, s.step3.fixPermsLabel, cmds.fixPerms)}
            </div>
        </div>

        <!-- Step 4: Test SSH Connection -->
        <div class="step-section fade-in delay-5">
            <div class="step-header">
                <div class="step-number">4</div>
                <h2>${icons.link} ${escapeHtml(s.step4.title)}</h2>
            </div>
            <div style="margin-left:56px">
                <p style="color:var(--text-400);margin-bottom:16px;font-size:14px">${escapeHtml(s.step4.desc)}</p>
                ${codeBlock('Linux / macOS', 'SSH', cmds.testSsh, 'green')}
                ${codeBlock('Windows', 'PowerShell', cmds.testSshWin, 'purple')}
                <div class="tip-box" style="margin-top:12px">
                    <span class="tip-label">${escapeHtml(s.step4.tip)}</span>
                    <span style="color:var(--text-400);font-size:13px">${escapeHtml(s.step4.tipText)}</span>
                </div>
            </div>
        </div>

        <!-- Step 5: Configure Plugin -->
        <div class="step-section fade-in delay-5">
            <div class="step-header">
                <div class="step-number">5</div>
                <h2>${icons.configuration} ${escapeHtml(s.step5.title)}</h2>
            </div>
            <div style="margin-left:56px">
                <p style="color:var(--text-400);margin-bottom:16px;font-size:14px">${escapeHtml(s.step5.desc)}</p>
                <div class="config-group">
                    <div class="config-group-header">${icons.ssh} ${escapeHtml(s.step5.settingsTitle)}</div>
                    <div class="config-list">
                        ${s.step5.settings.map(setting => `
                            <div class="config-row">
                                <span class="config-label">${escapeHtml(setting.label)}</span>
                                <span class="config-value">${escapeHtml(setting.value)}</span>
                            </div>
                        `).join('')}
                    </div>
                </div>
            </div>
        </div>

        <!-- Step 6: Path Mapping -->
        <div class="step-section fade-in delay-5">
            <div class="step-header">
                <div class="step-number">6</div>
                <h2>${icons.folder} ${escapeHtml(s.step6.title)}</h2>
            </div>
            <div style="margin-left:56px">
                <p style="color:var(--text-400);margin-bottom:16px;font-size:14px">${escapeHtml(s.step6.desc)}</p>
                <div class="config-group">
                    <div class="config-group-header">${icons.folder} ${escapeHtml(s.step6.mappingTitle)}</div>
                    <div class="config-list">
                        ${s.step6.mappings.map(m => `
                            <div class="config-row">
                                <span class="config-label">${escapeHtml(m.label)}</span>
                                <span class="config-value">${escapeHtml(m.value)}</span>
                            </div>
                        `).join('')}
                    </div>
                </div>
                <div class="tip-box" style="margin-top:12px">
                    <span class="tip-label">${escapeHtml(s.step6.tip)}</span>
                    <span style="color:var(--text-400);font-size:13px">${escapeHtml(s.step6.tipText)}</span>
                </div>
            </div>
        </div>

        <!-- Troubleshooting -->
        <div style="margin-top:40px" class="fade-in delay-5">
            <h3 style="color:white;font-weight:600;font-size:18px;margin-bottom:16px">${escapeHtml(s.troubleshoot.title)}</h3>
            <div class="accordion">
                ${s.troubleshoot.items.map((item, i) => `
                    <div class="accordion-item" data-idx="${i}">
                        <button class="accordion-trigger" onclick="toggleAccordion(${i})">
                            <div class="accordion-dot"></div>
                            <div class="accordion-trigger-text">
                                <h3>${escapeHtml(item.q)}</h3>
                            </div>
                            <span class="accordion-chevron">${icons.chevronDown}</span>
                        </button>
                        <div class="accordion-body">
                            <p style="color:var(--text-300);font-size:13px;margin-bottom:12px">${escapeHtml(item.a)}</p>
                            ${item.cmd ? `
                                <div class="code-block">
                                    <div class="code-header">
                                        <span style="color:var(--text-400);font-size:12px">${escapeHtml(item.cmdLabel || 'Command')}</span>
                                        <button class="copy-btn" onclick="copyText('${item.cmd.replace(/'/g, "\\'").replace(/\\/g, '\\\\')}', this)">${icons.copy}</button>
                                    </div>
                                    <div class="code-content" style="padding:12px 20px">${escapeHtml(item.cmd)}</div>
                                </div>
                            ` : ''}
                        </div>
                    </div>
                `).join('')}
            </div>
        </div>

        <div class="success-box fade-in delay-5" style="margin-top:32px">
            <div class="success-icon">${icons.checkCircle}</div>
            <div>
                <h3>${escapeHtml(s.done)}</h3>
                <p>${escapeHtml(s.doneText)}</p>
            </div>
        </div>

        <div class="nav-buttons fade-in delay-5" style="margin-top:24px">
            <a href="#configuration" class="btn-primary">${escapeHtml(t().nav.configuration)} ${icons.arrow}</a>
            <a href="#troubleshooting" class="btn-secondary">${escapeHtml(t().nav.troubleshooting)} ${icons.arrow}</a>
        </div>
        <div class="page-footer">${t().footer.copyright}</div>
    `;
}

/* ========================================
   Router
======================================== */
const renderers = {
    home: renderHome,
    installation: renderInstallation,
    sshSetup: renderSshSetup,
    configuration: renderConfiguration,
    features: renderFeatures,
    troubleshooting: renderTroubleshooting,
    dockerTags: renderDockerTags,
    changelog: renderChangelog
};

function route() {
    let hash = location.hash.replace('#', '') || 'home';
    if (!renderers[hash]) hash = 'home';
    currentPage = hash;
    renderNav();
    const main = document.getElementById('mainContent');
    main.innerHTML = renderers[hash]();
    main.scrollTop = 0;
    window.scrollTo(0, 0);
    closeSidebar();
}

/* ========================================
   Language
======================================== */
function setLanguage(lang) {
    currentLang = lang;
    localStorage.setItem('lang', lang);
    document.getElementById('currentFlag').textContent = flags[lang];
    document.getElementById('langMenu').classList.remove('open');
    // Update active state
    document.querySelectorAll('.lang-item').forEach(el => {
        el.classList.toggle('active', el.dataset.lang === lang);
    });
    route(); // re-render
}

/* ========================================
   Sidebar
======================================== */
function closeSidebar() {
    document.getElementById('sidebar').classList.remove('open');
    document.getElementById('sidebarOverlay').classList.remove('active');
    document.getElementById('menuOpen').style.display = '';
    document.getElementById('menuClose').style.display = 'none';
}

function toggleSidebar() {
    const sb = document.getElementById('sidebar');
    const ov = document.getElementById('sidebarOverlay');
    const isOpen = sb.classList.toggle('open');
    ov.classList.toggle('active', isOpen);
    document.getElementById('menuOpen').style.display = isOpen ? 'none' : '';
    document.getElementById('menuClose').style.display = isOpen ? '' : 'none';
}

/* ========================================
   Init
======================================== */
document.addEventListener('DOMContentLoaded', () => {
    // Set initial language flag
    document.getElementById('currentFlag').textContent = flags[currentLang];
    document.querySelectorAll('.lang-item').forEach(el => {
        el.classList.toggle('active', el.dataset.lang === currentLang);
    });

    // Language dropdown toggle
    document.getElementById('langBtn').addEventListener('click', (e) => {
        e.stopPropagation();
        document.getElementById('langMenu').classList.toggle('open');
    });

    // Language selection
    document.querySelectorAll('.lang-item').forEach(btn => {
        btn.addEventListener('click', () => setLanguage(btn.dataset.lang));
    });

    // Close language menu on outside click
    document.addEventListener('click', () => {
        document.getElementById('langMenu').classList.remove('open');
    });

    // Mobile menu
    document.getElementById('menuToggle').addEventListener('click', toggleSidebar);
    document.getElementById('sidebarOverlay').addEventListener('click', closeSidebar);

    // Hash routing
    window.addEventListener('hashchange', route);
    route();
});
