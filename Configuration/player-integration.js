// AI Upscaler Plugin - Player Integration v1.5.4.3
// Global script injection (loaded via index.html like Intro Skipper)
// Compatible with Jellyfin 10.11+

(function() {
    'use strict';

    // Plugin configuration
    const PLUGIN_ID = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';
    const PLUGIN_VERSION = '1.5.4.3';

    // Prevent double-init
    if (window._aiUpscalerLoaded) return;
    window._aiUpscalerLoaded = true;

    // All available models grouped by category (synced with Python AVAILABLE_MODELS)
    const MODEL_CATALOG = {
        realesrgan: {
            label: 'Real-ESRGAN',
            desc: 'Best Quality (ONNX)',
            models: [
                { id: 'realesrgan-x4', name: 'Real-ESRGAN x4', scale: 4, badge: 'Best' },
                { id: 'realesrgan-x4-256', name: 'Real-ESRGAN x4 (256px)', scale: 4, badge: 'Low VRAM' }
            ]
        },
        nextgen: {
            label: 'Next-Gen',
            desc: 'Modern Architectures (ONNX)',
            models: [
                { id: 'span-x2', name: 'SPAN x2', scale: 2, badge: 'Fast Quality' },
                { id: 'span-x4', name: 'SPAN x4', scale: 4 },
                { id: 'realesrgan-x2-plus', name: 'Real-ESRGAN x2+', scale: 2 },
                { id: 'realesrgan-animevideo-x4', name: 'Real-ESRGAN AnimeVideo x4', scale: 4 },
                { id: 'swinir-x4', name: 'SwinIR x4', scale: 4 },
                { id: 'apisr-x3', name: 'APISR x3', scale: 3 }
            ]
        },
        'video-fast': {
            label: 'Video Real-Time',
            desc: 'Ultra-Fast for Playback',
            models: [
                { id: 'clearreality-x4', name: 'ClearReality x4', scale: 4, badge: 'Ultra-Fast' },
                { id: 'nomosuni-compact-x2', name: 'NomosUni Compact x2', scale: 2 },
                { id: 'lsdir-compact-x4', name: 'LSDIR Compact x4', scale: 4 },
                { id: 'swinir-small-x2', name: 'SwinIR-S x2', scale: 2 },
                { id: 'swinir-small-x4', name: 'SwinIR-S x4', scale: 4 }
            ]
        },
        'video-quality': {
            label: 'Video Quality',
            desc: 'Best Single-Frame for Video',
            models: [
                { id: 'ultrasharp-v2-x4', name: 'UltraSharp V2 x4', scale: 4, badge: 'Best Photo/Video' },
                { id: 'nomos2-dat2-x4', name: 'Nomos2 DAT2 x4', scale: 4 },
                { id: 'nomos2-realplksr-x4', name: 'Nomos2 RealPLKSR x4', scale: 4 }
            ]
        },
        'film-restore': {
            label: 'Film Restoration',
            desc: 'Old Movies, DVDs, VHS',
            models: [
                { id: 'fsdedither-x4', name: 'FSDedither x4', scale: 4 },
                { id: 'nomos8k-hat-x4', name: 'Nomos8k HAT-S x4', scale: 4 }
            ]
        },
        anime: {
            label: 'Anime',
            desc: 'Anime Specialist',
            models: [
                { id: 'anime-compact-x4', name: 'Real-ESRGAN Anime Compact x4', scale: 4, badge: 'Fast Anime' },
                { id: 'apisr-anime-x2', name: 'APISR x2 Anime', scale: 2 }
            ]
        },
        'video-sr': {
            label: 'Video SR',
            desc: 'Multi-Frame (Best Batch Quality)',
            models: [
                { id: 'edvr-m-x4', name: 'EDVR-M x4 (5 Frame)', scale: 4 },
                { id: 'realbasicvsr-x4', name: 'RealBasicVSR x4 (5 Frame)', scale: 4 },
                { id: 'animesr-v2-x4', name: 'AnimeSR v2 x4 (5 Frame)', scale: 4 }
            ]
        },
        edsr: {
            label: 'EDSR',
            desc: 'High Quality (OpenCV)',
            models: [
                { id: 'edsr-x2', name: 'EDSR x2', scale: 2 },
                { id: 'edsr-x3', name: 'EDSR x3', scale: 3 },
                { id: 'edsr-x4', name: 'EDSR x4', scale: 4 }
            ]
        },
        lapsrn: {
            label: 'LapSRN',
            desc: 'Good Quality (OpenCV)',
            models: [
                { id: 'lapsrn-x2', name: 'LapSRN x2', scale: 2 },
                { id: 'lapsrn-x4', name: 'LapSRN x4', scale: 4 },
                { id: 'lapsrn-x8', name: 'LapSRN x8', scale: 8 }
            ]
        },
        fsrcnn: {
            label: 'FSRCNN',
            desc: 'Fast (OpenCV)',
            models: [
                { id: 'fsrcnn-x2', name: 'FSRCNN x2', scale: 2 },
                { id: 'fsrcnn-x3', name: 'FSRCNN x3', scale: 3 },
                { id: 'fsrcnn-x4', name: 'FSRCNN x4', scale: 4 }
            ]
        },
        espcn: {
            label: 'ESPCN',
            desc: 'Fastest (OpenCV)',
            models: [
                { id: 'espcn-x2', name: 'ESPCN x2', scale: 2 },
                { id: 'espcn-x3', name: 'ESPCN x3', scale: 3 },
                { id: 'espcn-x4', name: 'ESPCN x4', scale: 4 }
            ]
        },
        vulkan: {
            label: 'Vulkan GPU',
            desc: 'ncnn (AMD/Intel)',
            models: [
                { id: 'ncnn-realesrgan-x4', name: 'Real-ESRGAN x4 (Vulkan)', scale: 4 },
                { id: 'ncnn-realesrgan-anime-x4', name: 'Real-ESRGAN Anime x4 (Vulkan)', scale: 4 },
                { id: 'ncnn-realsr-x4', name: 'RealSR x4 (Vulkan)', scale: 4 }
            ]
        }
    };

    // Real-Time Upscaler Engine
    const RealtimeUpscaler = {
        _mode: null,       // 'webgl' | 'server' | null
        _active: false,
        _videoElement: null,
        _captureCanvas: null,
        _captureCtx: null,
        _overlayCanvas: null,
        _overlayCtx: null,
        _pendingFrame: false,
        _currentObjectUrl: null,
        _fpsFrameCount: 0,
        _fpsLastTime: 0,
        _currentFps: 0,
        _lowFpsStart: 0,
        _config: null,
        _benchmarkResult: null,
        _webglInstance: null,

        start: function(video, config, benchmarkResult) {
            this._videoElement = video;
            this._config = config;
            this._benchmarkResult = benchmarkResult;

            var mode = (config.RealtimeMode || 'auto').toLowerCase();
            if (mode === 'auto') {
                mode = this._decideTier(benchmarkResult, video);
            }

            this._mode = mode;
            this._active = true;
            this._lowFpsStart = 0;
            console.log('AI Upscaler RT: Starting in ' + mode + ' mode');

            if (mode === 'server') {
                this._startServer();
            } else {
                this._startWebGL();
            }

            this._createFpsOverlay();
            this._updateButtonIndicator(mode);
        },

        stop: function() {
            this._active = false;
            this._mode = null;
            this._stopServer();
            this._stopWebGL();
            this._removeFpsOverlay();
            this._updateButtonIndicator(null);
            console.log('AI Upscaler RT: Stopped');
        },

        _decideTier: function(benchmark, video) {
            if (!benchmark || benchmark.error) return 'webgl';
            var videoFps = 24; // reasonable default
            try {
                var rate = video.playbackRate || 1;
                videoFps = (video.getVideoPlaybackQuality && video.getVideoPlaybackQuality().totalVideoFrames > 0) ? 30 : 24;
                videoFps *= rate;
            } catch(e) {}
            if (benchmark.fps >= videoFps * 0.8) return 'server';
            return 'webgl';
        },

        // --- WebGL Tier ---
        _startWebGL: function() {
            if (!window.AIUpscalerWebGL) {
                this._loadWebGLScript(function() {
                    RealtimeUpscaler._initWebGL();
                });
                return;
            }
            this._initWebGL();
        },

        _loadWebGLScript: function(callback) {
            // Check if already loaded
            if (document.querySelector('script[src*="webgl-upscaler"]')) {
                if (window.AIUpscalerWebGL) { callback(); return; }
                setTimeout(callback, 500);
                return;
            }
            var paths = [
                '/api/upscaler/js/webgl-upscaler.js',
                '/upscaler/js/webgl-upscaler.js'
            ];
            var tryLoad = function(idx) {
                if (idx >= paths.length) {
                    console.warn('AI Upscaler RT: Could not load WebGL shader');
                    return;
                }
                var script = document.createElement('script');
                script.src = paths[idx];
                script.onload = function() { setTimeout(callback, 100); };
                script.onerror = function() { tryLoad(idx + 1); };
                document.head.appendChild(script);
            };
            tryLoad(0);
        },

        _initWebGL: function() {
            if (!window.AIUpscalerWebGL || !this._videoElement || !this._active) return;
            var wgl = window.AIUpscalerWebGL;
            if (wgl.init(this._videoElement)) {
                wgl.onFpsUpdate = function(fps) {
                    RealtimeUpscaler._currentFps = fps;
                    RealtimeUpscaler._updateFpsDisplay();
                };
                wgl.enable();
                this._webglInstance = wgl;
            }
        },

        _stopWebGL: function() {
            if (this._webglInstance) {
                this._webglInstance.disable();
                this._webglInstance.destroy();
                this._webglInstance = null;
            }
        },

        // --- Server AI Tier ---
        _startServer: function() {
            var captureW = (this._config && this._config.RealtimeCaptureWidth) || 480;
            var captureH = Math.round(captureW * (this._videoElement.videoHeight / this._videoElement.videoWidth));

            this._captureCanvas = document.createElement('canvas');
            this._captureCanvas.width = captureW;
            this._captureCanvas.height = captureH;
            this._captureCtx = this._captureCanvas.getContext('2d');

            // Overlay canvas for displaying upscaled frames
            this._overlayCanvas = document.createElement('canvas');
            this._overlayCanvas.style.cssText = 'position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:999;';
            var parent = this._videoElement.parentElement;
            if (parent) {
                parent.style.position = 'relative';
                parent.appendChild(this._overlayCanvas);
            }

            this._pendingFrame = false;
            this._fpsFrameCount = 0;
            this._fpsLastTime = performance.now();
            this._lastSuccessfulFrame = performance.now();
            this._serverRenderLoop();
            // Timer-based fallback check: if no successful frame for 5 seconds, switch to WebGL
            this._fallbackCheckInterval = setInterval(function() {
                if (!RealtimeUpscaler._active || RealtimeUpscaler._mode !== 'server') {
                    clearInterval(RealtimeUpscaler._fallbackCheckInterval);
                    return;
                }
                if (performance.now() - RealtimeUpscaler._lastSuccessfulFrame > 5000) {
                    console.log('AI Upscaler RT: No frames for 5s, switching to WebGL');
                    clearInterval(RealtimeUpscaler._fallbackCheckInterval);
                    RealtimeUpscaler._stopServer();
                    RealtimeUpscaler._mode = 'webgl';
                    RealtimeUpscaler._startWebGL();
                    RealtimeUpscaler._updateButtonIndicator('webgl');
                    if (window.PlayerIntegration) {
                        window.PlayerIntegration.showPlayerNotification('Switched to WebGL (server unresponsive)', 'warning');
                    }
                }
            }, 2000);
        },

        _stopServer: function() {
            if (this._serverRafId) {
                cancelAnimationFrame(this._serverRafId);
                this._serverRafId = null;
            }
            if (this._fallbackCheckInterval) {
                clearInterval(this._fallbackCheckInterval);
                this._fallbackCheckInterval = null;
            }
            // Revoke any pending object URL to prevent memory leak
            if (this._currentObjectUrl) {
                URL.revokeObjectURL(this._currentObjectUrl);
                this._currentObjectUrl = null;
            }
            if (this._overlayCanvas && this._overlayCanvas.parentElement) {
                this._overlayCanvas.parentElement.removeChild(this._overlayCanvas);
            }
            this._overlayCanvas = null;
            this._overlayCtx = null;
            this._captureCanvas = null;
            this._captureCtx = null;
        },

        _serverRafId: null,

        _serverRenderLoop: function() {
            if (!this._active || this._mode !== 'server') return;

            if (!this._pendingFrame && this._videoElement && !this._videoElement.paused) {
                this._captureAndSend();
            }

            this._serverRafId = requestAnimationFrame(function() { RealtimeUpscaler._serverRenderLoop(); });
        },

        _captureAndSend: function() {
            var video = this._videoElement;
            var ctx = this._captureCtx;
            var canvas = this._captureCanvas;
            if (!video || !ctx || !canvas) return;

            // Draw video frame to capture canvas
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

            this._pendingFrame = true;
            var self = this;

            canvas.toBlob(function(blob) {
                if (!blob || !self._active) { self._pendingFrame = false; return; }

                fetch(ApiClient.getUrl('Upscaler/upscale-frame'), {
                    method: 'POST',
                    body: blob
                }).then(function(resp) {
                    if (resp.status === 503) {
                        // Server busy, skip frame
                        self._pendingFrame = false;
                        return null;
                    }
                    if (!resp.ok) {
                        self._pendingFrame = false;
                        return null;
                    }
                    return resp.blob();
                }).then(function(resultBlob) {
                    if (!resultBlob || !self._active) { self._pendingFrame = false; return; }

                    var img = new Image();
                    img.onload = function() {
                        // Revoke object URL immediately after decode to prevent memory leak
                        URL.revokeObjectURL(img.src);
                        self._currentObjectUrl = null;

                        if (self._overlayCanvas && self._active) {
                            // Resize overlay to match result
                            if (self._overlayCanvas.width !== img.width || self._overlayCanvas.height !== img.height) {
                                self._overlayCanvas.width = img.width;
                                self._overlayCanvas.height = img.height;
                            }
                            if (!self._overlayCtx) self._overlayCtx = self._overlayCanvas.getContext('2d');
                            self._overlayCtx.drawImage(img, 0, 0);
                            self._lastSuccessfulFrame = performance.now();

                            // FPS tracking
                            self._fpsFrameCount++;
                            var now = performance.now();
                            if (now - self._fpsLastTime >= 1000) {
                                self._currentFps = Math.round(self._fpsFrameCount * 1000 / (now - self._fpsLastTime));
                                self._fpsFrameCount = 0;
                                self._fpsLastTime = now;
                                self._updateFpsDisplay();

                                // Auto-fallback: if FPS < 10 for 3 seconds → switch to WebGL
                                if (self._currentFps < 10) {
                                    if (!self._lowFpsStart) self._lowFpsStart = now;
                                    else if (now - self._lowFpsStart > 3000) {
                                        console.log('AI Upscaler RT: Server FPS too low, switching to WebGL');
                                        self._stopServer();
                                        self._mode = 'webgl';
                                        self._startWebGL();
                                        self._updateButtonIndicator('webgl');
                                        if (window.PlayerIntegration) {
                                            window.PlayerIntegration.showPlayerNotification('Switched to WebGL (server too slow)', 'warning');
                                        }
                                    }
                                } else {
                                    self._lowFpsStart = 0;
                                }
                            }
                        }
                        self._pendingFrame = false;
                    };
                    img.onerror = function() {
                        URL.revokeObjectURL(img.src);
                        self._currentObjectUrl = null;
                        self._pendingFrame = false;
                    };
                    // Revoke previous URL if still pending (safety net)
                    if (self._currentObjectUrl) {
                        URL.revokeObjectURL(self._currentObjectUrl);
                    }
                    self._currentObjectUrl = URL.createObjectURL(resultBlob);
                    img.src = self._currentObjectUrl;
                }).catch(function() {
                    self._pendingFrame = false;
                });
            }, 'image/jpeg', 0.85);
        },

        // --- UI ---
        _createFpsOverlay: function() {
            this._removeFpsOverlay();
            var el = document.createElement('div');
            el.id = 'aiUpscalerFpsOverlay';
            el.style.cssText = 'position:fixed;top:10px;left:10px;z-index:100002;padding:4px 10px;' +
                'background:rgba(0,0,0,0.7);color:#34d399;font-size:12px;font-family:monospace;' +
                'border-radius:6px;pointer-events:none;backdrop-filter:blur(6px);';
            el.textContent = 'AI --fps';
            document.body.appendChild(el);
        },

        _removeFpsOverlay: function() {
            var el = document.getElementById('aiUpscalerFpsOverlay');
            if (el) el.remove();
        },

        _updateFpsDisplay: function() {
            var el = document.getElementById('aiUpscalerFpsOverlay');
            if (!el) return;
            var modeLabel = this._mode === 'server' ? 'Server' : 'WebGL';
            var modelLabel = '';
            if (this._mode === 'server' && this._benchmarkResult && this._benchmarkResult.model) {
                modelLabel = ' ' + this._benchmarkResult.model;
            }
            el.textContent = 'AI ' + this._currentFps + 'fps | ' + modeLabel + modelLabel;
            el.style.color = this._currentFps >= 20 ? '#34d399' : this._currentFps >= 10 ? '#fbbf24' : '#ef4444';
        },

        _updateButtonIndicator: function(mode) {
            var btn = document.getElementById('aiUpscalerButton');
            if (!btn) return;
            // Remove old indicator
            var old = btn.querySelector('.ai-rt-dot');
            if (old) old.remove();

            if (!mode) return;
            var dot = document.createElement('span');
            dot.className = 'ai-rt-dot';
            dot.style.cssText = 'position:absolute;top:2px;right:2px;width:8px;height:8px;border-radius:50%;';
            dot.style.background = mode === 'server' ? '#34d399' : '#60a5fa';
            btn.style.position = 'relative';
            btn.appendChild(dot);
        },

        getStatus: function() {
            return {
                active: this._active,
                mode: this._mode,
                fps: this._currentFps,
                benchmark: this._benchmarkResult
            };
        }
    };

    window.RealtimeUpscaler = RealtimeUpscaler;

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
                        // Start real-time upscaling after 1s settle time
                        setTimeout(function() { PlayerIntegration.startRealtimeUpscaling(); }, 1000);
                    });
                    window.playbackManager.addEventListener('playbackstop', function() {
                        PlayerIntegration._buttonInjected = false;
                        RealtimeUpscaler.stop();
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
                    return window.ApiClient.updatePluginConfiguration(PLUGIN_ID, newConfig);
                }).catch(function(err) {
                    console.error('Failed to save plugin config:', err);
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
            }).catch(function(err) {
                console.error('Failed to load plugin config for menu:', err);
                // Build menu with defaults so button is never silently dead
                PlayerIntegration._buildMenu({});
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
                        '<div class="ai-menu__section-title">Real-Time Upscaling</div>' +
                        '<div class="ai-menu__rt-status" id="aiRtStatus">' +
                            '<span class="ai-menu__rt-label">Status:</span> ' +
                            '<span class="ai-menu__rt-value" id="aiRtStatusValue">--</span>' +
                        '</div>' +
                        '<div class="ai-menu__rt-row">' +
                            '<button class="ai-menu__rt-btn" data-action="rt-toggle">Toggle</button>' +
                            '<button class="ai-menu__rt-btn" data-action="rt-switch">Switch Mode</button>' +
                        '</div>' +
                    '</div>' +
                    '<div class="ai-menu__section">' +
                        '<button class="ai-menu__action" data-action="config">' +
                            '<span class="material-icons" style="font-size:16px;margin-right:8px">settings</span>' +
                            'Full Configuration' +
                        '</button>' +
                    '</div>' +
                '</div>';

            // After DOM insertion, update RT status
            setTimeout(function() {
                var statusEl = document.getElementById('aiRtStatusValue');
                if (statusEl) {
                    var st = RealtimeUpscaler.getStatus();
                    if (st.active) {
                        statusEl.textContent = st.mode.toUpperCase() + ' — ' + st.fps + ' fps';
                        statusEl.style.color = '#34d399';
                    } else {
                        statusEl.textContent = 'Inactive';
                        statusEl.style.color = 'rgba(255,255,255,0.4)';
                    }
                }
            }, 50);

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
                    } else if (action === 'rt-toggle') {
                        if (RealtimeUpscaler._active) {
                            RealtimeUpscaler.stop();
                            PlayerIntegration.showPlayerNotification('Real-time upscaling stopped', 'warning');
                        } else {
                            PlayerIntegration.startRealtimeUpscaling();
                            PlayerIntegration.showPlayerNotification('Real-time upscaling starting...', 'success');
                        }
                        menu.remove();
                        PlayerIntegration._cleanupMenu();
                    } else if (action === 'rt-switch') {
                        if (RealtimeUpscaler._active) {
                            var newMode = RealtimeUpscaler._mode === 'server' ? 'webgl' : 'server';
                            var bench = RealtimeUpscaler._benchmarkResult;
                            RealtimeUpscaler.stop();
                            var video = PlayerIntegration.findVideoElement();
                            if (video) {
                                var overrideConfig = Object.assign({}, PlayerIntegration._cachedConfig || {}, { RealtimeMode: newMode });
                                RealtimeUpscaler.start(video, overrideConfig, bench);
                            }
                            PlayerIntegration.showPlayerNotification('Switched to ' + newMode.toUpperCase(), 'info');
                        }
                        menu.remove();
                        PlayerIntegration._cleanupMenu();
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
            }).catch(function(err) {
                console.error('AI Upscaler: config fetch failed', err);
                PlayerIntegration.showPlayerNotification('Failed to toggle upscaling', 'error');
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

        findVideoElement: function() {
            return document.querySelector('video') ||
                   document.querySelector('#videoOsdPage video') ||
                   document.querySelector('.htmlvideoplayer video');
        },

        startRealtimeUpscaling: function() {
            this.getPluginConfig().then(function(config) {
                if (config.EnableRealtimeUpscaling === false) return;

                var video = PlayerIntegration.findVideoElement();
                if (!video) {
                    console.log('AI Upscaler RT: No video element found');
                    return;
                }

                var mode = (config.RealtimeMode || 'auto').toLowerCase();

                // Skip upscaling if source resolution is already high enough
                if (video.videoWidth >= 1920) {
                    console.log('AI Upscaler RT: Source resolution already high enough (' + video.videoWidth + 'x' + video.videoHeight + '), skipping real-time upscaling');
                    return;
                }

                // If mode is auto or server, run benchmark first
                if (mode === 'auto' || mode === 'server') {
                    var captureW = config.RealtimeCaptureWidth || 480;
                    var captureH = Math.round(captureW * (video.videoHeight / video.videoWidth));
                    fetch(ApiClient.getUrl('Upscaler/benchmark-frame') + '?width=' + captureW + '&height=' + captureH)
                        .then(function(r) { return r.json(); })
                        .then(function(bench) {
                            console.log('AI Upscaler RT: Benchmark result', bench);
                            RealtimeUpscaler.start(video, config, bench);
                        })
                        .catch(function(err) {
                            console.warn('AI Upscaler RT: Benchmark failed, using WebGL', err);
                            RealtimeUpscaler.start(video, config, { error: 'benchmark failed' });
                        });
                } else {
                    // WebGL mode, no benchmark needed
                    RealtimeUpscaler.start(video, config, null);
                }
            }).catch(function(err) {
                console.error('AI Upscaler: config fetch failed for RT upscaling', err);
            });
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

            // Clean up observer on SPA page navigation
            if (!this._viewHideCleanupBound) {
                this._viewHideCleanupBound = true;
                document.addEventListener('viewbeforehide', function() {
                    if (PlayerIntegration._mutationObserver) {
                        PlayerIntegration._mutationObserver.disconnect();
                        PlayerIntegration._mutationObserver = null;
                    }
                    PlayerIntegration._buttonInjected = false;
                });
            }
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
                '}' +

                /* Real-Time section */
                '.ai-menu__rt-status {' +
                    'padding: 6px 10px;' +
                    'font-size: 12px;' +
                    'color: rgba(255,255,255,0.6);' +
                '}' +
                '.ai-menu__rt-label { color: rgba(255,255,255,0.4); }' +
                '.ai-menu__rt-value { font-weight: 600; }' +
                '.ai-menu__rt-row {' +
                    'display: flex;' +
                    'gap: 6px;' +
                    'padding: 0 4px;' +
                '}' +
                '.ai-menu__rt-btn {' +
                    'flex: 1;' +
                    'padding: 7px;' +
                    'background: rgba(255,255,255,0.04);' +
                    'border: 1px solid rgba(255,255,255,0.1);' +
                    'border-radius: 8px;' +
                    'color: rgba(255,255,255,0.7);' +
                    'font-size: 11px;' +
                    'font-weight: 600;' +
                    'cursor: pointer;' +
                    'transition: all 0.15s;' +
                '}' +
                '.ai-menu__rt-btn:hover {' +
                    'background: rgba(139,92,246,0.15);' +
                    'border-color: rgba(139,92,246,0.3);' +
                    'color: #fff;' +
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
