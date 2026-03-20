// AI Upscaler Plugin - Player Integration v1.5.3.0
// Global script injection (loaded via index.html like Intro Skipper)
// Compatible with Jellyfin 10.11+

(function() {
    'use strict';

    // Plugin configuration
    const PLUGIN_ID = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';
    const PLUGIN_VERSION = '1.5.3.2';

    // Prevent double-init
    if (window._aiUpscalerLoaded) return;
    window._aiUpscalerLoaded = true;

    // All available models grouped by category
    const MODEL_CATALOG = {
        realesrgan: {
            label: 'Real-ESRGAN',
            desc: 'Best Quality',
            models: [
                { id: 'realesrgan-x4', name: 'Real-ESRGAN x4', scale: 4, badge: 'Best' },
                { id: 'realesrgan-x4-256', name: 'Real-ESRGAN x4 (256px)', scale: 4, badge: 'Low VRAM' }
            ]
        },
        edsr: {
            label: 'EDSR',
            desc: 'High Quality',
            models: [
                { id: 'edsr-x2', name: 'EDSR x2', scale: 2 },
                { id: 'edsr-x3', name: 'EDSR x3', scale: 3 },
                { id: 'edsr-x4', name: 'EDSR x4', scale: 4 }
            ]
        },
        lapsrn: {
            label: 'LapSRN',
            desc: 'Good Quality',
            models: [
                { id: 'lapsrn-x2', name: 'LapSRN x2', scale: 2 },
                { id: 'lapsrn-x4', name: 'LapSRN x4', scale: 4 },
                { id: 'lapsrn-x8', name: 'LapSRN x8', scale: 8 }
            ]
        },
        fsrcnn: {
            label: 'FSRCNN',
            desc: 'Fast',
            models: [
                { id: 'fsrcnn-x2', name: 'FSRCNN x2', scale: 2 },
                { id: 'fsrcnn-x3', name: 'FSRCNN x3', scale: 3 },
                { id: 'fsrcnn-x4', name: 'FSRCNN x4', scale: 4 }
            ]
        },
        espcn: {
            label: 'ESPCN',
            desc: 'Fastest',
            models: [
                { id: 'espcn-x2', name: 'ESPCN x2', scale: 2 },
                { id: 'espcn-x3', name: 'ESPCN x3', scale: 3 },
                { id: 'espcn-x4', name: 'ESPCN x4', scale: 4 }
            ]
        }
    };

    // Player integration manager
    const PlayerIntegration = {
        _buttonInjected: false,
        _stylesInjected: false,
        _playbackListenersAttached: false,
        _menuCloseHandler: null,
        _menuAutoCloseTimer: null,
        _cachedConfig: null,
        _configCacheTime: 0,

        // Initialize — called once when script loads
        init: function() {
            console.log('AI Upscaler: Player Integration v' + PLUGIN_VERSION + ' initializing...');
            this.addStyles();

            document.addEventListener('viewshow', function(e) {
                PlayerIntegration.onViewShow(e);
            });

            this.waitForApiClient();
            this.addKeyboardShortcuts();
            console.log('AI Upscaler: Player Integration v' + PLUGIN_VERSION + ' loaded');
        },

        waitForApiClient: function() {
            var retries = 0;
            var maxRetries = 30;
            var check = function() {
                if (window.ApiClient) {
                    PlayerIntegration.attachPlaybackListeners();
                } else if (retries < maxRetries) {
                    retries++;
                    setTimeout(check, 1000);
                }
            };
            check();
        },

        onViewShow: function(e) {
            var detail = e.detail || {};
            var type = detail.type || '';
            var isVideoPage = type === 'video-osd' ||
                              (e.target && e.target.id === 'videoOsdPage') ||
                              window.location.hash.startsWith('#/video');

            if (isVideoPage) {
                this._buttonInjected = false;
                this.injectPlayerButton();
            }
        },

        _injectRetryCount: 0,
        _injectMaxRetries: 10,
        _mutationObserver: null,

        injectPlayerButton: function() {
            if (this._buttonInjected) return;

            var selectors = [
                '.videoOsdBottom .buttons',
                '.videoOsdBottom .osdControls',
                '.videoOsdBottom',
                '#videoOsdPage .osdControls',
                '.osdControls',
                '.osdBottomBar',
                '[data-action="fullscreen"]',
                '.btnToggleFullscreen'
            ];

            var container = null;
            for (var i = 0; i < selectors.length; i++) {
                var el = document.querySelector(selectors[i]);
                if (el) {
                    container = (el.tagName === 'BUTTON') ? el.parentElement : el;
                    break;
                }
            }

            if (!container) {
                this._injectRetryCount++;
                if (this._injectRetryCount <= this._injectMaxRetries) {
                    var delay = Math.min(500 * Math.pow(1.5, this._injectRetryCount - 1), 3000);
                    setTimeout(function() { PlayerIntegration.injectPlayerButton(); }, delay);
                } else {
                    this._startMutationObserver();
                }
                return;
            }

            this._injectRetryCount = 0;
            this._stopMutationObserver();

            if (document.querySelector('#aiUpscalerButton')) {
                this._buttonInjected = true;
                return;
            }

            var btn = document.createElement('button');
            btn.id = 'aiUpscalerButton';
            btn.className = 'paper-icon-button-light autoSize';
            btn.setAttribute('is', 'paper-icon-button-light');
            btn.setAttribute('type', 'button');
            btn.setAttribute('title', 'AI Upscaler');
            btn.innerHTML = '<span class="material-icons">auto_awesome</span>';

            btn.addEventListener('click', function(e) {
                e.preventDefault();
                e.stopPropagation();
                PlayerIntegration.toggleUpscalerMenu();
            });

            var refButton = container.querySelector('.btnVideoOsdSettings, .btnToggleFullscreen');
            if (refButton) {
                refButton.parentNode.insertBefore(btn, refButton);
            } else {
                container.appendChild(btn);
            }

            this._buttonInjected = true;
            console.log('AI Upscaler: Player button injected');
        },

        attachPlaybackListeners: function() {
            if (this._playbackListenersAttached) return;

            if (window.playbackManager) {
                try {
                    window.playbackManager.addEventListener('playbackstart', function() {
                        PlayerIntegration._buttonInjected = false;
                        setTimeout(function() { PlayerIntegration.injectPlayerButton(); }, 500);
                    });
                    window.playbackManager.addEventListener('playbackstop', function() {
                        PlayerIntegration._buttonInjected = false;
                    });
                    this._playbackListenersAttached = true;
                } catch (err) {
                    console.warn('AI Upscaler: Could not attach playback listeners:', err);
                }
            }
        },

        // Get config with 10s cache
        getPluginConfig: function() {
            var now = Date.now();
            if (this._cachedConfig && (now - this._configCacheTime) < 10000) {
                return Promise.resolve(this._cachedConfig);
            }
            if (window.ApiClient) {
                return window.ApiClient.getPluginConfiguration(PLUGIN_ID).then(function(config) {
                    PlayerIntegration._cachedConfig = config;
                    PlayerIntegration._configCacheTime = Date.now();
                    return config;
                });
            }
            return Promise.resolve({});
        },

        updatePluginConfig: function(updates) {
            if (window.ApiClient) {
                this.getPluginConfig().then(function(config) {
                    var newConfig = Object.assign({}, config, updates);
                    PlayerIntegration._cachedConfig = newConfig;
                    PlayerIntegration._configCacheTime = Date.now();
                    window.ApiClient.updatePluginConfiguration(PLUGIN_ID, newConfig);
                });
            }
        },

        // Menu management
        _cleanupMenu: function() {
            if (this._menuCloseHandler) {
                document.removeEventListener('click', this._menuCloseHandler);
                this._menuCloseHandler = null;
            }
            if (this._menuAutoCloseTimer) {
                clearTimeout(this._menuAutoCloseTimer);
                this._menuAutoCloseTimer = null;
            }
        },

        toggleUpscalerMenu: function() {
            var existing = document.querySelector('#aiUpscalerQuickMenu');
            if (existing) {
                existing.remove();
                this._cleanupMenu();
                return;
            }

            // Read config to get position + current model + scale
            this.getPluginConfig().then(function(config) {
                PlayerIntegration._buildMenu(config);
            });
        },

        _buildMenu: function(config) {
            var position = (config.ButtonPosition || 'right').toLowerCase();
            var currentModel = config.Model || 'realesrgan-x4';
            var currentScale = config.ScaleFactor || 2;
            var isEnabled = config.EnablePlugin !== false;

            var menu = document.createElement('div');
            menu.id = 'aiUpscalerQuickMenu';
            menu.className = 'ai-menu ai-menu--' + position;

            // Build model list HTML grouped by category
            var modelsHtml = '';
            var cats = Object.keys(MODEL_CATALOG);
            for (var ci = 0; ci < cats.length; ci++) {
                var catKey = cats[ci];
                var cat = MODEL_CATALOG[catKey];
                modelsHtml += '<div class="ai-menu__cat">';
                modelsHtml += '<div class="ai-menu__cat-head">';
                modelsHtml += '<span class="ai-menu__cat-name">' + cat.label + '</span>';
                modelsHtml += '<span class="ai-menu__cat-desc">' + cat.desc + '</span>';
                modelsHtml += '</div>';
                for (var mi = 0; mi < cat.models.length; mi++) {
                    var m = cat.models[mi];
                    var isActive = m.id === currentModel;
                    modelsHtml += '<button class="ai-menu__model' + (isActive ? ' ai-menu__model--active' : '') + '" data-model="' + m.id + '" data-scale="' + m.scale + '">';
                    modelsHtml += '<span class="ai-menu__model-name">' + m.name + '</span>';
                    if (m.badge) {
                        modelsHtml += '<span class="ai-menu__badge">' + m.badge + '</span>';
                    }
                    modelsHtml += '<span class="ai-menu__model-scale">' + m.scale + 'x</span>';
                    if (isActive) {
                        modelsHtml += '<span class="ai-menu__check">&#10003;</span>';
                    }
                    modelsHtml += '</button>';
                }
                modelsHtml += '</div>';
            }

            // Scale buttons
            var scales = [2, 3, 4];
            var scaleHtml = '';
            for (var si = 0; si < scales.length; si++) {
                var s = scales[si];
                var sActive = s === currentScale;
                scaleHtml += '<button class="ai-menu__scale' + (sActive ? ' ai-menu__scale--active' : '') + '" data-scale-val="' + s + '">' + s + 'x</button>';
            }

            menu.innerHTML =
                '<div class="ai-menu__header">' +
                    '<div class="ai-menu__header-left">' +
                        '<span class="material-icons ai-menu__logo">auto_awesome</span>' +
                        '<div>' +
                            '<div class="ai-menu__title">AI Upscaler</div>' +
                            '<div class="ai-menu__version">v' + PLUGIN_VERSION + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="ai-menu__header-right">' +
                        '<button class="ai-menu__toggle' + (isEnabled ? ' ai-menu__toggle--on' : '') + '" data-action="toggle">' +
                            (isEnabled ? 'ON' : 'OFF') +
                        '</button>' +
                        '<button class="ai-menu__close" data-action="close">&times;</button>' +
                    '</div>' +
                '</div>' +
                '<div class="ai-menu__body">' +
                    '<div class="ai-menu__section">' +
                        '<div class="ai-menu__section-title">Models</div>' +
                        '<div class="ai-menu__models">' + modelsHtml + '</div>' +
                    '</div>' +
                    '<div class="ai-menu__section">' +
                        '<div class="ai-menu__section-title">Scale Factor</div>' +
                        '<div class="ai-menu__scales">' + scaleHtml + '</div>' +
                    '</div>' +
                    '<div class="ai-menu__section">' +
                        '<button class="ai-menu__action" data-action="config">' +
                            '<span class="material-icons" style="font-size:16px;margin-right:8px">settings</span>' +
                            'Full Configuration' +
                        '</button>' +
                    '</div>' +
                '</div>';

            document.body.appendChild(menu);

            // Event delegation
            menu.addEventListener('click', function(e) {
                var target = e.target.closest('[data-model]');
                if (target) {
                    PlayerIntegration.quickSetModel(target.getAttribute('data-model'));
                    return;
                }
                target = e.target.closest('[data-scale-val]');
                if (target) {
                    PlayerIntegration.setScale(parseInt(target.getAttribute('data-scale-val'), 10));
                    return;
                }
                target = e.target.closest('[data-action]');
                if (target) {
                    var action = target.getAttribute('data-action');
                    if (action === 'close') {
                        menu.remove();
                        PlayerIntegration._cleanupMenu();
                    } else if (action === 'toggle') {
                        PlayerIntegration.toggleUpscaling();
                    } else if (action === 'config') {
                        PlayerIntegration.openFullConfig();
                    }
                }
            });

            // Close on outside click
            this._cleanupMenu();
            this._menuCloseHandler = function(e) {
                if (!menu.contains(e.target) && e.target.id !== 'aiUpscalerButton') {
                    menu.remove();
                    PlayerIntegration._cleanupMenu();
                }
            };
            setTimeout(function() { document.addEventListener('click', PlayerIntegration._menuCloseHandler); }, 100);

            // Auto-close after 20s
            this._menuAutoCloseTimer = setTimeout(function() {
                if (menu.parentElement) menu.remove();
                PlayerIntegration._cleanupMenu();
            }, 20000);
        },

        quickSetModel: function(model) {
            this.updatePluginConfig({ Model: model });
            this.showPlayerNotification('Model: ' + model, 'success');
            var menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
            this._cleanupMenu();
        },

        setScale: function(scale) {
            this.updatePluginConfig({ ScaleFactor: scale });
            this.showPlayerNotification('Scale: ' + scale + 'x', 'success');
            var menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
            this._cleanupMenu();
        },

        toggleUpscaling: function() {
            this.getPluginConfig().then(function(config) {
                var newState = !config.EnablePlugin;
                PlayerIntegration.updatePluginConfig({ EnablePlugin: newState });
                PlayerIntegration.showPlayerNotification(
                    'Upscaling ' + (newState ? 'enabled' : 'disabled'),
                    newState ? 'success' : 'warning'
                );
            });
            var menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
            this._cleanupMenu();
        },

        openFullConfig: function() {
            window.location.hash = '/configurationpage?name=' + encodeURIComponent('AI Upscaler Plugin');
            var menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
            this._cleanupMenu();
        },

        showPlayerNotification: function(message, type) {
            type = type || 'info';
            var notification = document.createElement('div');
            notification.className = 'ai-notif ai-notif--' + type;
            notification.textContent = message;
            document.body.appendChild(notification);
            setTimeout(function() {
                if (notification.parentElement) notification.remove();
            }, 3000);
        },

        addKeyboardShortcuts: function() {
            document.addEventListener('keydown', function(e) {
                if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.isContentEditable) return;
                if (e.altKey && e.key === 'u') {
                    e.preventDefault();
                    PlayerIntegration.toggleUpscaling();
                }
                if (e.altKey && e.key === 'm') {
                    e.preventDefault();
                    PlayerIntegration.toggleUpscalerMenu();
                }
            });
        },

        _startMutationObserver: function() {
            if (this._mutationObserver) return;
            this._mutationObserver = new MutationObserver(function(mutations) {
                if (PlayerIntegration._buttonInjected) {
                    PlayerIntegration._stopMutationObserver();
                    return;
                }
                for (var i = 0; i < mutations.length; i++) {
                    var addedNodes = mutations[i].addedNodes;
                    for (var j = 0; j < addedNodes.length; j++) {
                        var node = addedNodes[j];
                        if (node.nodeType !== 1) continue;
                        if (node.classList && (
                            node.classList.contains('videoOsdBottom') ||
                            node.classList.contains('osdControls') ||
                            node.id === 'videoOsdPage'
                        )) {
                            PlayerIntegration._injectRetryCount = 0;
                            PlayerIntegration.injectPlayerButton();
                            return;
                        }
                        if (node.querySelector && node.querySelector('.videoOsdBottom, .osdControls, #videoOsdPage')) {
                            PlayerIntegration._injectRetryCount = 0;
                            PlayerIntegration.injectPlayerButton();
                            return;
                        }
                    }
                }
            });
            this._mutationObserver.observe(document.body, { childList: true, subtree: true });
        },

        _stopMutationObserver: function() {
            if (this._mutationObserver) {
                this._mutationObserver.disconnect();
                this._mutationObserver = null;
            }
        },

        addStyles: function() {
            if (this._stylesInjected) return;
            if (document.getElementById('aiUpscalerPlayerStyles')) { this._stylesInjected = true; return; }

            var styles = document.createElement('style');
            styles.id = 'aiUpscalerPlayerStyles';
            styles.textContent =
                /* Button */
                '#aiUpscalerButton {' +
                    'display: inline-flex !important;' +
                    'align-items: center;' +
                    'justify-content: center;' +
                    'color: #fff;' +
                    'cursor: pointer;' +
                    'transition: color 0.2s;' +
                '}' +
                '#aiUpscalerButton:hover {' +
                    'color: #a78bfa;' +
                '}' +
                '#aiUpscalerButton .material-icons {' +
                    'font-size: 24px;' +
                '}' +

                /* Menu base */
                '.ai-menu {' +
                    'position: fixed;' +
                    'bottom: 90px;' +
                    'z-index: 100000;' +
                    'width: 320px;' +
                    'max-height: calc(100vh - 140px);' +
                    'background: rgba(15, 10, 30, 0.96);' +
                    'border: 1px solid rgba(139, 92, 246, 0.3);' +
                    'border-radius: 16px;' +
                    'box-shadow: 0 8px 40px rgba(0, 0, 0, 0.7), 0 0 60px rgba(139, 92, 246, 0.08);' +
                    'backdrop-filter: blur(20px);' +
                    '-webkit-backdrop-filter: blur(20px);' +
                    'overflow: hidden;' +
                    'animation: aiMenuIn 0.25s ease-out;' +
                    'font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;' +
                '}' +

                /* Position variants */
                '.ai-menu--right { right: 20px; }' +
                '.ai-menu--left { left: 20px; }' +
                '.ai-menu--center { left: 50%; transform: translateX(-50%); }' +

                '@keyframes aiMenuIn {' +
                    'from { opacity: 0; transform: translateY(20px); }' +
                    'to { opacity: 1; transform: translateY(0); }' +
                '}' +
                '.ai-menu--center { animation: aiMenuInCenter 0.25s ease-out; }' +
                '@keyframes aiMenuInCenter {' +
                    'from { opacity: 0; transform: translateX(-50%) translateY(20px); }' +
                    'to { opacity: 1; transform: translateX(-50%) translateY(0); }' +
                '}' +

                /* Header */
                '.ai-menu__header {' +
                    'display: flex;' +
                    'align-items: center;' +
                    'justify-content: space-between;' +
                    'padding: 14px 16px;' +
                    'background: linear-gradient(135deg, rgba(139, 92, 246, 0.15), rgba(59, 130, 246, 0.1));' +
                    'border-bottom: 1px solid rgba(139, 92, 246, 0.15);' +
                '}' +
                '.ai-menu__header-left {' +
                    'display: flex;' +
                    'align-items: center;' +
                    'gap: 10px;' +
                '}' +
                '.ai-menu__logo {' +
                    'font-size: 22px;' +
                    'color: #a78bfa;' +
                '}' +
                '.ai-menu__title {' +
                    'font-size: 14px;' +
                    'font-weight: 600;' +
                    'color: #e2e0ff;' +
                    'letter-spacing: 0.3px;' +
                '}' +
                '.ai-menu__version {' +
                    'font-size: 10px;' +
                    'color: rgba(167, 139, 250, 0.6);' +
                '}' +
                '.ai-menu__header-right {' +
                    'display: flex;' +
                    'align-items: center;' +
                    'gap: 8px;' +
                '}' +
                '.ai-menu__toggle {' +
                    'padding: 4px 12px;' +
                    'border-radius: 12px;' +
                    'border: 1px solid rgba(255,255,255,0.15);' +
                    'background: rgba(255, 60, 60, 0.2);' +
                    'color: #ff6b6b;' +
                    'font-size: 11px;' +
                    'font-weight: 700;' +
                    'cursor: pointer;' +
                    'transition: all 0.2s;' +
                '}' +
                '.ai-menu__toggle--on {' +
                    'background: rgba(52, 211, 153, 0.2);' +
                    'color: #34d399;' +
                    'border-color: rgba(52, 211, 153, 0.3);' +
                '}' +
                '.ai-menu__toggle:hover { opacity: 0.8; }' +
                '.ai-menu__close {' +
                    'background: none;' +
                    'border: none;' +
                    'color: rgba(255,255,255,0.4);' +
                    'font-size: 20px;' +
                    'cursor: pointer;' +
                    'padding: 0;' +
                    'width: 24px;' +
                    'height: 24px;' +
                    'display: flex;' +
                    'align-items: center;' +
                    'justify-content: center;' +
                    'border-radius: 6px;' +
                    'transition: all 0.15s;' +
                '}' +
                '.ai-menu__close:hover {' +
                    'background: rgba(255,255,255,0.1);' +
                    'color: #fff;' +
                '}' +

                /* Body */
                '.ai-menu__body {' +
                    'padding: 12px;' +
                    'overflow-y: auto;' +
                    'max-height: calc(100vh - 240px);' +
                '}' +
                '.ai-menu__body::-webkit-scrollbar { width: 4px; }' +
                '.ai-menu__body::-webkit-scrollbar-thumb { background: rgba(139,92,246,0.3); border-radius: 2px; }' +
                '.ai-menu__body::-webkit-scrollbar-track { background: transparent; }' +

                /* Section */
                '.ai-menu__section {' +
                    'margin-bottom: 12px;' +
                '}' +
                '.ai-menu__section:last-child { margin-bottom: 0; }' +
                '.ai-menu__section-title {' +
                    'font-size: 10px;' +
                    'font-weight: 600;' +
                    'text-transform: uppercase;' +
                    'letter-spacing: 1.2px;' +
                    'color: rgba(167, 139, 250, 0.5);' +
                    'padding: 0 4px 6px;' +
                '}' +

                /* Model categories */
                '.ai-menu__cat {' +
                    'margin-bottom: 8px;' +
                '}' +
                '.ai-menu__cat:last-child { margin-bottom: 0; }' +
                '.ai-menu__cat-head {' +
                    'display: flex;' +
                    'align-items: baseline;' +
                    'gap: 6px;' +
                    'padding: 4px;' +
                '}' +
                '.ai-menu__cat-name {' +
                    'font-size: 11px;' +
                    'font-weight: 600;' +
                    'color: rgba(255,255,255,0.7);' +
                '}' +
                '.ai-menu__cat-desc {' +
                    'font-size: 10px;' +
                    'color: rgba(255,255,255,0.3);' +
                '}' +

                /* Model button */
                '.ai-menu__model {' +
                    'display: flex;' +
                    'align-items: center;' +
                    'width: 100%;' +
                    'padding: 7px 10px;' +
                    'background: rgba(255,255,255,0.03);' +
                    'border: 1px solid transparent;' +
                    'border-radius: 8px;' +
                    'color: rgba(255,255,255,0.75);' +
                    'font-size: 12px;' +
                    'cursor: pointer;' +
                    'transition: all 0.15s;' +
                    'margin: 2px 0;' +
                    'text-align: left;' +
                '}' +
                '.ai-menu__model:hover {' +
                    'background: rgba(139, 92, 246, 0.1);' +
                    'border-color: rgba(139, 92, 246, 0.2);' +
                    'color: #fff;' +
                '}' +
                '.ai-menu__model--active {' +
                    'background: rgba(139, 92, 246, 0.15) !important;' +
                    'border-color: rgba(139, 92, 246, 0.4) !important;' +
                    'color: #a78bfa !important;' +
                '}' +
                '.ai-menu__model-name {' +
                    'flex: 1;' +
                '}' +
                '.ai-menu__model-scale {' +
                    'font-size: 10px;' +
                    'color: rgba(255,255,255,0.35);' +
                    'margin-left: 8px;' +
                '}' +
                '.ai-menu__badge {' +
                    'font-size: 9px;' +
                    'padding: 1px 6px;' +
                    'border-radius: 4px;' +
                    'background: rgba(139, 92, 246, 0.2);' +
                    'color: #a78bfa;' +
                    'margin-left: 6px;' +
                    'font-weight: 600;' +
                '}' +
                '.ai-menu__check {' +
                    'color: #34d399;' +
                    'font-size: 13px;' +
                    'margin-left: 6px;' +
                '}' +

                /* Scale buttons */
                '.ai-menu__scales {' +
                    'display: flex;' +
                    'gap: 6px;' +
                '}' +
                '.ai-menu__scale {' +
                    'flex: 1;' +
                    'padding: 8px;' +
                    'background: rgba(255,255,255,0.04);' +
                    'border: 1px solid rgba(255,255,255,0.1);' +
                    'border-radius: 8px;' +
                    'color: rgba(255,255,255,0.7);' +
                    'font-size: 13px;' +
                    'font-weight: 600;' +
                    'cursor: pointer;' +
                    'transition: all 0.15s;' +
                '}' +
                '.ai-menu__scale:hover {' +
                    'background: rgba(139, 92, 246, 0.15);' +
                    'border-color: rgba(139, 92, 246, 0.3);' +
                    'color: #fff;' +
                '}' +
                '.ai-menu__scale--active {' +
                    'background: rgba(139, 92, 246, 0.2) !important;' +
                    'border-color: rgba(139, 92, 246, 0.5) !important;' +
                    'color: #a78bfa !important;' +
                '}' +

                /* Action button */
                '.ai-menu__action {' +
                    'display: flex;' +
                    'align-items: center;' +
                    'justify-content: center;' +
                    'width: 100%;' +
                    'padding: 10px;' +
                    'background: rgba(255,255,255,0.04);' +
                    'border: 1px solid rgba(255,255,255,0.08);' +
                    'border-radius: 8px;' +
                    'color: rgba(255,255,255,0.6);' +
                    'font-size: 12px;' +
                    'cursor: pointer;' +
                    'transition: all 0.15s;' +
                '}' +
                '.ai-menu__action:hover {' +
                    'background: rgba(255,255,255,0.08);' +
                    'color: #fff;' +
                '}' +

                /* Notification */
                '.ai-notif {' +
                    'position: fixed;' +
                    'top: 20px;' +
                    'right: 20px;' +
                    'padding: 10px 16px;' +
                    'border-radius: 10px;' +
                    'color: #fff;' +
                    'font-size: 13px;' +
                    'font-weight: 500;' +
                    'z-index: 100001;' +
                    'animation: aiNotifIn 0.3s ease-out;' +
                    'pointer-events: none;' +
                    'backdrop-filter: blur(12px);' +
                    '-webkit-backdrop-filter: blur(12px);' +
                '}' +
                '.ai-notif--info { background: rgba(59, 130, 246, 0.85); }' +
                '.ai-notif--success { background: rgba(16, 185, 129, 0.85); }' +
                '.ai-notif--warning { background: rgba(245, 158, 11, 0.85); }' +
                '.ai-notif--error { background: rgba(239, 68, 68, 0.85); }' +
                '@keyframes aiNotifIn {' +
                    'from { transform: translateY(-10px); opacity: 0; }' +
                    'to { transform: translateY(0); opacity: 1; }' +
                '}';

            document.head.appendChild(styles);
            this._stylesInjected = true;
        }
    };

    // Make available globally
    window.PlayerIntegration = PlayerIntegration;

    // Initialize
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() { PlayerIntegration.init(); });
    } else {
        PlayerIntegration.init();
    }
})();
