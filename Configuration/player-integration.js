// AI Upscaler Plugin - Player Integration v1.5.2.4
// Global script injection (loaded via index.html like Intro Skipper)
// Compatible with Jellyfin 10.11+

(function() {
    'use strict';

    // Plugin configuration
    const PLUGIN_ID = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';
    const PLUGIN_VERSION = '1.5.2.4';

    // Prevent double-init
    if (window._aiUpscalerLoaded) return;
    window._aiUpscalerLoaded = true;

    // Player integration manager
    const PlayerIntegration = {
        _buttonInjected: false,
        _stylesInjected: false,
        _playbackListenersAttached: false,

        // Initialize — called once when script loads
        init: function() {
            console.log(`AI Upscaler: Player Integration v${PLUGIN_VERSION} initializing...`);

            // Inject CSS once
            this.addStyles();

            // Listen for Jellyfin SPA navigation events
            // 'viewshow' fires every time Jellyfin navigates to a new view (including video player)
            document.addEventListener('viewshow', (e) => {
                this.onViewShow(e);
            });

            // Also listen for playback events when ApiClient becomes available
            this.waitForApiClient();

            // Add keyboard shortcuts once
            this.addKeyboardShortcuts();

            console.log(`AI Upscaler: Player Integration v${PLUGIN_VERSION} loaded (global injection)`);
        },

        // Wait for Jellyfin's ApiClient to be available
        waitForApiClient: function() {
            const check = () => {
                if (window.ApiClient) {
                    this.attachPlaybackListeners();
                } else {
                    setTimeout(check, 1000);
                }
            };
            check();
        },

        // Called on every SPA view change
        onViewShow: function(e) {
            const detail = e.detail || {};
            const type = detail.type || '';
            const id = (detail.params && detail.params.id) || '';

            // Check if we navigated to the video player page
            // Jellyfin 10.11 uses #/video as the video OSD page
            const isVideoPage = type === 'video-osd' ||
                                (e.target && e.target.id === 'videoOsdPage') ||
                                window.location.hash.startsWith('#/video');

            if (isVideoPage) {
                console.log('AI Upscaler: Video player detected, injecting button...');
                this._buttonInjected = false; // Reset so we re-inject
                this.injectPlayerButton();
            }
        },

        // Inject button into video player OSD
        injectPlayerButton: function() {
            if (this._buttonInjected) return;

            // Try multiple selectors for different Jellyfin versions
            const selectors = [
                '.videoOsdBottom .buttons',           // 10.11 standard
                '.videoOsdBottom',                     // 10.11 fallback
                '#videoOsdPage .osdControls',           // alternate
                '.osdControls',                         // generic
            ];

            let container = null;
            for (const sel of selectors) {
                container = document.querySelector(sel);
                if (container) break;
            }

            if (!container) {
                // Player DOM not ready yet — retry
                setTimeout(() => this.injectPlayerButton(), 500);
                return;
            }

            // Don't duplicate
            if (document.querySelector('#aiUpscalerButton')) {
                this._buttonInjected = true;
                return;
            }

            // Create the AI Upscaler button (matches Jellyfin's player button style)
            const btn = document.createElement('button');
            btn.id = 'aiUpscalerButton';
            btn.className = 'paper-icon-button-light autoSize';
            btn.setAttribute('is', 'paper-icon-button-light');
            btn.setAttribute('type', 'button');
            btn.setAttribute('title', 'AI Upscaler');
            btn.innerHTML = '<span class="material-icons">auto_awesome</span>';

            btn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.toggleUpscalerMenu();
            });

            // Insert before fullscreen button or at end
            const refButton = container.querySelector('.btnVideoOsdSettings, .btnToggleFullscreen');
            if (refButton) {
                refButton.parentNode.insertBefore(btn, refButton);
            } else {
                container.appendChild(btn);
            }

            this._buttonInjected = true;
            console.log('AI Upscaler: Player button injected');
        },

        // Attach playback event listeners (once)
        attachPlaybackListeners: function() {
            if (this._playbackListenersAttached) return;

            if (window.playbackManager) {
                try {
                    window.playbackManager.addEventListener('playbackstart', () => {
                        console.log('AI Upscaler: Playback started');
                        // Re-inject button when playback starts (DOM might have been rebuilt)
                        this._buttonInjected = false;
                        setTimeout(() => this.injectPlayerButton(), 500);
                    });

                    window.playbackManager.addEventListener('playbackstop', () => {
                        console.log('AI Upscaler: Playback stopped');
                        this._buttonInjected = false;
                    });

                    this._playbackListenersAttached = true;
                    console.log('AI Upscaler: Playback listeners attached');
                } catch (err) {
                    console.warn('AI Upscaler: Could not attach playback listeners:', err);
                }
            }
        },

        // Toggle upscaler quick menu
        toggleUpscalerMenu: function() {
            const existingMenu = document.querySelector('#aiUpscalerQuickMenu');
            if (existingMenu) {
                existingMenu.remove();
                return;
            }

            const menu = document.createElement('div');
            menu.id = 'aiUpscalerQuickMenu';
            menu.className = 'aiUpscalerQuickMenu';
            menu.innerHTML = `
                <div class="quick-menu-header">
                    <span class="menu-title">🚀 AI Upscaler</span>
                    <button class="menu-close" onclick="this.parentElement.parentElement.remove()">×</button>
                </div>
                <div class="quick-menu-content">
                    <div class="menu-section">
                        <h4>Quick Settings</h4>
                        <div class="menu-item" onclick="PlayerIntegration.quickSetModel('realesrgan-x4')">
                            <span class="menu-icon">🎨</span>
                            <span>Real-ESRGAN x4 (Best Quality)</span>
                        </div>
                        <div class="menu-item" onclick="PlayerIntegration.quickSetModel('edsr-x4')">
                            <span class="menu-icon">⚡</span>
                            <span>EDSR x4 (Best OpenCV)</span>
                        </div>
                        <div class="menu-item" onclick="PlayerIntegration.quickSetModel('fsrcnn-x2')">
                            <span class="menu-icon">🔧</span>
                            <span>FSRCNN x2 (Fast)</span>
                        </div>
                        <div class="menu-item" onclick="PlayerIntegration.quickSetModel('espcn-x2')">
                            <span class="menu-icon">🚀</span>
                            <span>ESPCN x2 (Fastest)</span>
                        </div>
                    </div>
                    <div class="menu-section">
                        <h4>Scale Factor</h4>
                        <div class="scale-buttons">
                            <button class="scale-btn" onclick="PlayerIntegration.setScale(2)">2x</button>
                            <button class="scale-btn" onclick="PlayerIntegration.setScale(3)">3x</button>
                            <button class="scale-btn" onclick="PlayerIntegration.setScale(4)">4x</button>
                        </div>
                    </div>
                    <div class="menu-section">
                        <h4>Actions</h4>
                        <div class="menu-item" onclick="PlayerIntegration.toggleUpscaling()">
                            <span class="menu-icon">🔄</span>
                            <span>Toggle Upscaling</span>
                        </div>
                        <div class="menu-item" onclick="PlayerIntegration.openFullConfig()">
                            <span class="menu-icon">⚙️</span>
                            <span>Full Configuration</span>
                        </div>
                    </div>
                </div>
            `;

            document.body.appendChild(menu);

            // Close menu on outside click
            const closeHandler = (e) => {
                if (!menu.contains(e.target) && e.target.id !== 'aiUpscalerButton') {
                    menu.remove();
                    document.removeEventListener('click', closeHandler);
                }
            };
            setTimeout(() => document.addEventListener('click', closeHandler), 100);

            // Auto-close after 15 seconds
            setTimeout(() => {
                if (menu.parentElement) {
                    menu.remove();
                    document.removeEventListener('click', closeHandler);
                }
            }, 15000);
        },

        // Quick model selection
        quickSetModel: function(model) {
            this.updatePluginConfig({ Model: model });
            this.showPlayerNotification('Model: ' + model, 'success');
            const menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
        },

        // Set scale factor
        setScale: function(scale) {
            this.updatePluginConfig({ ScaleFactor: scale });
            this.showPlayerNotification('Scale: ' + scale + 'x', 'success');
            const menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
        },

        // Toggle upscaling on/off
        toggleUpscaling: function() {
            this.getPluginConfig().then(config => {
                const newState = !config.EnablePlugin;
                this.updatePluginConfig({ EnablePlugin: newState });
                this.showPlayerNotification(
                    'Upscaling ' + (newState ? 'enabled' : 'disabled'),
                    newState ? 'success' : 'warning'
                );
            });
            const menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
        },

        // Open full configuration
        openFullConfig: function() {
            // Navigate within Jellyfin SPA
            var configUrl = ApiClient.getUrl('web/configurationpage') + '?name=' + encodeURIComponent('AI Upscaler Plugin');
            window.location.hash = '/configurationpage?name=' + encodeURIComponent('AI Upscaler Plugin');
            const menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
        },

        // Configuration management
        getPluginConfig: function() {
            if (window.ApiClient) {
                return window.ApiClient.getPluginConfiguration(PLUGIN_ID);
            }
            return Promise.resolve({});
        },

        updatePluginConfig: function(updates) {
            if (window.ApiClient) {
                this.getPluginConfig().then(config => {
                    const newConfig = Object.assign({}, config, updates);
                    window.ApiClient.updatePluginConfiguration(PLUGIN_ID, newConfig).then(() => {
                        console.log('AI Upscaler: Configuration updated');
                    });
                });
            }
        },

        // Show player notification
        showPlayerNotification: function(message, type) {
            type = type || 'info';
            const notification = document.createElement('div');
            notification.className = 'ai-upscaler-notification notification-' + type;
            notification.textContent = message;

            const videoContainer = document.querySelector('#videoOsdPage, .videoContainer, .playerContainer');
            if (videoContainer) {
                videoContainer.appendChild(notification);
            } else {
                document.body.appendChild(notification);
            }

            setTimeout(() => {
                if (notification.parentElement) {
                    notification.remove();
                }
            }, 3000);
        },

        // Keyboard shortcuts
        addKeyboardShortcuts: function() {
            document.addEventListener('keydown', (e) => {
                if (e.altKey && e.key === 'u') {
                    e.preventDefault();
                    this.toggleUpscaling();
                }
                if (e.altKey && e.key === 'm') {
                    e.preventDefault();
                    this.toggleUpscalerMenu();
                }
            });
        },

        // Add styles (once)
        addStyles: function() {
            if (this._stylesInjected) return;

            const styles = document.createElement('style');
            styles.id = 'aiUpscalerPlayerStyles';
            styles.textContent = `
                #aiUpscalerButton {
                    display: inline-flex !important;
                    align-items: center;
                    justify-content: center;
                    color: #fff;
                    cursor: pointer;
                }
                #aiUpscalerButton:hover {
                    color: #00d4ff;
                }
                #aiUpscalerButton .material-icons {
                    font-size: 24px;
                }

                .aiUpscalerQuickMenu {
                    position: fixed;
                    top: 50%;
                    left: 50%;
                    transform: translate(-50%, -50%);
                    background: rgba(0, 0, 0, 0.95);
                    border: 2px solid #00d4ff;
                    border-radius: 12px;
                    z-index: 100000;
                    min-width: 300px;
                    max-width: 400px;
                    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.8);
                    animation: aiMenuSlideIn 0.3s ease-out;
                }
                @keyframes aiMenuSlideIn {
                    from { transform: translate(-50%, -50%) scale(0.8); opacity: 0; }
                    to { transform: translate(-50%, -50%) scale(1); opacity: 1; }
                }
                .quick-menu-header {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    padding: 15px 20px;
                    background: linear-gradient(135deg, #00d4ff, #0099cc);
                    color: #000;
                    border-radius: 10px 10px 0 0;
                }
                .menu-title { font-weight: bold; font-size: 16px; }
                .menu-close {
                    background: none; border: none; color: #000;
                    font-size: 20px; cursor: pointer; padding: 0;
                    width: 24px; height: 24px; border-radius: 50%;
                    display: flex; align-items: center; justify-content: center;
                }
                .menu-close:hover { background: rgba(0,0,0,0.2); }
                .quick-menu-content { padding: 20px; }
                .menu-section { margin-bottom: 20px; }
                .menu-section:last-child { margin-bottom: 0; }
                .menu-section h4 { color: #00d4ff; margin: 0 0 10px 0; font-size: 14px; }
                .menu-item {
                    display: flex; align-items: center;
                    padding: 10px 15px; background: rgba(255,255,255,0.1);
                    border-radius: 6px; margin: 5px 0;
                    cursor: pointer; color: #fff; transition: all 0.2s ease;
                }
                .menu-item:hover {
                    background: rgba(0,212,255,0.3);
                    transform: translateX(5px);
                }
                .menu-icon { margin-right: 10px; font-size: 16px; }
                .scale-buttons { display: flex; gap: 10px; }
                .scale-btn {
                    flex: 1; padding: 8px 12px;
                    background: rgba(255,255,255,0.1);
                    border: 1px solid rgba(255,255,255,0.3);
                    border-radius: 4px; color: #fff; cursor: pointer;
                    transition: all 0.2s ease;
                }
                .scale-btn:hover { background: rgba(0,212,255,0.5); border-color: #00d4ff; }
                .ai-upscaler-notification {
                    position: fixed; top: 20px; right: 20px;
                    padding: 12px 18px; border-radius: 8px;
                    color: white; font-weight: 500; z-index: 100001;
                    animation: aiNotifSlideIn 0.3s ease-out; pointer-events: none;
                }
                .notification-info { background: rgba(37,99,235,0.9); }
                .notification-success { background: rgba(5,150,105,0.9); }
                .notification-warning { background: rgba(217,119,6,0.9); }
                .notification-error { background: rgba(220,38,38,0.9); }
                @keyframes aiNotifSlideIn {
                    from { transform: translateX(100%); opacity: 0; }
                    to { transform: translateX(0); opacity: 1; }
                }
            `;
            document.head.appendChild(styles);
            this._stylesInjected = true;
        }
    };

    // Make available globally
    window.PlayerIntegration = PlayerIntegration;

    // Initialize
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => PlayerIntegration.init());
    } else {
        PlayerIntegration.init();
    }
})();
