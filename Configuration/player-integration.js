// AI Upscaler Plugin - Player Integration v1.6.1.15
// Global script injection (loaded via index.html like Intro Skipper)
// Compatible with Jellyfin 10.11+

(function() {
    'use strict';

    // Plugin configuration
    const PLUGIN_ID = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';
    const PLUGIN_VERSION = '1.6.1.15';

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

    // ── v1.6.1.13: Live filter overlay (CSS filter on <video>) ────────────
    //
    // Client-side only — each preset maps to a CSS `filter` string applied directly
    // to the playing <video> element. No transcode, no AI service. FFmpeg presets
    // in VideoFilterService.cs are visually close but not pixel-identical: CSS can't
    // express curves / LUTs / vignette / film grain. Those live in the Advanced pane
    // and apply via the server filter chain on next seek.
    const PRESET_CSS = {
        'none':        '',
        'cinematic':   'brightness(0.96) contrast(1.25) saturate(1.15)',
        'vintage':     'contrast(1.1) saturate(0.65) sepia(0.3)',
        'vivid':       'contrast(1.1) saturate(1.4)',
        'noir':        'contrast(1.3) saturate(0) brightness(1.05)',
        'warm':        'saturate(1.08) sepia(0.08) brightness(1.02)',
        'cool':        'saturate(0.9) hue-rotate(-5deg) brightness(0.98)',
        'hdr-pop':     'contrast(1.3) saturate(1.25) brightness(1.06)',
        'sepia':       'sepia(0.85) saturate(1.2)',
        'pastel':      'saturate(0.75) brightness(1.08) contrast(0.95)',
        'cyberpunk':   'contrast(1.3) saturate(1.5) hue-rotate(10deg)',
        'drama':       'contrast(1.4) saturate(0.8) brightness(0.92)',
        'soft-glow':   'brightness(1.1) contrast(0.95) saturate(1.05) blur(0.5px)',
        'sharp-hd':    'contrast(1.15) saturate(1.1)',
        'retrogame':   'saturate(1.3) contrast(1.2) brightness(1.02)',
        'teal-orange': 'contrast(1.15) saturate(1.2) hue-rotate(-5deg)'
    };

    // Ordered list for chip rendering. 'custom' is omitted — it's implied whenever
    // a live slider moves off zero.
    const PRESET_LABELS = [
        ['none', 'None'],
        ['cinematic', 'Cinematic'], ['vintage', 'Vintage'], ['vivid', 'Vivid'],
        ['noir', 'Noir'], ['warm', 'Warm'], ['cool', 'Cool'], ['hdr-pop', 'HDR Pop'],
        ['sepia', 'Sepia'], ['pastel', 'Pastel'], ['cyberpunk', 'Cyberpunk'],
        ['drama', 'Drama'], ['soft-glow', 'Soft Glow'], ['sharp-hd', 'Sharp HD'],
        ['retrogame', 'Retro'], ['teal-orange', 'Teal/Orange']
    ];

    // The 3 sliders that drive live CSS. Each owns a CSS formatter so the value
    // stays in UI-space (-100..100 etc.) and converts to the float CSS expects
    // only at apply time.
    const LIVE_SLIDERS = [
        { key: 'brightness', label: 'Brightness', min: -50, max: 50, def: 0, icon: 'brightness_6',
          toCss: function(v) { return 'brightness(' + (1 + v * 0.01).toFixed(2) + ')'; } },
        { key: 'contrast',   label: 'Contrast',   min: -50, max: 50, def: 0, icon: 'contrast',
          toCss: function(v) { return 'contrast(' + (1 + v * 0.01).toFixed(2) + ')'; } },
        { key: 'saturation', label: 'Saturation', min: -100, max: 100, def: 0, icon: 'palette',
          toCss: function(v) { return 'saturate(' + (1 + v * 0.01).toFixed(2) + ')'; } }
    ];


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
            if (window.AIUpscalerWebGL) { callback(); return; }
            if (document.querySelector('script[data-upscaler-webgl]')) {
                setTimeout(callback, 500);
                return;
            }
            var script = document.createElement('script');
            script.src = '/web/configurationpage?name=UPSCALERWebGLShader';
            script.setAttribute('data-upscaler-webgl', '1');
            script.onload = function() { setTimeout(callback, 100); };
            script.onerror = function() {
                console.warn('AI Upscaler RT: Could not load WebGL shader from', script.src);
            };
            document.head.appendChild(script);
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
            this._serverStartTime = performance.now();
            this._serverRenderLoop();
            // Timer-based fallback check: if no successful frame for 10 seconds, switch to WebGL.
            // The grace window accounts for slow first-frame warmup on CPU backends (fsrcnn-x2
            // on 4-core CPU takes ~250ms/frame cold, plus model-load race on fresh sessions).
            this._fallbackCheckInterval = setInterval(function() {
                if (!RealtimeUpscaler._active || RealtimeUpscaler._mode !== 'server') {
                    clearInterval(RealtimeUpscaler._fallbackCheckInterval);
                    return;
                }
                if (performance.now() - RealtimeUpscaler._lastSuccessfulFrame > 10000) {
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
                    headers: { 'Authorization': 'MediaBrowser Token="' + ApiClient.accessToken() + '"' },
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
                'border-radius:6px;pointer-events:none;backdrop-filter:blur(6px);' +
                'opacity:0;transition:opacity .18s ease;';
            el.textContent = 'AI --fps';
            document.body.appendChild(el);

            // Auto-hide with Jellyfin's OSD: only show HUD while playback controls are visible.
            // Jellyfin fades .videoOsdBottom / .osdControls via opacity when idle; we mirror that.
            var self = this;
            this._fpsVisSync = setInterval(function() {
                if (!el.isConnected) return;
                var osd = document.querySelector('.videoOsdBottom, .osdControls');
                var visible = osd && osd.offsetParent !== null &&
                              parseFloat(getComputedStyle(osd).opacity || '0') > 0.1;
                el.style.opacity = visible ? '1' : '0';
            }, 200);
        },

        _removeFpsOverlay: function() {
            if (this._fpsVisSync) { clearInterval(this._fpsVisSync); this._fpsVisSync = null; }
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
        _modelStates: null,

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
                // Modern Jellyfin (10.11+) no longer exposes window.playbackManager,
                // so playbackstart listener never fires. Wait for video element + playing event instead.
                this._waitForVideoAndAutoStart();
            } else {
                // Leaving video page — stop upscaling
                if (window.RealtimeUpscaler && window.RealtimeUpscaler._active) {
                    window.RealtimeUpscaler.stop();
                }
            }
        },

        _waitForVideoAndAutoStart: function() {
            if (this._autoStartPending) return;
            this._autoStartPending = true;
            var self = this;
            var retries = 0;
            var maxRetries = 60; // 30s @ 500ms
            var check = function() {
                var v = self.findVideoElement();
                if (v) {
                    self._autoStartPending = false;
                    var trigger = function() {
                        if (window.location.hash.indexOf('#/video') !== 0) return;
                        setTimeout(function() { self.startRealtimeUpscaling(); }, 600);
                    };
                    if (v.readyState >= 2 && !v.paused) {
                        trigger();
                    } else {
                        v.addEventListener('playing', trigger, { once: true });
                    }
                    return;
                }
                if (++retries < maxRetries) {
                    setTimeout(check, 500);
                } else {
                    self._autoStartPending = false;
                }
            };
            check();
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
            if (!window.ApiClient) return Promise.reject(new Error('ApiClient unavailable'));
            return this.getPluginConfig().then(function(config) {
                var newConfig = Object.assign({}, config, updates);
                PlayerIntegration._cachedConfig = newConfig;
                PlayerIntegration._configCacheTime = Date.now();
                return window.ApiClient.updatePluginConfiguration(PLUGIN_ID, newConfig);
            });
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
            if (this._statusPollTimer) {
                clearInterval(this._statusPollTimer);
                this._statusPollTimer = null;
            }
        },

        // Refresh the live status row — state/mode/fps/model. Runs every 500ms while menu is open.
        // Reads truth from RealtimeUpscaler (active flag, mode, _currentFps, _benchmarkResult.model)
        // and falls back to the configured model when idle. No placeholders — the RT engine tracks
        // FPS in requestAnimationFrame (WebGL) or from the server capture loop.
        _refreshStatusRow: function() {
            var menu = document.querySelector('#aiUpscalerQuickMenu');
            if (!menu) return;
            var dot = menu.querySelector('[data-status-dot]');
            var stateEl = menu.querySelector('[data-status-state]');
            var modeEl = menu.querySelector('[data-status-mode]');
            var fpsEl = menu.querySelector('[data-status-fps]');
            var modelEl = menu.querySelector('[data-status-model]');
            if (!dot || !stateEl || !modeEl || !fpsEl || !modelEl) return;

            var rt = window.RealtimeUpscaler;
            var st = rt && typeof rt.getStatus === 'function' ? rt.getStatus() : null;
            var cfg = this._cachedConfig || {};
            var video = document.querySelector('video');
            var playing = video && !video.paused && !video.ended && video.readyState >= 2;

            if (st && st.active) {
                dot.className = 'ai-menu__status-dot ai-menu__status-dot--on';
                stateEl.textContent = 'ACTIVE';
                modeEl.textContent = st.mode === 'server' ? 'Server' : 'WebGL';
                var fps = st.fps | 0;
                fpsEl.innerHTML = '<b>' + fps + '</b> fps';
                fpsEl.className = 'ai-menu__status-fps ' +
                    (fps >= 20 ? '' : fps >= 10 ? 'ai-menu__status-fps--warn' : 'ai-menu__status-fps--err');
                var mName = (st.benchmark && st.benchmark.model) || cfg.Model || '—';
                modelEl.textContent = mName;
            } else if (playing && cfg.EnableUpscaling === false) {
                dot.className = 'ai-menu__status-dot ai-menu__status-dot--off';
                stateEl.textContent = 'DISABLED';
                modeEl.textContent = '—';
                fpsEl.textContent = '-- fps';
                modelEl.textContent = cfg.Model || '—';
            } else if (playing) {
                dot.className = 'ai-menu__status-dot ai-menu__status-dot--warn';
                stateEl.textContent = 'STANDBY';
                modeEl.textContent = '—';
                fpsEl.textContent = '-- fps';
                modelEl.textContent = cfg.Model || '—';
            } else {
                dot.className = 'ai-menu__status-dot ai-menu__status-dot--off';
                stateEl.textContent = 'IDLE';
                modeEl.textContent = '—';
                fpsEl.textContent = '-- fps';
                modelEl.textContent = cfg.Model || '—';
            }
        },

        _startStatusPoll: function() {
            var self = this;
            if (this._statusPollTimer) clearInterval(this._statusPollTimer);
            this._refreshStatusRow();
            this._statusPollTimer = setInterval(function() { self._refreshStatusRow(); }, 500);
        },

        toggleUpscalerMenu: function() {
            var existing = document.querySelector('#aiUpscalerQuickMenu');
            if (existing) {
                existing.remove();
                this._cleanupMenu();
                return;
            }

            // Read config first, then fetch model download states in parallel
            this.getPluginConfig().then(function(config) {
                PlayerIntegration._buildMenu(config, null);
                PlayerIntegration._fetchModelStates().then(function(states) {
                    PlayerIntegration._refreshModelStates(states);
                });
            }).catch(function(err) {
                console.error('Failed to load plugin config for menu:', err);
                PlayerIntegration._buildMenu({}, null);
            });
        },

        _fetchModelStates: function() {
            // Use ApiClient.ajax so the X-Emby-Token header is attached automatically.
            // Raw fetch() against /Upscaler/* returns 401 because the [Authorize] controller
            // needs Jellyfin's session token and ApiClient.getRequestHeader() doesn't exist.
            return ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('Upscaler/models'),
                dataType: 'json'
            }).then(function(data) {
                var map = {};
                (data.models || []).forEach(function(m) {
                    map[m.id] = { downloaded: !!m.downloaded, available: m.available !== false, loaded: !!m.loaded };
                });
                return map;
            }).catch(function(err) {
                console.warn('AI Upscaler: could not fetch model states —', err && (err.message || err.status || err));
                return null;
            });
        },

        _renderModelCard: function(m, isActive, state) {
            var stateIcon, stateClass, title;
            if (state && !state.available) {
                stateIcon = '&#9888;'; stateClass = 'err'; title = 'Not yet available';
            } else if (state && state.downloaded) {
                stateIcon = '&#10003;'; stateClass = 'ready'; title = 'Downloaded & ready';
            } else if (state) {
                stateIcon = '&#8595;'; stateClass = 'need-dl'; title = 'Click to download & load';
            } else {
                stateIcon = '&#8226;'; stateClass = 'need-dl'; title = 'Status unknown';
            }
            var html = '<button class="ai-menu__model' + (isActive ? ' ai-menu__model--active' : '') +
                '" data-model="' + m.id + '" data-scale="' + m.scale + '" title="' + title + '">';
            html += '<span class="ai-menu__state ai-menu__state--' + stateClass + '" data-state-slot="' + m.id + '">' + stateIcon + '</span>';
            html += '<span class="ai-menu__model-name">' + m.name + '</span>';
            if (m.badge) html += '<span class="ai-menu__badge">' + m.badge + '</span>';
            html += '<span class="ai-menu__model-scale">' + m.scale + 'x</span>';
            html += '</button>';
            return html;
        },

        _refreshModelStates: function(states) {
            if (!states) return;
            var menu = document.querySelector('#aiUpscalerQuickMenu');
            if (!menu) return;
            PlayerIntegration._modelStates = states;

            // Update summary counter
            var total = 0, ready = 0;
            Object.keys(states).forEach(function(k) {
                total++;
                if (states[k].downloaded) ready++;
            });
            var summ = menu.querySelector('[data-summary-ready]');
            if (summ) summ.textContent = ready + ' of ' + total;

            // Update each state icon
            Object.keys(states).forEach(function(id) {
                var slot = menu.querySelector('[data-state-slot="' + id + '"]');
                if (!slot) return;
                var s = states[id];
                slot.classList.remove('ai-menu__state--ready','ai-menu__state--need-dl','ai-menu__state--busy','ai-menu__state--err');
                if (!s.available) { slot.classList.add('ai-menu__state--err'); slot.innerHTML = '&#9888;'; }
                else if (s.downloaded) { slot.classList.add('ai-menu__state--ready'); slot.innerHTML = '&#10003;'; }
                else { slot.classList.add('ai-menu__state--need-dl'); slot.innerHTML = '&#8595;'; }
            });
        },

        _applyChipFilter: function(menu, filter) {
            menu.querySelectorAll('.ai-menu__chip').forEach(function(c) {
                c.classList.toggle('ai-menu__chip--active', c.getAttribute('data-filter') === filter);
            });
            var states = PlayerIntegration._modelStates || {};
            menu.querySelectorAll('.ai-menu__model').forEach(function(btn) {
                var id = btn.getAttribute('data-model');
                var s = states[id] || {};
                var show = true;
                if (filter === 'ready') show = !!s.downloaded;
                else if (filter === 'recommended') show = ['realesrgan-x4','span-x2','clearreality-x4','ultrasharp-v2-x4','fsrcnn-x2'].indexOf(id) !== -1;
                btn.style.display = show ? '' : 'none';
            });
            menu.querySelectorAll('.ai-menu__cat').forEach(function(cat) {
                var visible = cat.querySelectorAll('.ai-menu__model:not([style*="display: none"])').length;
                cat.style.display = visible > 0 ? '' : 'none';
            });
        },

        _buildMenu: function(config, modelStates) {
            var position = (config.ButtonPosition || 'right').toLowerCase();
            var currentModel = config.Model || 'realesrgan-x4';
            var currentScale = config.ScaleFactor || 2;
            var isEnabled = config.EnablePlugin !== false;

            var menu = document.createElement('div');
            menu.id = 'aiUpscalerQuickMenu';
            menu.className = 'ai-menu ai-menu--' + position;

            // Build category groups with state-aware model cards
            var modelsHtml = '';
            var cats = Object.keys(MODEL_CATALOG);
            for (var ci = 0; ci < cats.length; ci++) {
                var catKey = cats[ci];
                var cat = MODEL_CATALOG[catKey];
                modelsHtml += '<div class="ai-menu__cat" data-cat="' + catKey + '">';
                modelsHtml += '<div class="ai-menu__cat-head">';
                modelsHtml += '<span class="ai-menu__cat-name">' + cat.label + '</span>';
                modelsHtml += '<span class="ai-menu__cat-desc">' + cat.desc + '</span>';
                modelsHtml += '</div>';
                for (var mi = 0; mi < cat.models.length; mi++) {
                    var m = cat.models[mi];
                    var isActive = m.id === currentModel;
                    var st = modelStates ? modelStates[m.id] : null;
                    modelsHtml += this._renderModelCard(m, isActive, st);
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

            // Count models for summary strip
            var totalModels = 0, readyModels = 0;
            if (modelStates) {
                Object.keys(modelStates).forEach(function(k) {
                    totalModels++;
                    if (modelStates[k].downloaded) readyModels++;
                });
            }

            // v1.6.1.13: tab panes replace the flat scroll. Each pane is rendered
            // but only the active one is visible — see ai-menu__pane--active in CSS.
            // Filter state is seeded synchronously from cached config + defaults;
            // then _loadFilterConfig() refreshes it from the server after menu mount.
            var filterState = this._filterState || this._defaultFilterState();
            var filtersHtml = this._buildFiltersPane(filterState);

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
                        '<button class="ai-menu__switch' + (isEnabled ? ' ai-menu__switch--on' : '') + '" data-action="toggle" aria-label="Toggle upscaling" title="' + (isEnabled ? 'Disable' : 'Enable') + ' upscaling"></button>' +
                        '<button class="ai-menu__close" data-action="close" aria-label="Close">&times;</button>' +
                    '</div>' +
                '</div>' +
                '<div class="ai-menu__status" data-status-row>' +
                    '<span class="ai-menu__status-dot ai-menu__status-dot--off" data-status-dot></span>' +
                    '<span class="ai-menu__status-state" data-status-state>INACTIVE</span>' +
                    '<span class="ai-menu__status-sep">·</span>' +
                    '<span class="ai-menu__status-mode" data-status-mode>—</span>' +
                    '<span class="ai-menu__status-sep">·</span>' +
                    '<span class="ai-menu__status-fps" data-status-fps>-- fps</span>' +
                    '<span class="ai-menu__status-sep">·</span>' +
                    '<span class="ai-menu__status-model" data-status-model>' + currentModel + '</span>' +
                '</div>' +
                '<div class="ai-menu__summary">' +
                    '<span class="ai-menu__summary-dot' + (readyModels > 0 ? '' : ' ai-menu__summary-dot--off') + '"></span>' +
                    '<span><span class="ai-menu__summary-strong" data-summary-ready>' + (totalModels ? (readyModels + ' of ' + totalModels) : '—') + '</span> models ready</span>' +
                '</div>' +
                '<div class="ai-menu__tabs" role="tablist">' +
                    '<button class="ai-menu__tab ai-menu__tab--active" data-tab="models" role="tab"><span class="material-icons">view_module</span><span>Models</span></button>' +
                    '<button class="ai-menu__tab" data-tab="filters" role="tab"><span class="material-icons">tune</span><span>Filters</span><span class="ai-menu__tab-live" data-filter-live-dot></span></button>' +
                    '<button class="ai-menu__tab" data-tab="realtime" role="tab"><span class="material-icons">bolt</span><span>Realtime</span></button>' +
                '</div>' +
                '<div class="ai-menu__body">' +
                    '<div class="ai-menu__pane ai-menu__pane--active" data-pane="models">' +
                        '<div class="ai-menu__chips">' +
                            '<button class="ai-menu__chip ai-menu__chip--active" data-filter="all">All</button>' +
                            '<button class="ai-menu__chip" data-filter="ready">Downloaded</button>' +
                            '<button class="ai-menu__chip" data-filter="recommended">Recommended</button>' +
                        '</div>' +
                        '<div class="ai-menu__section">' +
                            '<div class="ai-menu__section-title"><span>Models</span><span class="ai-menu__section-sub">Click to load</span></div>' +
                            '<div class="ai-menu__models">' + modelsHtml + '</div>' +
                        '</div>' +
                        '<div class="ai-menu__section">' +
                            '<div class="ai-menu__section-title"><span>Scale Factor</span><span class="ai-menu__section-sub">Output multiplier</span></div>' +
                            '<div class="ai-menu__scales">' + scaleHtml + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="ai-menu__pane" data-pane="filters">' + filtersHtml + '</div>' +
                    '<div class="ai-menu__pane" data-pane="realtime">' +
                        '<div class="ai-menu__section">' +
                            '<div class="ai-menu__section-title"><span>Real-Time Upscaling</span></div>' +
                            '<div class="ai-menu__rt-card">' +
                                '<div class="ai-menu__rt-status">' +
                                    '<span class="ai-menu__rt-indicator" id="aiRtIndicator"></span>' +
                                    '<span class="ai-menu__rt-label">Status:</span>' +
                                    '<span class="ai-menu__rt-value" id="aiRtStatusValue">--</span>' +
                                '</div>' +
                                '<div class="ai-menu__rt-row">' +
                                    '<button class="ai-menu__rt-btn" data-action="rt-toggle">Toggle</button>' +
                                    '<button class="ai-menu__rt-btn" data-action="rt-switch">Switch Mode</button>' +
                                '</div>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="ai-menu__footer">' +
                        '<button class="ai-menu__action" data-action="config">' +
                            '<span class="material-icons" style="font-size:16px;margin-right:8px">settings</span>' +
                            'Full Configuration' +
                        '</button>' +
                    '</div>' +
                '</div>';

            // After DOM insertion, update RT status
            setTimeout(function() {
                var statusEl = document.getElementById('aiRtStatusValue');
                var indicator = document.getElementById('aiRtIndicator');
                if (statusEl) {
                    var st = RealtimeUpscaler.getStatus();
                    if (st.active) {
                        statusEl.textContent = st.mode.toUpperCase() + ' · ' + st.fps + ' fps';
                        statusEl.style.color = '#34d399';
                        if (indicator) indicator.classList.add('ai-menu__rt-indicator--on');
                    } else {
                        statusEl.textContent = 'Inactive';
                        statusEl.style.color = 'rgba(255,255,255,0.4)';
                        if (indicator) indicator.classList.remove('ai-menu__rt-indicator--on');
                    }
                }
            }, 50);

            document.body.appendChild(menu);

            // v1.6.1.13: apply any previously-chosen CSS filter and refresh the state
            // from the server so the filter pane shows the current saved preset.
            this._applyFilterState(filterState);
            this._loadFilterConfig();

            // Any interaction on the menu keeps it alive — important for filter sliders
            // where users may drag for several seconds. _touchMenuTimer resets the auto-close.
            var touchTimer = function() { PlayerIntegration._touchMenuTimer(menu); };
            menu.addEventListener('pointerdown', touchTimer);
            menu.addEventListener('input', function(e) {
                touchTimer();
                var slider = e.target.closest('[data-slider]');
                if (slider) {
                    PlayerIntegration._onSliderInput(menu, slider);
                }
            });
            menu.addEventListener('change', function(e) {
                var adv = e.target.closest('[data-adv-slider]');
                if (adv) PlayerIntegration._onAdvSliderChange(menu, adv);
            });

            // Event delegation (click)
            menu.addEventListener('click', function(e) {
                touchTimer();

                var tab = e.target.closest('[data-tab]');
                if (tab) {
                    PlayerIntegration._switchTab(menu, tab.getAttribute('data-tab'));
                    return;
                }
                var presetBtn = e.target.closest('[data-preset]');
                if (presetBtn) {
                    PlayerIntegration._pickPreset(menu, presetBtn.getAttribute('data-preset'));
                    return;
                }
                var chip = e.target.closest('[data-filter]');
                if (chip) {
                    PlayerIntegration._applyChipFilter(menu, chip.getAttribute('data-filter'));
                    return;
                }
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
                    } else if (action === 'filter-reset') {
                        PlayerIntegration._resetFilters(menu);
                    } else if (action === 'filter-save') {
                        PlayerIntegration._saveFilterConfig(menu, target);
                    } else if (action === 'filter-advanced-toggle') {
                        var adv = menu.querySelector('[data-adv-pane]');
                        if (adv) adv.classList.toggle('ai-menu__adv--open');
                        var caret = target.querySelector('.ai-menu__adv-caret');
                        if (caret) caret.textContent = adv && adv.classList.contains('ai-menu__adv--open') ? 'expand_less' : 'expand_more';
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

            // Auto-close after 30s of no interaction. _touchMenuTimer resets this on
            // any click/input — important for filter sliders that users drag for seconds.
            this._touchMenuTimer(menu);

            // Start live status poll so users can see upscaling state + FPS + model
            // update in real time while the menu is open.
            this._startStatusPoll();
        },

        _touchMenuTimer: function(menu) {
            if (this._menuAutoCloseTimer) clearTimeout(this._menuAutoCloseTimer);
            this._menuAutoCloseTimer = setTimeout(function() {
                if (menu && menu.parentElement) menu.remove();
                PlayerIntegration._cleanupMenu();
            }, 30000);
        },

        // ── v1.6.1.13: filter pane ───────────────────────────────────────
        _defaultFilterState: function() {
            return {
                enabled: false,
                preset: 'none',
                brightness: 0, contrast: 0, saturation: 0,
                gamma: 1.0, sharpness: 0, colorTemperature: 6500,
                vignette: 0, filmGrain: 0, denoise: 0,
                canSave: false // set true after GET /filter-config response + admin check
            };
        },

        _buildFiltersPane: function(st) {
            // Preset chip grid
            var chipsHtml = '';
            for (var i = 0; i < PRESET_LABELS.length; i++) {
                var key = PRESET_LABELS[i][0];
                var label = PRESET_LABELS[i][1];
                var active = (st.preset === key) ? ' ai-menu__preset--active' : '';
                chipsHtml += '<button class="ai-menu__preset' + active + '" data-preset="' + key + '">' + label + '</button>';
            }

            // Live sliders (CSS filter on <video>)
            var slidersHtml = '';
            for (var j = 0; j < LIVE_SLIDERS.length; j++) {
                var s = LIVE_SLIDERS[j];
                var v = st[s.key];
                slidersHtml +=
                    '<div class="ai-menu__slider-row">' +
                        '<label class="ai-menu__slider-label">' +
                            '<span class="material-icons ai-menu__slider-icon">' + s.icon + '</span>' +
                            '<span>' + s.label + '</span>' +
                            '<span class="ai-menu__slider-val" data-slider-val="' + s.key + '">' + (v > 0 ? '+' : '') + v + '</span>' +
                        '</label>' +
                        '<input type="range" class="ai-menu__slider" data-slider="' + s.key + '"' +
                               ' min="' + s.min + '" max="' + s.max + '" step="1" value="' + v + '">' +
                    '</div>';
            }

            // Advanced (server-persisted) sliders — shown collapsed by default
            var adv = [
                { key: 'gamma', label: 'Gamma', min: 0.5, max: 2.5, step: 0.01, val: st.gamma },
                { key: 'sharpness', label: 'Sharpness', min: 0, max: 3, step: 0.1, val: st.sharpness },
                { key: 'colorTemperature', label: 'Color Temperature (K)', min: 3000, max: 10000, step: 100, val: st.colorTemperature },
                { key: 'vignette', label: 'Vignette', min: 0, max: 3, step: 0.1, val: st.vignette },
                { key: 'filmGrain', label: 'Film Grain', min: 0, max: 50, step: 1, val: st.filmGrain },
                { key: 'denoise', label: 'Denoise', min: 0, max: 10, step: 0.5, val: st.denoise }
            ];
            var advHtml = '';
            for (var k = 0; k < adv.length; k++) {
                var a = adv[k];
                advHtml +=
                    '<div class="ai-menu__adv-row">' +
                        '<label class="ai-menu__adv-label">' + a.label +
                            '<span class="ai-menu__adv-val" data-adv-val="' + a.key + '">' + a.val + '</span>' +
                        '</label>' +
                        '<input type="range" class="ai-menu__adv-slider" data-adv-slider="' + a.key + '"' +
                               ' min="' + a.min + '" max="' + a.max + '" step="' + a.step + '" value="' + a.val + '">' +
                    '</div>';
            }

            return '' +
                '<div class="ai-menu__section">' +
                    '<div class="ai-menu__section-title">' +
                        '<span>Look</span>' +
                        '<span class="ai-menu__section-sub">Live — no transcode</span>' +
                    '</div>' +
                    '<div class="ai-menu__presets">' + chipsHtml + '</div>' +
                '</div>' +
                '<div class="ai-menu__section">' +
                    '<div class="ai-menu__section-title"><span>Fine-tune</span></div>' +
                    '<div class="ai-menu__sliders">' + slidersHtml + '</div>' +
                '</div>' +
                '<div class="ai-menu__section">' +
                    '<button class="ai-menu__adv-toggle" data-action="filter-advanced-toggle">' +
                        '<span>Advanced (applies on next seek)</span>' +
                        '<span class="material-icons ai-menu__adv-caret">expand_more</span>' +
                    '</button>' +
                    '<div class="ai-menu__adv" data-adv-pane>' + advHtml + '</div>' +
                '</div>' +
                '<div class="ai-menu__filter-actions">' +
                    '<button class="ai-menu__filter-btn ai-menu__filter-btn--secondary" data-action="filter-reset">Reset</button>' +
                    '<button class="ai-menu__filter-btn ai-menu__filter-btn--primary" data-action="filter-save"' + (st.canSave ? '' : ' disabled title="Admin privileges required"') + '>Save</button>' +
                '</div>';
        },

        _switchTab: function(menu, tabName) {
            var tabs = menu.querySelectorAll('[data-tab]');
            var panes = menu.querySelectorAll('[data-pane]');
            for (var i = 0; i < tabs.length; i++) {
                tabs[i].classList.toggle('ai-menu__tab--active', tabs[i].getAttribute('data-tab') === tabName);
            }
            for (var j = 0; j < panes.length; j++) {
                panes[j].classList.toggle('ai-menu__pane--active', panes[j].getAttribute('data-pane') === tabName);
            }
        },

        _onSliderInput: function(menu, slider) {
            if (!this._filterState) this._filterState = this._defaultFilterState();
            var key = slider.getAttribute('data-slider');
            var v = parseInt(slider.value, 10) || 0;
            this._filterState[key] = v;
            // Any manual slider adjust transitions to 'custom' — clears preset highlight.
            this._filterState.preset = 'custom';
            this._filterState.enabled = true;
            var valEl = menu.querySelector('[data-slider-val="' + key + '"]');
            if (valEl) valEl.textContent = (v > 0 ? '+' : '') + v;
            var presets = menu.querySelectorAll('[data-preset]');
            for (var i = 0; i < presets.length; i++) presets[i].classList.remove('ai-menu__preset--active');
            this._applyFilterState(this._filterState);
        },

        _onAdvSliderChange: function(menu, slider) {
            // Advanced sliders don't drive live CSS — they apply server-side on next seek.
            if (!this._filterState) this._filterState = this._defaultFilterState();
            var key = slider.getAttribute('data-adv-slider');
            var v = parseFloat(slider.value);
            if (!isNaN(v)) this._filterState[key] = v;
            var valEl = menu.querySelector('[data-adv-val="' + key + '"]');
            if (valEl) valEl.textContent = (key === 'colorTemperature' || key === 'filmGrain') ? v.toFixed(0) : v.toFixed(2);
        },

        _pickPreset: function(menu, preset) {
            if (!this._filterState) this._filterState = this._defaultFilterState();
            this._filterState.preset = preset;
            this._filterState.enabled = (preset !== 'none');
            // Reset the 3 live sliders — presets define their own look.
            this._filterState.brightness = 0;
            this._filterState.contrast = 0;
            this._filterState.saturation = 0;
            // Reflect in UI
            var presets = menu.querySelectorAll('[data-preset]');
            for (var i = 0; i < presets.length; i++) {
                presets[i].classList.toggle('ai-menu__preset--active', presets[i].getAttribute('data-preset') === preset);
            }
            var sliders = menu.querySelectorAll('[data-slider]');
            for (var j = 0; j < sliders.length; j++) {
                sliders[j].value = 0;
                var k = sliders[j].getAttribute('data-slider');
                var valEl = menu.querySelector('[data-slider-val="' + k + '"]');
                if (valEl) valEl.textContent = '0';
            }
            this._applyFilterState(this._filterState);
        },

        _composeCssFromState: function(st) {
            // Preset takes priority if the 3 live sliders are all zero.
            var allSlidersZero = (st.brightness === 0 && st.contrast === 0 && st.saturation === 0);
            if (st.preset && st.preset !== 'custom' && st.preset !== 'none' && allSlidersZero) {
                return PRESET_CSS[st.preset] || '';
            }
            if (st.preset === 'none' && allSlidersZero) return '';
            // Otherwise compose from live sliders.
            var parts = [];
            for (var i = 0; i < LIVE_SLIDERS.length; i++) {
                var s = LIVE_SLIDERS[i];
                var v = st[s.key];
                if (v !== 0) parts.push(s.toCss(v));
            }
            return parts.join(' ');
        },

        _applyFilterState: function(st) {
            var video = this.findVideoElement();
            if (!video) return;
            var css = this._composeCssFromState(st);
            video.style.filter = css || '';
            this._updateLiveDot(css.length > 0);
        },

        _updateLiveDot: function(isLive) {
            var dot = document.querySelector('[data-filter-live-dot]');
            if (dot) dot.classList.toggle('ai-menu__tab-live--on', isLive);
        },

        _loadFilterConfig: function() {
            if (!window.ApiClient) return;
            var self = this;
            var url = ApiClient.getUrl('Upscaler/filter-config');
            ApiClient.ajax({ type: 'GET', url: url, dataType: 'json' }).then(function(resp) {
                if (!resp) return;
                // Merge server state into client state. We preserve any slider moves
                // the user already made this session (don't clobber active edits).
                var cur = self._filterState;
                if (!cur || (cur.brightness === 0 && cur.contrast === 0 && cur.saturation === 0)) {
                    self._filterState = {
                        enabled: !!resp.enabled,
                        preset: resp.preset || 'none',
                        brightness: 0, contrast: 0, saturation: 0,
                        gamma: resp.gamma || 1.0,
                        sharpness: resp.sharpness || 0,
                        colorTemperature: resp.colorTemperature || 6500,
                        vignette: resp.vignette || 0,
                        filmGrain: resp.filmGrain || 0,
                        denoise: resp.denoise || 0,
                        canSave: true // server responded — we'll let the POST discover non-admin
                    };
                    self._applyFilterState(self._filterState);
                    // Update UI to reflect loaded state
                    var menu = document.querySelector('#aiUpscalerQuickMenu');
                    if (menu) {
                        var presets = menu.querySelectorAll('[data-preset]');
                        for (var i = 0; i < presets.length; i++) {
                            presets[i].classList.toggle('ai-menu__preset--active', presets[i].getAttribute('data-preset') === self._filterState.preset);
                        }
                    }
                }
            }).catch(function(err) {
                console.warn('AI Upscaler: filter-config fetch failed:', err);
            });
        },

        _resetFilters: function(menu) {
            this._filterState = this._defaultFilterState();
            // Reflect default state in all controls
            var sliders = menu.querySelectorAll('[data-slider]');
            for (var i = 0; i < sliders.length; i++) {
                sliders[i].value = 0;
                var k = sliders[i].getAttribute('data-slider');
                var valEl = menu.querySelector('[data-slider-val="' + k + '"]');
                if (valEl) valEl.textContent = '0';
            }
            var presets = menu.querySelectorAll('[data-preset]');
            for (var j = 0; j < presets.length; j++) {
                presets[j].classList.toggle('ai-menu__preset--active', presets[j].getAttribute('data-preset') === 'none');
            }
            this._applyFilterState(this._filterState);
            this.showPlayerNotification('Filters reset', 'info');
        },

        _saveFilterConfig: function(menu, btn) {
            var st = this._filterState;
            if (!st) return;
            if (btn) { btn.disabled = true; btn.textContent = 'Saving...'; }
            var body = {
                enabled: st.enabled,
                preset: st.preset === 'custom' ? 'custom' : st.preset,
                brightness: Math.max(-1, Math.min(1, st.brightness * 0.02)),
                contrast: Math.max(0, Math.min(3, 1 + st.contrast * 0.02)),
                saturation: Math.max(0, Math.min(3, 1 + st.saturation * 0.01)),
                gamma: st.gamma,
                sharpness: st.sharpness,
                colorTemperature: Math.round(st.colorTemperature),
                vignette: st.vignette,
                filmGrain: Math.round(st.filmGrain),
                denoise: st.denoise
            };
            var url = ApiClient.getUrl('Upscaler/filter-config');
            ApiClient.ajax({
                type: 'POST', url: url, contentType: 'application/json',
                data: JSON.stringify(body), dataType: 'json'
            }).then(function() {
                PlayerIntegration.showPlayerNotification('Filter settings saved', 'success');
                if (btn) { btn.disabled = false; btn.textContent = 'Save'; }
            }).catch(function(err) {
                var msg = 'Save failed — admin privileges required';
                if (err && err.status && err.status !== 403) msg = 'Save failed: HTTP ' + err.status;
                PlayerIntegration.showPlayerNotification(msg, 'warning');
                if (btn) { btn.disabled = false; btn.textContent = 'Save'; }
            });
        },

        quickSetModel: function(model) {
            var self = this;
            var menu = document.querySelector('#aiUpscalerQuickMenu');
            var modelBtn = menu ? menu.querySelector('[data-model="' + model + '"]') : null;
            var slot = menu ? menu.querySelector('[data-state-slot="' + model + '"]') : null;
            var state = (this._modelStates && this._modelStates[model]) || null;
            var needsDownload = state && !state.downloaded && state.available;

            // Block if model is not available at all
            if (state && !state.available) {
                this.showPlayerNotification(model + ' is not yet available', 'warning');
                return;
            }

            // Show inline spinner on the clicked model; keep menu open
            if (modelBtn) modelBtn.classList.add('ai-menu__model--loading');
            if (slot) {
                slot.classList.remove('ai-menu__state--ready','ai-menu__state--need-dl','ai-menu__state--err');
                slot.classList.add('ai-menu__state--busy');
                slot.innerHTML = '<div class="ai-menu__spinner"></div>';
            }
            this.showPlayerNotification(
                (needsDownload ? 'Downloading ' : 'Loading ') + model + (needsDownload ? ' (may take 30-120s)' : '...'),
                'info'
            );

            this.updatePluginConfig({ Model: model }).then(function() {
                var loadUrl = ApiClient.getUrl('Upscaler/models/load') + '?model_name=' + encodeURIComponent(model);
                return fetch(loadUrl, {
                    method: 'POST',
                    headers: { 'Authorization': 'MediaBrowser Token="' + ApiClient.accessToken() + '"' },
                    credentials: 'include'
                }).then(function(r) {
                    if (!r.ok) {
                        return r.text().then(function(t) {
                            var detail = t;
                            try { var j = JSON.parse(t); detail = j.detail || j.message || t; } catch (e) {}
                            throw new Error('HTTP ' + r.status + ': ' + (detail || 'model load failed'));
                        });
                    }
                    return r.json().catch(function() { return {}; });
                });
            }).then(function() {
                // Update active styling + refresh states
                if (menu) {
                    menu.querySelectorAll('.ai-menu__model').forEach(function(b) { b.classList.remove('ai-menu__model--active'); });
                    if (modelBtn) {
                        modelBtn.classList.remove('ai-menu__model--loading');
                        modelBtn.classList.add('ai-menu__model--active');
                    }
                }
                if (slot) {
                    slot.classList.remove('ai-menu__state--busy');
                    slot.classList.add('ai-menu__state--ready');
                    slot.innerHTML = '&#10003;';
                }
                if (self._modelStates && self._modelStates[model]) {
                    self._modelStates[model].downloaded = true;
                    self._modelStates[model].loaded = true;
                }
                // Restart real-time upscaling if it was running
                if (RealtimeUpscaler._active) {
                    var bench = RealtimeUpscaler._benchmarkResult;
                    RealtimeUpscaler.stop();
                    var video = self.findVideoElement();
                    if (video) {
                        self.getPluginConfig().then(function(cfg) { RealtimeUpscaler.start(video, cfg, bench); });
                    }
                } else {
                    var video = self.findVideoElement();
                    if (video) self.startRealtimeUpscaling();
                }
                self.showPlayerNotification('Model ready: ' + model, 'success');
            }).catch(function(err) {
                console.error('AI Upscaler: quickSetModel failed', err);
                if (modelBtn) modelBtn.classList.remove('ai-menu__model--loading');
                if (slot) {
                    slot.classList.remove('ai-menu__state--busy');
                    slot.classList.add('ai-menu__state--err');
                    slot.innerHTML = '&#9888;';
                }
                var msg = (err && err.message) ? err.message : 'unknown error';
                // Surface config-specific hint when AI service token is missing
                if (/API_TOKEN|403|401/.test(msg)) {
                    msg = 'AI service auth not configured. Open Full Configuration → AI Service → set API Token.';
                }
                self.showPlayerNotification('Failed: ' + msg, 'error');
            });
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
                // Live-update switch visual without closing the menu
                var sw = document.querySelector('#aiUpscalerQuickMenu .ai-menu__switch');
                if (sw) sw.classList.toggle('ai-menu__switch--on', newState);
                PlayerIntegration.showPlayerNotification(
                    'Upscaling ' + (newState ? 'enabled' : 'disabled'),
                    newState ? 'success' : 'warning'
                );
            }).catch(function(err) {
                console.error('AI Upscaler: config fetch failed', err);
                PlayerIntegration.showPlayerNotification('Failed to toggle upscaling', 'error');
            });
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

        // Extract Jellyfin itemId from the currently-playing video URL hash.
        // Hash format: "#/video?id=<guid>&..."  Returns null if not on a video page.
        _getPlayingItemId: function() {
            try {
                var hash = window.location.hash || '';
                var qIdx = hash.indexOf('?');
                if (qIdx < 0) return null;
                var qs = hash.substring(qIdx + 1);
                var params = qs.split('&');
                for (var i = 0; i < params.length; i++) {
                    var kv = params[i].split('=');
                    if (kv[0] === 'id' && kv[1]) return decodeURIComponent(kv[1]);
                }
            } catch (e) {}
            return null;
        },

        // Auto-Mode: ask the plugin to pick the best model + filter for this video.
        // Returns a Promise<{model, filter}> or resolves with null on any failure.
        // Only kicks in when config.EnableAutoModelSelection === true (default false —
        // the user must opt in under Settings). No placeholder: the backend runs real
        // heuristics over genres/resolution/multi-frame-capability.
        _autoSelectForVideo: function(video, config) {
            if (!config || !config.EnableAutoModelSelection) return Promise.resolve(null);
            if (!video || !video.videoWidth || !video.videoHeight) return Promise.resolve(null);

            var w = video.videoWidth | 0;
            var h = video.videoHeight | 0;
            var itemId = this._getPlayingItemId();

            var fetchItem = itemId && window.ApiClient
                ? window.ApiClient.getItem(ApiClient.getCurrentUserId(), itemId).catch(function() { return null; })
                : Promise.resolve(null);

            return fetchItem.then(function(item) {
                var genres = (item && Array.isArray(item.Genres)) ? item.Genres.join(',') : '';
                var url = ApiClient.getUrl('Upscaler/recommend-model') +
                    '?width=' + w + '&height=' + h +
                    '&isBatch=false' +
                    (genres ? ('&genres=' + encodeURIComponent(genres)) : '');
                return ApiClient.ajax({ type: 'GET', url: url, dataType: 'json' })
                    .then(function(res) {
                        if (!res || !res.success) return null;
                        console.log('AI Upscaler Auto: picked model=' + res.recommended_model +
                            ' filter=' + res.recommended_filter +
                            ' (' + w + 'x' + h + ', genres=' + (genres || 'none') + ')');
                        return {
                            model: res.recommended_model,
                            filter: res.recommended_filter
                        };
                    });
            }).catch(function(err) {
                console.warn('AI Upscaler Auto: recommend-model failed —', err && (err.message || err));
                return null;
            });
        },

        // POST the auto-picked filter preset so FFmpeg applies it on next seek.
        // Fire-and-forget — failure to apply the filter shouldn't block RT upscaling.
        _applyAutoFilter: function(presetKey) {
            if (!presetKey || presetKey === 'none') return;
            var body = { ActiveFilterPreset: presetKey, EnableVideoFilters: true };
            ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('Upscaler/filter-config'),
                data: JSON.stringify(body),
                contentType: 'application/json',
                dataType: 'json'
            }).catch(function(err) {
                console.warn('AI Upscaler Auto: filter-config apply failed —', err && (err.message || err));
            });
        },

        startRealtimeUpscaling: function() {
            this.getPluginConfig().then(function(config) {
                if (config.EnableRealtimeUpscaling === false) return;

                var video = PlayerIntegration.findVideoElement();
                if (!video) {
                    console.log('AI Upscaler RT: No video element found');
                    return;
                }

                // Auto-Mode hook: if the user opted in, let the plugin pick model+filter
                // for *this* video based on genres + resolution. Overrides config.Model
                // for this session only — does not persist back to config.
                return PlayerIntegration._autoSelectForVideo(video, config).then(function(pick) {
                    if (pick && pick.model) {
                        config = Object.assign({}, config, { Model: pick.model });
                        PlayerIntegration._applyAutoFilter(pick.filter);
                        PlayerIntegration.showPlayerNotification(
                            'Auto: ' + pick.model + (pick.filter && pick.filter !== 'none' ? ' + ' + pick.filter : ''),
                            'info');
                    }
                    PlayerIntegration._startRtWithConfig(video, config);
                });
            }).catch(function(err) {
                console.error('AI Upscaler: config fetch failed for RT upscaling', err);
            });
        },

        // Extracted from startRealtimeUpscaling so auto-select can supply an overridden config.
        _startRtWithConfig: function(video, config) {
                var mode = (config.RealtimeMode || 'auto').toLowerCase();

                // WebGL-only mode skips any AI-service interaction.
                if (mode === 'webgl') {
                    RealtimeUpscaler.start(video, config, null);
                    return;
                }

                // Auto + Server modes require an active server-side model before the
                // benchmark (otherwise /benchmark-frame returns 400 "No model loaded",
                // the auto-tier picks WebGL, and server-mode never engages).
                var captureW = config.RealtimeCaptureWidth || 480;
                var captureH = Math.round(captureW * (video.videoHeight / video.videoWidth));
                var modelName = config.Model || 'fsrcnn-x2';
                var authHeaders = { 'Authorization': 'MediaBrowser Token="' + ApiClient.accessToken() + '"' };

                var runBenchmarkAndStart = function() {
                    fetch(ApiClient.getUrl('Upscaler/benchmark-frame') + '?width=' + captureW + '&height=' + captureH, {
                        headers: authHeaders
                    })
                        .then(function(r) { return r.ok ? r.json() : Promise.reject(new Error('HTTP ' + r.status)); })
                        .then(function(bench) {
                            console.log('AI Upscaler RT: Benchmark result', bench);
                            RealtimeUpscaler.start(video, config, bench);
                        })
                        .catch(function(err) {
                            console.warn('AI Upscaler RT: Benchmark failed, using WebGL', err);
                            RealtimeUpscaler.start(video, config, { error: 'benchmark failed' });
                        });
                };

                fetch(ApiClient.getUrl('Upscaler/models/load') + '?model_name=' + encodeURIComponent(modelName), {
                    method: 'POST',
                    headers: authHeaders
                })
                    .then(function(r) {
                        if (!r.ok) {
                            console.warn('AI Upscaler RT: Model preload failed (HTTP ' + r.status + '), running benchmark anyway');
                        }
                        runBenchmarkAndStart();
                    })
                    .catch(function(err) {
                        console.warn('AI Upscaler RT: Model preload network error, running benchmark anyway', err);
                        runBenchmarkAndStart();
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
            // v1.6.1.15 — Redesign: Non-AI style, matches Docker AI Service dashboard
            // Colors: --bg #0b0d12 --surface #11141b --surface-2 #161a23 --border #1f2430
            //         --text #e6e8ec --text-dim #9199a6 --text-muted #5c6472 --accent #3b82f6
            styles.textContent = [
                /* Player toolbar button */
                '#aiUpscalerButton{display:inline-flex!important;align-items:center;justify-content:center;color:#e6e8ec;cursor:pointer;transition:color .15s}',
                '#aiUpscalerButton:hover{color:#3b82f6}',
                '#aiUpscalerButton .material-icons{font-size:24px}',

                /* Menu shell */
                '.ai-menu{position:fixed;bottom:90px;z-index:100000;width:380px;max-height:calc(100vh - 140px);background:#0b0d12;border:1px solid #1f2430;border-radius:6px;box-shadow:0 10px 30px rgba(0,0,0,.55);overflow:hidden;animation:aiMenuIn .16s ease-out;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Inter,sans-serif;display:flex;flex-direction:column;color:#e6e8ec}',
                '.ai-menu--right{right:20px}.ai-menu--left{left:20px}.ai-menu--center{left:50%;transform:translateX(-50%)}',
                '@keyframes aiMenuIn{from{opacity:0;transform:translateY(6px)}to{opacity:1;transform:translateY(0)}}',
                '.ai-menu--center{animation:aiMenuInCenter .16s ease-out}',
                '@keyframes aiMenuInCenter{from{opacity:0;transform:translateX(-50%) translateY(6px)}to{opacity:1;transform:translateX(-50%) translateY(0)}}',

                /* Header */
                '.ai-menu__header{display:flex;align-items:center;justify-content:space-between;padding:12px 14px;background:#11141b;border-bottom:1px solid #1f2430;flex-shrink:0}',
                '.ai-menu__header-left{display:flex;align-items:center;gap:10px}',
                '.ai-menu__logo{font-size:16px;color:#3b82f6;width:22px;height:22px;background:rgba(59,130,246,.10);border:1px solid rgba(59,130,246,.35);border-radius:4px;display:grid;place-items:center}',
                '.ai-menu__title{font-size:13px;font-weight:600;color:#e6e8ec;letter-spacing:.2px;line-height:1.15}',
                '.ai-menu__version{font-size:10px;color:#5c6472;font-weight:500;margin-top:1px;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}',
                '.ai-menu__header-right{display:flex;align-items:center;gap:8px}',

                /* Switch */
                '.ai-menu__switch{position:relative;width:36px;height:20px;border-radius:3px;border:1px solid #2a3040;background:#161a23;cursor:pointer;transition:border-color .15s,background .15s;padding:0;flex-shrink:0}',
                '.ai-menu__switch::after{content:"";position:absolute;top:2px;left:2px;width:14px;height:14px;border-radius:2px;background:#5c6472;transition:left .15s,background .15s}',
                '.ai-menu__switch--on{border-color:#3b82f6;background:rgba(59,130,246,.14)}',
                '.ai-menu__switch--on::after{background:#3b82f6;left:18px}',

                '.ai-menu__close{background:transparent;border:1px solid transparent;color:#5c6472;font-size:18px;cursor:pointer;padding:0;width:24px;height:24px;display:flex;align-items:center;justify-content:center;border-radius:3px;transition:color .15s,border-color .15s;line-height:1}',
                '.ai-menu__close:hover{color:#e6e8ec;border-color:#2a3040}',

                /* Live status strip (upscaling state + FPS + model) */
                '.ai-menu__status{display:flex;align-items:center;gap:6px;padding:8px 14px;background:#0b0d12;border-bottom:1px solid #1f2430;font-size:11px;color:#9199a6;flex-shrink:0;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
                '.ai-menu__status-dot{width:7px;height:7px;border-radius:50%;background:#5c6472;flex-shrink:0;transition:background .2s}',
                '.ai-menu__status-dot--on{background:#34d399;box-shadow:0 0 8px rgba(52,211,153,.6)}',
                '.ai-menu__status-dot--off{background:#5c6472}',
                '.ai-menu__status-dot--warn{background:#fbbf24}',
                '.ai-menu__status-dot--err{background:#ef4444}',
                '.ai-menu__status-state{color:#e6e8ec;font-weight:600;letter-spacing:.5px;font-size:10px}',
                '.ai-menu__status-sep{color:#2a3040}',
                '.ai-menu__status-mode{color:#93c5fd;font-weight:500}',
                '.ai-menu__status-fps{color:#e6e8ec;font-weight:500}',
                '.ai-menu__status-model{color:#9199a6;overflow:hidden;text-overflow:ellipsis;min-width:0;flex:1}',

                /* Summary strip (models-ready counter) */
                '.ai-menu__summary{display:flex;align-items:center;gap:8px;padding:8px 14px;background:#0b0d12;border-bottom:1px solid #1f2430;font-size:11px;color:#9199a6;flex-shrink:0;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}',
                '.ai-menu__summary-dot{width:6px;height:6px;border-radius:50%;background:#34d399;flex-shrink:0}',
                '.ai-menu__summary-dot--off{background:#5c6472}',
                '.ai-menu__summary-strong{color:#e6e8ec;font-weight:600}',

                /* Body */
                '.ai-menu__body{padding:10px 12px;overflow-y:auto;flex:1;min-height:0}',
                '.ai-menu__body::-webkit-scrollbar{width:6px}',
                '.ai-menu__body::-webkit-scrollbar-thumb{background:#2a3040;border-radius:3px}',
                '.ai-menu__body::-webkit-scrollbar-thumb:hover{background:#3b4558}',
                '.ai-menu__body::-webkit-scrollbar-track{background:transparent}',

                /* Filter chips */
                '.ai-menu__chips{display:flex;gap:6px;padding:2px 2px 10px;flex-wrap:wrap}',
                '.ai-menu__chip{padding:4px 10px;background:#161a23;border:1px solid #1f2430;border-radius:3px;color:#9199a6;font-size:11px;font-weight:500;cursor:pointer;transition:border-color .15s,color .15s;letter-spacing:.2px}',
                '.ai-menu__chip:hover{border-color:#2a3040;color:#e6e8ec}',
                '.ai-menu__chip--active{background:rgba(59,130,246,.10);border-color:#3b82f6;color:#93c5fd}',

                /* Section */
                '.ai-menu__section{margin-bottom:14px}',
                '.ai-menu__section:last-child{margin-bottom:0}',
                '.ai-menu__section-title{font-size:10px;font-weight:600;text-transform:uppercase;letter-spacing:.08em;color:#5c6472;padding:0 2px 8px;display:flex;align-items:center;justify-content:space-between}',
                '.ai-menu__section-sub{font-size:10px;font-weight:500;color:#475569;text-transform:none;letter-spacing:.2px}',

                /* Category group */
                '.ai-menu__cat{margin-bottom:10px}',
                '.ai-menu__cat:last-child{margin-bottom:0}',
                '.ai-menu__cat-head{display:flex;align-items:baseline;gap:8px;padding:2px 4px 5px;border-bottom:1px solid #1f2430;margin-bottom:4px}',
                '.ai-menu__cat-name{font-size:11px;font-weight:600;color:#cbd5e1;letter-spacing:.2px}',
                '.ai-menu__cat-desc{font-size:10px;color:#5c6472;font-style:normal}',

                /* Model button */
                '.ai-menu__model{display:flex;align-items:center;gap:8px;width:100%;padding:7px 10px;background:#11141b;border:1px solid #1f2430;border-radius:3px;color:#cbd5e1;font-size:12px;cursor:pointer;transition:border-color .12s,background .12s;margin:3px 0;text-align:left;position:relative}',
                '.ai-menu__model:hover{background:#161a23;border-color:#2a3040;color:#e6e8ec}',
                '.ai-menu__model--active{background:rgba(59,130,246,.08)!important;border-color:#3b82f6!important;color:#e6e8ec!important}',
                '.ai-menu__model--loading{opacity:.6;pointer-events:none}',

                '.ai-menu__model-name{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:500}',
                '.ai-menu__model-scale{font-size:10px;color:#9199a6;font-weight:600;padding:1px 6px;background:#0b0d12;border:1px solid #1f2430;border-radius:3px;flex-shrink:0;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}',
                '.ai-menu__badge{font-size:9px;padding:2px 6px;border-radius:3px;background:rgba(59,130,246,.12);border:1px solid rgba(59,130,246,.35);color:#93c5fd;font-weight:600;text-transform:uppercase;letter-spacing:.5px;flex-shrink:0}',

                /* State icons */
                '.ai-menu__state{width:16px;height:16px;display:flex;align-items:center;justify-content:center;flex-shrink:0;font-size:12px}',
                '.ai-menu__state--ready{color:#34d399}',
                '.ai-menu__state--need-dl{color:#5c6472}',
                '.ai-menu__state--busy{color:#3b82f6}',
                '.ai-menu__state--err{color:#f87171}',
                '.ai-menu__spinner{width:12px;height:12px;border:2px solid #1f2430;border-top-color:#3b82f6;border-radius:50%;animation:aiSpin .7s linear infinite}',
                '@keyframes aiSpin{to{transform:rotate(360deg)}}',

                /* Scale picker */
                '.ai-menu__scales{display:flex;gap:6px}',
                '.ai-menu__scale{flex:1;padding:8px;background:#11141b;border:1px solid #1f2430;border-radius:3px;color:#cbd5e1;font-size:13px;font-weight:600;cursor:pointer;transition:border-color .15s,background .15s;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}',
                '.ai-menu__scale:hover{background:#161a23;border-color:#2a3040;color:#e6e8ec}',
                '.ai-menu__scale--active{background:rgba(59,130,246,.10)!important;border-color:#3b82f6!important;color:#93c5fd!important}',

                /* Real-Time card */
                '.ai-menu__rt-card{padding:10px 12px;background:#11141b;border:1px solid #1f2430;border-radius:3px}',
                '.ai-menu__rt-status{display:flex;align-items:center;gap:8px;font-size:12px;color:#9199a6;margin-bottom:9px;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}',
                '.ai-menu__rt-label{color:#5c6472;font-weight:500}',
                '.ai-menu__rt-value{font-weight:600;letter-spacing:.2px;color:#e6e8ec}',
                '.ai-menu__rt-indicator{width:7px;height:7px;border-radius:50%;background:#475569;flex-shrink:0}',
                '.ai-menu__rt-indicator--on{background:#34d399;animation:aiPulse 1.8s ease-in-out infinite}',
                '@keyframes aiPulse{0%,100%{opacity:1}50%{opacity:.55}}',
                '.ai-menu__rt-row{display:flex;gap:6px}',
                '.ai-menu__rt-btn{flex:1;padding:7px;background:#161a23;border:1px solid #2a3040;border-radius:3px;color:#cbd5e1;font-size:11px;font-weight:600;cursor:pointer;transition:border-color .15s,color .15s;letter-spacing:.2px}',
                '.ai-menu__rt-btn:hover{border-color:#3b82f6;color:#e6e8ec}',

                /* Action link */
                '.ai-menu__action{display:flex;align-items:center;justify-content:center;width:100%;padding:10px;background:#11141b;border:1px solid #1f2430;border-radius:3px;color:#cbd5e1;font-size:12px;font-weight:600;cursor:pointer;transition:border-color .15s,color .15s;letter-spacing:.2px}',
                '.ai-menu__action:hover{border-color:#3b82f6;color:#e6e8ec}',

                /* Skeleton loading */
                '.ai-menu__skeleton{height:34px;margin:4px 0;background:linear-gradient(90deg,#11141b 0%,#161a23 50%,#11141b 100%);background-size:200% 100%;border-radius:3px;animation:aiShimmer 1.2s ease-in-out infinite}',
                '@keyframes aiShimmer{0%{background-position:200% 0}100%{background-position:-200% 0}}',

                /* Notification toast */
                '.ai-notif{position:fixed;top:20px;right:20px;padding:10px 14px;border-radius:3px;color:#e6e8ec;font-size:12px;font-weight:500;z-index:100001;animation:aiNotifIn .22s ease-out;pointer-events:none;box-shadow:0 8px 24px rgba(0,0,0,.4);max-width:340px;background:#0b0d12;border:1px solid #2a3040}',
                '.ai-notif--info{border-color:#3b82f6;color:#93c5fd}',
                '.ai-notif--success{border-color:#34d399;color:#6ee7b7}',
                '.ai-notif--warning{border-color:#fbbf24;color:#fcd34d}',
                '.ai-notif--error{border-color:#f87171;color:#fca5a5}',
                '@keyframes aiNotifIn{from{transform:translateX(16px);opacity:0}to{transform:translateX(0);opacity:1}}',

                /* Tab bar — underline style matching Docker dashboard */
                '.ai-menu__tabs{display:flex;gap:0;padding:0 12px;background:#11141b;border-bottom:1px solid #1f2430}',
                '.ai-menu__tab{flex:1;display:flex;align-items:center;justify-content:center;gap:6px;padding:10px 6px;background:transparent;border:none;border-bottom:2px solid transparent;color:#9199a6;font-size:12px;font-weight:500;cursor:pointer;transition:color .15s,border-color .15s;letter-spacing:.2px}',
                '.ai-menu__tab .material-icons{font-size:15px}',
                '.ai-menu__tab:hover{color:#e6e8ec}',
                '.ai-menu__tab--active{color:#e6e8ec;border-bottom-color:#3b82f6}',
                '.ai-menu__tab-live{width:6px;height:6px;border-radius:50%;background:transparent;transition:background .2s}',
                '.ai-menu__tab-live--on{background:#34d399;animation:aiPulse 1.8s ease-in-out infinite}',
                '.ai-menu__pane{display:none}',
                '.ai-menu__pane--active{display:block}',
                '.ai-menu__footer{padding-top:10px;margin-top:6px;border-top:1px solid #1f2430}',

                /* Preset chips grid */
                '.ai-menu__presets{display:grid;grid-template-columns:repeat(4,1fr);gap:5px}',
                '.ai-menu__preset{padding:8px 4px;background:#11141b;border:1px solid #1f2430;border-radius:3px;color:#cbd5e1;font-size:11px;font-weight:600;cursor:pointer;transition:border-color .12s,color .12s;text-align:center;letter-spacing:.1px}',
                '.ai-menu__preset:hover{border-color:#2a3040;color:#e6e8ec}',
                '.ai-menu__preset--active{background:rgba(59,130,246,.10);border-color:#3b82f6;color:#93c5fd}',

                /* Live sliders */
                '.ai-menu__sliders{display:flex;flex-direction:column;gap:9px}',
                '.ai-menu__slider-row{display:flex;flex-direction:column;gap:5px}',
                '.ai-menu__slider-label{display:flex;align-items:center;gap:6px;font-size:11px;font-weight:600;color:#cbd5e1;letter-spacing:.2px}',
                '.ai-menu__slider-icon{font-size:14px;color:#3b82f6}',
                '.ai-menu__slider-val{margin-left:auto;color:#93c5fd;font-variant-numeric:tabular-nums;min-width:32px;text-align:right;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}',
                '.ai-menu__slider{width:100%;-webkit-appearance:none;appearance:none;height:4px;border-radius:2px;background:#1f2430;outline:none;cursor:pointer}',
                '.ai-menu__slider::-webkit-slider-thumb{-webkit-appearance:none;appearance:none;width:12px;height:12px;border-radius:2px;background:#3b82f6;cursor:pointer;border:1px solid #0b0d12}',
                '.ai-menu__slider::-webkit-slider-thumb:hover{background:#60a5fa}',
                '.ai-menu__slider::-moz-range-thumb{width:12px;height:12px;border-radius:2px;background:#3b82f6;cursor:pointer;border:1px solid #0b0d12}',

                /* Advanced collapsible */
                '.ai-menu__adv-toggle{display:flex;align-items:center;justify-content:space-between;width:100%;padding:8px 10px;background:#11141b;border:1px solid #1f2430;border-radius:3px;color:#9199a6;font-size:11px;font-weight:600;cursor:pointer;transition:color .15s,border-color .15s;letter-spacing:.2px}',
                '.ai-menu__adv-toggle:hover{border-color:#2a3040;color:#e6e8ec}',
                '.ai-menu__adv-caret{font-size:16px;color:#5c6472}',
                '.ai-menu__adv{max-height:0;overflow:hidden;transition:max-height .28s ease;display:flex;flex-direction:column;gap:7px}',
                '.ai-menu__adv--open{max-height:480px;padding-top:8px}',
                '.ai-menu__adv-row{display:flex;flex-direction:column;gap:3px;padding:5px 8px;background:#0b0d12;border:1px solid #1f2430;border-radius:3px}',
                '.ai-menu__adv-label{display:flex;justify-content:space-between;font-size:10px;font-weight:600;color:#5c6472;text-transform:uppercase;letter-spacing:.4px}',
                '.ai-menu__adv-val{color:#93c5fd;font-variant-numeric:tabular-nums;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace}',
                '.ai-menu__adv-slider{width:100%;-webkit-appearance:none;appearance:none;height:3px;border-radius:2px;background:#1f2430;outline:none;cursor:pointer}',
                '.ai-menu__adv-slider::-webkit-slider-thumb{-webkit-appearance:none;appearance:none;width:10px;height:10px;border-radius:2px;background:#3b82f6;cursor:pointer;border:1px solid #0b0d12}',
                '.ai-menu__adv-slider::-moz-range-thumb{width:10px;height:10px;border-radius:2px;background:#3b82f6;cursor:pointer;border:1px solid #0b0d12}',

                /* Filter action buttons */
                '.ai-menu__filter-actions{display:flex;gap:7px;padding-top:6px}',
                '.ai-menu__filter-btn{flex:1;padding:9px;border-radius:3px;font-size:12px;font-weight:600;cursor:pointer;transition:border-color .15s,background .15s;letter-spacing:.2px}',
                '.ai-menu__filter-btn:disabled{opacity:.4;cursor:not-allowed}',
                '.ai-menu__filter-btn--secondary{background:#11141b;border:1px solid #1f2430;color:#cbd5e1}',
                '.ai-menu__filter-btn--secondary:hover:not(:disabled){border-color:#2a3040;color:#e6e8ec}',
                '.ai-menu__filter-btn--primary{background:rgba(59,130,246,.12);border:1px solid #3b82f6;color:#93c5fd}',
                '.ai-menu__filter-btn--primary:hover:not(:disabled){background:rgba(59,130,246,.2);color:#e6e8ec}'
            ].join('');

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
