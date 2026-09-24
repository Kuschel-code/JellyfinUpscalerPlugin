// AI Upscaler Plugin - Player Integration v1.7.13
// Global script injection (loaded via index.html like Intro Skipper)
// Compatible with Jellyfin 10.11+

(function() {
    'use strict';

    // Plugin configuration
    const PLUGIN_ID = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';
    const PLUGIN_VERSION = '1.8.3.32';

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
    // v1.7.0 - Modes: 'lanczos' (was 'webgl', honest rebrand: it's Lanczos+Sharpen, NOT AI),
    //                  'anime4k' (NEW: real AI shader for anime via Anime4K.js library, MIT-licensed),
    //                  'server' (Docker AI), 'auto' (smart-pick).
    // 'webgl' is kept as alias for 'lanczos' for backwards-compat with v1.6.x configs.
    const RealtimeUpscaler = {
        _generation: 0,
        _retryCount: 0,
        _nextFrameAt: 0,
        _requestController: null,
        _startupController: null,
        _reason: null,
        _mode: null,       // 'lanczos' | 'anime4k' | 'server' | null
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
        // v1.8.3.24 - read once when the loop starts, not per frame: switching the target
        // endpoint mid-flight would mix masked and unmasked frames on the same overlay.
        _objectMaskEnabled: false,
        _lastDetectionCount: 0,
        _benchmarkResult: null,
        _webglInstance: null,
        _anime4kInstance: null,
        _anime4kCanvas: null,

        start: function(video, config, benchmarkResult) {
            this.stop();
            this._videoElement = video;
            this._config = config;
            this._benchmarkResult = benchmarkResult;
            this._objectMaskEnabled = config.EnableObjectMasking === true;
            this._lastDetectionCount = 0;

            var mode = (config.RealtimeMode || 'auto').toLowerCase();
            // v1.7.0 - 'webgl' rebrand: it was always Lanczos+Sharpen (no AI), so call it that.
            if (mode === 'webgl') mode = 'lanczos';
            this._reason = null;
            if (this._objectMaskEnabled) {
                // Masking replaces upscaling but still uses the shared server capture loop.
                mode = 'server';
                this._reason = 'Object masking replaces plugin upscaling';
            } else if (config.ClientDriverUpscalingActive === true) {
                mode = 'off';
                this._reason = 'Client driver upscaling is active';
            } else if (mode === 'auto') {
                mode = this._decideTier(benchmarkResult, video);
            }
            if (mode === 'off') {
                this._mode = 'off';
                if (window.PlayerIntegration) window.PlayerIntegration.showPlayerNotification(this._reason, 'info');
                return;
            }

            this._mode = mode;
            this._active = true;
            this._lowFpsStart = 0;
            console.log('AI Upscaler RT: Starting in ' + mode + ' mode');

            if (mode === 'server') {
                this._startServer();
            } else if (mode === 'anime4k') {
                this._startAnime4K();
            } else if (mode === 'ai-webgpu') {
                this._startWebGPUAI();
            } else {
                // Default: Lanczos+Sharpen WebGL shader (existing fast non-AI path).
                this._startWebGL();
            }

            this._createFpsOverlay();
            this._updateButtonIndicator(mode);
        },

        stop: function() {
            if (this._startupController) this._startupController.abort();
            this._startupController = null;
            this._active = false;
            this._mode = null;
            this._stopServer();
            this._stopWebGL();
            this._stopAnime4K();
            this._stopWebGPUAI();
            this._removeFpsOverlay();
            this._updateButtonIndicator(null);
            console.log('AI Upscaler RT: Stopped');
        },

        _decideTier: function(benchmark, video) {
            if (this._config && this._config.ClientDriverUpscalingActive === true) return 'off';
            if (this._config && this._config.EnableObjectMasking === true) return 'off';
            if (!benchmark || benchmark.error) return 'webgl';
            // Use the playing item's frame rate. Without one, Auto cannot prove
            // the server keeps up; prefer the client shader until it is known.
            var videoFps = Number(benchmark.videoFps);
            if (!Number.isFinite(videoFps) || videoFps <= 0) return 'webgl';
            videoFps *= video.playbackRate || 1;
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
            if (!window.AIUpscalerWebGL || !this._videoElement || !this._active ||
                (this._mode !== 'lanczos' && this._mode !== 'webgl')) return;
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

        // --- Anime4K Tier (v1.7.9: embedded, vendored bundle) ---
        // Real Anime4K (4.0.1) WebGL anime upscaling via a VENDORED, tree-shaken
        // Anime4K.js v1.1.2 bundle (MIT, https://github.com/monyone/Anime4K.js)
        // embedded in the plugin DLL and served from UPSCALERAnime4K -- NO CDN, fully
        // self-contained/offline. Exposes window.Anime4KJS. Honest label: anime SHADER,
        // not a neural net. Auto-falls back to Lanczos if WebGL2 float textures are absent.
        _startAnime4K: function() {
            var self = this;
            var generation = this._generation;
            this._loadAnime4KLibrary(function(ok) {
                if (!self._active || self._generation !== generation || self._mode !== 'anime4k') return;
                if (!ok) {
                    console.warn('AI Upscaler RT: Anime4K bundle load failed, falling back to Lanczos');
                    self._fallbackToLanczos();
                    return;
                }
                self._initAnime4K();
            });
        },

        _loadAnime4KLibrary: function(callback) {
            // Load the embedded bundle (exposes window.Anime4KJS). No external network.
            function resolved() { return window.Anime4KJS; }
            if (resolved()) { callback(true); return; }
            if (document.querySelector('script[data-upscaler-anime4k]')) {
                var attempts = 0;
                var poll = setInterval(function() {
                    attempts++;
                    if (resolved()) { clearInterval(poll); callback(true); }
                    else if (attempts >= 50) { clearInterval(poll); callback(false); }
                }, 100);
                return;
            }
            var script = document.createElement('script');
            script.src = '/web/configurationpage?name=UPSCALERAnime4K';
            script.setAttribute('data-upscaler-anime4k', '1');
            script.onload = function() { setTimeout(function() { callback(!!resolved()); }, 50); };
            script.onerror = function() {
                console.warn('AI Upscaler RT: Failed to load embedded Anime4K bundle', script.src);
                callback(false);
            };
            document.head.appendChild(script);
        },

        _initAnime4K: function() {
            if (!this._videoElement || !this._active || this._mode !== 'anime4k') return;
            var ns = window.Anime4KJS;
            if (!ns || typeof ns.VideoUpscaler !== 'function') {
                console.warn('AI Upscaler RT: Anime4KJS.VideoUpscaler missing');
                this._fallbackToLanczos();
                return;
            }
            var VideoUpscaler = ns.VideoUpscaler;
            try {
                if (typeof VideoUpscaler.isSupported === 'function' && !VideoUpscaler.isSupported()) {
                    console.warn('AI Upscaler RT: Anime4K unsupported here (no WebGL float textures), using Lanczos');
                    this._fallbackToLanczos();
                    return;
                }
            } catch (e) { this._fallbackToLanczos(); return; }
            var profile = ns.ANIME4KJS_SIMPLE_M_2X || ns.ANIME4KJS_SIMPLE_S_2X;
            if (!profile) {
                console.warn('AI Upscaler RT: Anime4K profile missing');
                this._fallbackToLanczos();
                return;
            }
            try {
                this._anime4kCanvas = document.createElement('canvas');
                this._anime4kCanvas.style.cssText = 'position:absolute;top:0;left:0;width:100%;height:100%;object-fit:contain;pointer-events:none;z-index:999;';
                var parent = this._videoElement.parentElement;
                if (parent) { parent.style.position = 'relative'; parent.appendChild(this._anime4kCanvas); }
                this._anime4kInstance = new VideoUpscaler(profile, 30);
                this._anime4kInstance.attachVideo(this._videoElement, this._anime4kCanvas);
                this._anime4kInstance.start();
                console.log('AI Upscaler RT: Anime4K (embedded, SIMPLE_M 2x) started');
            } catch (e) {
                console.warn('AI Upscaler RT: Anime4K init threw', e);
                this._fallbackToLanczos();
            }
        },

        _fallbackToLanczos: function() {
            this._stopAnime4K();
            if (!this._active || this._objectMaskEnabled ||
                (this._config && this._config.ClientDriverUpscalingActive)) return;
            this._mode = 'lanczos';
            this._updateButtonIndicator('lanczos');
            this._startWebGL();
        },

        _stopAnime4K: function() {
            if (this._anime4kInstance) {
                try {
                    if (typeof this._anime4kInstance.stop === 'function') this._anime4kInstance.stop();
                    if (typeof this._anime4kInstance.detachVideo === 'function') this._anime4kInstance.detachVideo();
                } catch (e) { /* best effort */ }
                this._anime4kInstance = null;
            }
            if (this._anime4kCanvas && this._anime4kCanvas.parentElement) {
                this._anime4kCanvas.parentElement.removeChild(this._anime4kCanvas);
            }
            this._anime4kCanvas = null;
        },

        // --- WebGPU AI Tier (v1.7.1) ---
        // Real-ESRGAN compact via onnxruntime-web @WebGPU. Loaded lazily via embedded
        // resource (UPSCALERWebGPUAI page) which then loads onnxruntime-web from jsdelivr.
        // Three-stage fallback: WebGPU missing -> ORT load fail -> model fetch fail = Lanczos.
        _startWebGPUAI: function() {
            var self = this;
            var generation = this._generation;
            function current() { return self._active && self._generation === generation && self._mode === 'ai-webgpu'; }
            this._loadWebGPUAIScript(function(loaded) {
                if (!current()) return;
                if (!loaded || !window.WebGPUAIUpscaler) {
                    console.warn('AI Upscaler RT: WebGPU AI script load failed, falling back to Lanczos');
                    self._mode = 'lanczos';
                    self._updateButtonIndicator('lanczos');
                    self._startWebGL();
                    return;
                }
                window.WebGPUAIUpscaler.start(self._videoElement, {
                    fpsCallback: function(fps) {
                        RealtimeUpscaler._currentFps = fps;
                        RealtimeUpscaler._updateFpsDisplay();
                    },
                    onFatal: function(reason) {
                        if (!current()) return;
                        console.warn('AI Upscaler RT: WebGPU AI fatal:', reason, '- falling back to Lanczos');
                        self._mode = 'lanczos';
                        self._updateButtonIndicator('lanczos');
                        self._startWebGL();
                    }
                }).then(function(ok) {
                    if (!current()) return;
                    if (!ok) {
                        // Returned false (e.g. no WebGPU adapter) - fall back to Lanczos.
                        self._mode = 'lanczos';
                        self._updateButtonIndicator('lanczos');
                        self._startWebGL();
                    }
                });
            });
        },

        _loadWebGPUAIScript: function(callback) {
            if (window.WebGPUAIUpscaler) { callback(true); return; }
            if (document.querySelector('script[data-upscaler-webgpu-ai]')) {
                // Already loading - poll briefly.
                var attempts = 0;
                var poll = setInterval(function() {
                    attempts++;
                    if (window.WebGPUAIUpscaler) { clearInterval(poll); callback(true); }
                    else if (attempts >= 50) { clearInterval(poll); callback(false); }
                }, 100);
                return;
            }
            var script = document.createElement('script');
            script.src = '/web/configurationpage?name=UPSCALERWebGPUAI';
            script.setAttribute('data-upscaler-webgpu-ai', '1');
            script.onload = function() { setTimeout(function() { callback(!!window.WebGPUAIUpscaler); }, 50); };
            script.onerror = function() {
                console.warn('AI Upscaler RT: Failed to load WebGPU AI script from', script.src);
                callback(false);
            };
            document.head.appendChild(script);
        },

        _stopWebGPUAI: function() {
            if (window.WebGPUAIUpscaler && typeof window.WebGPUAIUpscaler.stop === 'function') {
                try { window.WebGPUAIUpscaler.stop(); } catch (e) { /* best effort */ }
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
            this._retryCount = 0;
            this._nextFrameAt = 0;
            this._fpsFrameCount = 0;
            this._fpsLastTime = performance.now();
            this._lastSuccessfulFrame = performance.now();
            // Background tabs may suspend the watchdog throughout a pause. Reset
            // its clock on resume as well, before the first resumed interval runs.
            var self = this;
            this._serverPlaybackListener = function() {
                self._lastSuccessfulFrame = performance.now();
                self._lowFpsStart = 0;
            };
            this._videoElement.addEventListener('pause', this._serverPlaybackListener);
            this._videoElement.addEventListener('playing', this._serverPlaybackListener);
            this._serverRenderLoop();
            this._fallbackCheckInterval = setInterval(function() {
                if (!self._active || self._mode !== 'server') return;
                var now = performance.now();
                // Pauses and a service-requested wait are not processing timeouts.
                if (!self._videoElement || self._videoElement.paused || now < self._nextFrameAt) {
                    self._lastSuccessfulFrame = now;
                    self._lowFpsStart = 0;
                    return;
                }
                if (!self._objectMaskEnabled && now - self._lastSuccessfulFrame > 10000) {
                    self._stopServer();
                    self._fallbackToLanczos();
                    if (window.PlayerIntegration) window.PlayerIntegration.showPlayerNotification('Switched to Lanczos (server unresponsive)', 'warning');
                }
            }, 1000);
        },

        _stopServer: function() {
            this._generation++;
            if (this._videoElement && this._serverPlaybackListener) {
                this._videoElement.removeEventListener('pause', this._serverPlaybackListener);
                this._videoElement.removeEventListener('playing', this._serverPlaybackListener);
            }
            this._serverPlaybackListener = null;
            if (this._requestController) this._requestController.abort();
            this._requestController = null;
            this._pendingFrame = false;
            this._nextFrameAt = 0;
            if (this._serverRafId != null) cancelAnimationFrame(this._serverRafId);
            this._serverRafId = null;
            if (this._fallbackCheckInterval) clearInterval(this._fallbackCheckInterval);
            this._fallbackCheckInterval = null;
            if (this._currentObjectUrl) URL.revokeObjectURL(this._currentObjectUrl);
            this._currentObjectUrl = null;
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
            if (!this._pendingFrame && this._videoElement && !this._videoElement.paused &&
                performance.now() >= this._nextFrameAt) this._captureAndSend();
            this._serverRafId = requestAnimationFrame(function() { RealtimeUpscaler._serverRenderLoop(); });
        },

        _retryAfterMs: function(value) {
            if (!value) return 0;
            if (/^\d+(\.\d+)?$/.test(value.trim())) return Number(value) * 1000;
            var date = Date.parse(value);
            return Number.isFinite(date) ? Math.max(0, date - Date.now()) : 0;
        },

        _waitForFrame: function(retryAfter, detail) {
            var delay = Math.min(2000, 250 * Math.pow(2, Math.min(this._retryCount++, 4)) * (1 + Math.random() * 0.25));
            this._nextFrameAt = performance.now() + Math.max(delay, this._retryAfterMs(retryAfter));
            this._reason = detail;
            if (window.PlayerIntegration && detail) window.PlayerIntegration.showPlayerNotification(detail, 'warning');
        },

        _captureAndSend: function() {
            var self = this;
            var video = this._videoElement;
            var canvas = this._captureCanvas;
            if (this._pendingFrame || !this._active || this._mode !== 'server' || !video || video.paused ||
                !this._captureCtx || !canvas || performance.now() < this._nextFrameAt) return;
            var generation = this._generation;
            var controller = new AbortController();
            this._requestController = controller;
            this._pendingFrame = true;
            function current() { return self._active && self._mode === 'server' && self._generation === generation; }
            function finish() {
                if (current()) { self._pendingFrame = false; self._requestController = null; }
            }
            function failed(error) {
                if (current() && error.name !== 'AbortError') self._waitForFrame(null, error.message || 'Frame request failed');
                finish();
            }
            try {
                // Capture after the backoff, never retry the previously captured frame.
                this._captureCtx.drawImage(video, 0, 0, canvas.width, canvas.height);
                canvas.toBlob(function(blob) {
                    if (!current()) return;
                    if (!blob) { failed(new Error('Could not capture video frame')); return; }
                    var endpoint = self._objectMaskEnabled ? 'Upscaler/detect-mask' : 'Upscaler/upscale-frame';
                    fetch(ApiClient.getUrl(endpoint), {
                        method: 'POST',
                        headers: { 'Authorization': 'MediaBrowser Token="' + ApiClient.accessToken() + '"' },
                        body: blob,
                        signal: controller.signal
                    }).then(async function(resp) {
                        if (!current()) return null;
                        if (!resp.ok) {
                            var detail = await resp.text();
                            try { var data = JSON.parse(detail); detail = data.detail || data.error || detail; } catch (e) { /* plain text */ }
                            if (current()) self._waitForFrame(resp.headers.get('Retry-After'), 'HTTP ' + resp.status + ': ' + String(detail).slice(0, 500));
                            return null;
                        }
                        if (self._objectMaskEnabled) self._lastDetectionCount = parseInt(resp.headers.get('X-Detections'), 10) || 0;
                        return resp.blob();
                    }).then(function(resultBlob) {
                        if (!current()) return;
                        if (!resultBlob) { finish(); return; }
                        var img = new Image();
                        var url = URL.createObjectURL(resultBlob);
                        self._currentObjectUrl = url;
                        function release() {
                            URL.revokeObjectURL(url);
                            if (self._currentObjectUrl === url) self._currentObjectUrl = null;
                        }
                        img.onload = function() {
                            release();
                            if (!current()) return;
                            try {
                                self._overlayCanvas.width = img.width;
                                self._overlayCanvas.height = img.height;
                                self._overlayCtx = self._overlayCanvas.getContext('2d');
                                self._overlayCtx.drawImage(img, 0, 0);
                                self._lastSuccessfulFrame = performance.now();
                                self._retryCount = 0;
                                self._reason = null;
                                self._fpsFrameCount++;
                                var now = performance.now();
                                if (now - self._fpsLastTime >= 1000) {
                                    self._currentFps = Math.round(self._fpsFrameCount * 1000 / (now - self._fpsLastTime));
                                    self._fpsFrameCount = 0;
                                    self._fpsLastTime = now;
                                    self._updateFpsDisplay();
                                }
                                finish();
                            } catch (error) { failed(error); }
                        };
                        img.onerror = function() { release(); failed(new Error('Could not decode processed frame')); };
                        img.src = url;
                    }).catch(failed);
                }, 'image/jpeg', 0.85);
            } catch (error) { failed(error); }
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
                benchmark: this._benchmarkResult,
                reason: this._reason
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
                if (window.RealtimeUpscaler) {
                    window.RealtimeUpscaler.stop();
                }
            }
        },

        _waitForVideoAndAutoStart: function() {
            if (this._autoStartPending) return;
            this._autoStartPending = true;
            var self = this;
            var generation = RealtimeUpscaler._generation;
            var retries = 0;
            var maxRetries = 60; // 30s @ 500ms
            var check = function() {
                if (generation !== RealtimeUpscaler._generation) {
                    self._autoStartPending = false;
                    return;
                }
                var v = self.findVideoElement();
                if (v) {
                    self._autoStartPending = false;
                    var trigger = function() {
                        if (generation !== RealtimeUpscaler._generation) return;
                        if (window.location.hash.indexOf('#/video') !== 0) return;
                        setTimeout(function() {
                            if (generation !== RealtimeUpscaler._generation ||
                                window.location.hash.indexOf('#/video') !== 0) return;
                            self.startRealtimeUpscaling();
                        }, 600);
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
            var fps = null, target = 0, level = '';

            if (st && st.active) {
                dot.className = 'ai-menu__status-dot ai-menu__status-dot--on';
                stateEl.textContent = 'ACTIVE';
                modeEl.textContent = this._modeLabel(st.mode);
                fps = st.fps | 0;
                // Judge the rate against the video's own frame rate when the benchmark
                // knows it (server mode); the fixed 20/10 fps marks only stand in for it.
                target = Number(st.benchmark && st.benchmark.videoFps) || 0;
                level = target ? (fps >= target * 0.8 ? '' : fps >= target * 0.5 ? 'warn' : 'err')
                               : (fps >= 20 ? '' : fps >= 10 ? 'warn' : 'err');
                modelEl.textContent = (st.benchmark && st.benchmark.model) || cfg.Model || '—';
            } else if (playing && cfg.EnableUpscaling === false) {
                dot.className = 'ai-menu__status-dot ai-menu__status-dot--off';
                stateEl.textContent = 'DISABLED';
                modeEl.textContent = '—';
                modelEl.textContent = cfg.Model || '—';
            } else if (playing) {
                dot.className = 'ai-menu__status-dot ai-menu__status-dot--warn';
                stateEl.textContent = 'STANDBY';
                modeEl.textContent = st && st.mode === 'off' ? 'Off' : '—';
                // A guard that switched realtime off says why; show that instead of a model.
                modelEl.textContent = (st && st.reason) || cfg.Model || '—';
            } else {
                dot.className = 'ai-menu__status-dot ai-menu__status-dot--off';
                stateEl.textContent = 'IDLE';
                modeEl.textContent = '—';
                modelEl.textContent = cfg.Model || '—';
            }
            fpsEl.innerHTML = '<b>' + (fps === null ? '—' : fps) + '</b> fps';
            fpsEl.className = 'ai-menu__status-fps' + (level ? ' ai-menu__status-fps--' + level : '');
            this._drawFpsTrend(menu, fps, target, level);
        },

        _modeLabel: function(mode) {
            var labels = { server: 'Server AI', lanczos: 'Lanczos', webgl: 'Lanczos', anime4k: 'Anime4K', 'ai-webgpu': 'WebGPU AI', off: 'Off' };
            return labels[mode] || (mode ? String(mode) : '—');
        },

        // Last 20 s of the frame rate (one sample per 500 ms poll), drawn against a
        // dashed line at the video's own rate - "is it keeping up?" at a glance.
        _drawFpsTrend: function(menu, fps, target, level) {
            var hist = this._fpsHistory || (this._fpsHistory = []);
            if (fps === null) hist.length = 0;
            else { hist.push(fps); if (hist.length > 40) hist.shift(); }
            var svg = menu.querySelector('[data-status-spark]');
            if (!svg) return;
            var line = svg.querySelector('.ai-menu__spark-line');
            var goal = svg.querySelector('.ai-menu__spark-target');
            var W = 120, H = 36, top = Math.max(target * 1.25, 10);
            for (var i = 0; i < hist.length; i++) top = Math.max(top, hist[i] * 1.1);
            var pts = [];
            for (var j = 0; j < hist.length; j++) {
                var x = W * (40 - hist.length + j) / 39;
                pts.push(x.toFixed(1) + ',' + (H - 2 - hist[j] / top * (H - 4)).toFixed(1));
            }
            if (line) line.setAttribute('points', pts.join(' '));
            if (goal) {
                var y = target ? (H - 2 - target / top * (H - 4)).toFixed(1) : '-10';
                goal.setAttribute('y1', y);
                goal.setAttribute('y2', y);
            }
            svg.setAttribute('class', 'ai-menu__spark' + (level ? ' ai-menu__spark--' + level : ''));
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
                    map[m.id] = { downloaded: !!m.downloaded, available: m.available !== false, loaded: !!m.loaded, category: m.category };
                });
                return map;
            }).catch(function(err) {
                console.warn('AI Upscaler: could not fetch model states —', err && (err.message || err.status || err));
                return null;
            });
        },

        _isUpscalerState: function(state) {
            return !state || (state.available !== false &&
                ['interpolation', 'face_restore', 'face-restore', 'object-detection'].indexOf((state.category || '').toLowerCase()) === -1);
        },

        // One glyph set for every state change (render, refresh, load, failure).
        _stateGlyph: function(kind) {
            var icons = { ready: 'check_circle', 'need-dl': 'download', err: 'error_outline', unknown: 'more_horiz' };
            return '<span class="material-icons" aria-hidden="true">' + (icons[kind] || icons['need-dl']) + '</span>';
        },

        _renderModelCard: function(m, isActive, state) {
            if (!this._isUpscalerState(state)) return '';
            var esc = this._escapeHtml;
            var kind, title;
            if (state && !state.available) {
                kind = 'err'; title = 'Not yet available';
            } else if (state && state.downloaded) {
                kind = 'ready'; title = 'Downloaded and ready';
            } else if (state) {
                kind = 'need-dl'; title = 'Downloads on first use';
            } else {
                kind = 'unknown'; title = 'Status unknown';
            }
            // Favorites come from a config string, so ids and names are escaped here.
            var html = '<button class="ai-menu__model' + (isActive ? ' ai-menu__model--active' : '') +
                '" data-model="' + esc(m.id) + '" data-scale="' + esc(m.scale) + '" title="' + title +
                '" aria-pressed="' + (isActive ? 'true' : 'false') + '">';
            html += '<span class="ai-menu__radio" aria-hidden="true"></span>';
            html += '<span class="ai-menu__model-name">' + esc(m.name) + '</span>';
            if (m.badge) html += '<span class="ai-menu__badge">' + esc(m.badge) + '</span>';
            html += '<span class="ai-menu__model-scale">' + esc(m.scale) + '&times;</span>';
            html += '<span class="ai-menu__state ai-menu__state--' + (kind === 'unknown' ? 'need-dl' : kind) +
                '" data-state-slot="' + esc(m.id) + '">' + this._stateGlyph(kind) + '</span>';
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
            var summDot = menu.querySelector('.ai-menu__summary-dot');
            if (summDot) summDot.classList.toggle('ai-menu__summary-dot--off', ready === 0);

            // Update each state icon. A pinned favorite is listed twice (Favorites and its
            // own category), so every slot for the id is updated, not just the first.
            Object.keys(states).forEach(function(id) {
                var slots = menu.querySelectorAll('[data-state-slot="' + id + '"]');
                var s = states[id];
                var kind = !s.available ? 'err' : s.downloaded ? 'ready' : 'need-dl';
                for (var n = 0; n < slots.length; n++) {
                    var slot = slots[n];
                    if (!PlayerIntegration._isUpscalerState(s)) { slot.closest('button').remove(); continue; }
                    slot.classList.remove('ai-menu__state--ready','ai-menu__state--need-dl','ai-menu__state--busy','ai-menu__state--err');
                    slot.classList.add('ai-menu__state--' + kind);
                    slot.innerHTML = PlayerIntegration._stateGlyph(kind);
                }
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
            // v1.8.3.11 - pinned favorites first (the config page's star list)
            var favIds = (config.FavoriteModels || '').split(',').map(function(x) { return x.trim(); }).filter(Boolean);
            if (favIds.length) {
                var flatCat = {};
                Object.keys(MODEL_CATALOG).forEach(function(fck) { MODEL_CATALOG[fck].models.forEach(function(fm) { flatCat[fm.id] = fm; }); });
                modelsHtml += '<div class="ai-menu__cat" data-cat="favorites">';
                modelsHtml += '<div class="ai-menu__cat-head"><span class="ai-menu__cat-name">★ Favorites</span><span class="ai-menu__cat-desc">pinned on the config page</span></div>';
                for (var fvi = 0; fvi < favIds.length; fvi++) {
                    var favId = favIds[fvi];
                    var favM = flatCat[favId] || { id: favId, name: favId.replace(/^omdb-/, ''), scale: 2 };
                    modelsHtml += this._renderModelCard(favM, favId === currentModel, modelStates ? modelStates[favId] : null);
                }
                modelsHtml += '</div>';
            }
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
                scaleHtml += '<button class="ai-menu__scale' + (sActive ? ' ai-menu__scale--active' : '') + '" data-scale-val="' + s +
                    '" aria-pressed="' + (sActive ? 'true' : 'false') + '">' + s + '&times;</button>';
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

            // The engines realtime playback can run on, in the order the Realtime tab lists them.
            var engines = [
                ['server', 'Server AI', 'Your AI service upscales captured frames. Best quality; needs a GPU to keep up.'],
                ['lanczos', 'Lanczos', 'Sharpening scaler in this browser. Light, and never touches the server.'],
                ['anime4k', 'Anime4K', 'Line-art shader for anime, in this browser.'],
                ['ai-webgpu', 'WebGPU AI', 'Small neural network in this browser. Experimental.']
            ];
            var enginesHtml = '';
            for (var ei = 0; ei < engines.length; ei++) {
                enginesHtml += '<li class="ai-menu__engine" data-engine="' + engines[ei][0] + '">' +
                    '<span class="ai-menu__engine-name">' + engines[ei][1] + '</span>' +
                    '<span class="ai-menu__engine-desc">' + engines[ei][2] + '</span></li>';
            }
            this._fpsHistory = [];

            menu.innerHTML =
                '<div class="ai-menu__grip" aria-hidden="true"></div>' +
                '<div class="ai-menu__header">' +
                    '<div class="ai-menu__brand">' +
                        '<span class="material-icons ai-menu__logo" aria-hidden="true">auto_awesome</span>' +
                        '<div>' +
                            '<div class="ai-menu__title">AI Upscaler</div>' +
                            '<div class="ai-menu__version">v' + PLUGIN_VERSION + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="ai-menu__header-right">' +
                        '<button class="ai-menu__switch' + (isEnabled ? ' ai-menu__switch--on' : '') + '" data-action="toggle" role="switch" aria-checked="' + (isEnabled ? 'true' : 'false') + '" aria-label="Upscaling" title="' + (isEnabled ? 'Disable' : 'Enable') + ' upscaling"></button>' +
                        '<button class="ai-menu__close" data-action="close" aria-label="Close"><span class="material-icons" aria-hidden="true">close</span></button>' +
                    '</div>' +
                '</div>' +
                // Live readout: frame rate and its 20 s trend, state, engine and model.
                '<div class="ai-menu__status" data-status-row>' +
                    '<div class="ai-menu__readout">' +
                        '<span class="ai-menu__status-fps" data-status-fps><b>—</b> fps</span>' +
                        '<svg class="ai-menu__spark" data-status-spark viewBox="0 0 120 36" preserveAspectRatio="none" aria-hidden="true">' +
                            '<line class="ai-menu__spark-target" x1="0" x2="120" y1="-10" y2="-10"></line>' +
                            '<polyline class="ai-menu__spark-line" points=""></polyline>' +
                        '</svg>' +
                        '<span class="ai-menu__pill">' +
                            '<span class="ai-menu__status-dot ai-menu__status-dot--off" data-status-dot></span>' +
                            '<span class="ai-menu__status-state" data-status-state>IDLE</span>' +
                        '</span>' +
                    '</div>' +
                    '<div class="ai-menu__status-meta">' +
                        '<span class="ai-menu__status-mode" data-status-mode>—</span>' +
                        '<span class="ai-menu__status-sep" aria-hidden="true">·</span>' +
                        '<span class="ai-menu__status-model" data-status-model>' + this._escapeHtml(currentModel) + '</span>' +
                    '</div>' +
                '</div>' +
                '<div class="ai-menu__tabs" role="tablist">' +
                    // v1.8.3.18 - Auto leads. Auto mode is the default (EnableAutoModelSelection
                    // is true out of the box), so the first thing the panel should answer is
                    // "what is it doing to THIS video, and how do I stop it" - not "pick a model
                    // from a list", which is the Custom-mode question.
                    '<button class="ai-menu__tab ai-menu__tab--active" data-tab="auto" role="tab" aria-selected="true"><span class="material-icons" aria-hidden="true">auto_awesome</span><span>Auto</span><span class="ai-menu__tab-live" data-auto-live-dot></span></button>' +
                    '<button class="ai-menu__tab" data-tab="models" role="tab" aria-selected="false"><span class="material-icons" aria-hidden="true">grid_view</span><span>Models</span></button>' +
                    '<button class="ai-menu__tab" data-tab="filters" role="tab" aria-selected="false"><span class="material-icons" aria-hidden="true">tune</span><span>Filters</span><span class="ai-menu__tab-live" data-filter-live-dot></span></button>' +
                    '<button class="ai-menu__tab" data-tab="realtime" role="tab" aria-selected="false"><span class="material-icons" aria-hidden="true">bolt</span><span>Realtime</span></button>' +
                '</div>' +
                '<div class="ai-menu__body">' +
                    // v1.8.3.18 - the auto pane. Everything here writes straight to the
                    // plugin config and takes effect on the running video; nothing needs
                    // the full configuration page. Contents are filled by _renderAutoPane
                    // once the config and the decision for this video have been fetched.
                    '<div class="ai-menu__pane ai-menu__pane--active" data-pane="auto" role="tabpanel">' +
                        '<div data-auto-body><div class="ai-menu__auto-loading">Reading the decision for this video&hellip;</div></div>' +
                    '</div>' +
                    '<div class="ai-menu__pane" data-pane="models" role="tabpanel">' +
                        '<div class="ai-menu__chips" role="group" aria-label="Show models">' +
                            '<button class="ai-menu__chip ai-menu__chip--active" data-filter="all">All</button>' +
                            '<button class="ai-menu__chip" data-filter="ready">Downloaded</button>' +
                            '<button class="ai-menu__chip" data-filter="recommended">Recommended</button>' +
                        '</div>' +
                        '<div class="ai-menu__models">' + modelsHtml + '</div>' +
                        '<div class="ai-menu__section">' +
                            '<div class="ai-menu__section-title"><span>Output scale</span><span class="ai-menu__section-sub">AI models keep their own factor</span></div>' +
                            '<div class="ai-menu__scales" role="group" aria-label="Output scale">' + scaleHtml + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="ai-menu__pane" data-pane="filters" role="tabpanel">' + filtersHtml + '</div>' +
                    '<div class="ai-menu__pane" data-pane="realtime" role="tabpanel">' +
                        '<div class="ai-menu__rt-card">' +
                            '<div class="ai-menu__rt-status">' +
                                '<span class="ai-menu__rt-indicator" id="aiRtIndicator"></span>' +
                                '<span class="ai-menu__rt-label">Realtime</span>' +
                                '<span class="ai-menu__rt-value" id="aiRtStatusValue">--</span>' +
                            '</div>' +
                            '<div class="ai-menu__rt-row">' +
                                '<button class="ai-menu__filter-btn ai-menu__filter-btn--primary" data-action="rt-toggle" data-rt-toggle>Start</button>' +
                                '<button class="ai-menu__filter-btn ai-menu__filter-btn--secondary" data-action="rt-switch" data-rt-switch disabled>Switch engine</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="ai-menu__section">' +
                            '<div class="ai-menu__section-title"><span>Engines</span><span class="ai-menu__section-sub">default set in All settings</span></div>' +
                            '<ul class="ai-menu__engines">' + enginesHtml + '</ul>' +
                        '</div>' +
                    '</div>' +
                '</div>' +
                '<div class="ai-menu__footer">' +
                    '<span class="ai-menu__summary">' +
                        '<span class="ai-menu__summary-dot' + (readyModels > 0 ? '' : ' ai-menu__summary-dot--off') + '"></span>' +
                        '<span><span class="ai-menu__summary-strong" data-summary-ready>' + (totalModels ? (readyModels + ' of ' + totalModels) : '—') + '</span> models downloaded</span>' +
                    '</span>' +
                    '<button class="ai-menu__action" data-action="config">All settings<span class="material-icons" aria-hidden="true">chevron_right</span></button>' +
                '</div>';

            // After DOM insertion, show what realtime is doing and label its buttons to match.
            setTimeout(function() {
                var statusEl = document.getElementById('aiRtStatusValue');
                var indicator = document.getElementById('aiRtIndicator');
                if (!statusEl) return;
                var st = RealtimeUpscaler.getStatus();
                var rtMenu = statusEl.closest('.ai-menu');
                var toggleBtn = rtMenu && rtMenu.querySelector('[data-rt-toggle]');
                var switchBtn = rtMenu && rtMenu.querySelector('[data-rt-switch]');
                statusEl.textContent = st.active ? PlayerIntegration._modeLabel(st.mode) + ' · ' + st.fps + ' fps' : (st.reason || 'Stopped');
                statusEl.classList.toggle('ai-menu__rt-value--on', !!st.active);
                if (indicator) indicator.classList.toggle('ai-menu__rt-indicator--on', !!st.active);
                if (toggleBtn) toggleBtn.textContent = st.active ? 'Stop' : 'Start';
                if (switchBtn) {
                    switchBtn.disabled = !st.active;
                    switchBtn.textContent = !st.active ? 'Switch engine'
                        : st.mode === 'server' ? 'Switch to Lanczos' : 'Switch to Server AI';
                }
                var engineKey = st.mode === 'webgl' ? 'lanczos' : st.mode;
                var rows = rtMenu ? rtMenu.querySelectorAll('[data-engine]') : [];
                for (var r = 0; r < rows.length; r++) {
                    rows[r].classList.toggle('ai-menu__engine--on', !!st.active && rows[r].getAttribute('data-engine') === engineKey);
                }
            }, 50);

            document.body.appendChild(menu);

            // v1.6.1.13: apply any previously-chosen CSS filter and refresh the state
            // from the server so the filter pane shows the current saved preset.
            this._applyFilterState(filterState);
            this._loadFilterConfig();

            // v1.8.3.18 - Auto is the landing tab, so fill it right away instead of
            // waiting for a tab click the user has no reason to make.
            this._renderAutoPane(menu);

            // Any interaction on the menu keeps it alive — important for filter sliders
            // where users may drag for several seconds. _touchMenuTimer resets the auto-close.
            var touchTimer = function() { PlayerIntegration._touchMenuTimer(menu); };
            menu.addEventListener('pointerdown', touchTimer);
            // Remote controls and keyboards navigate with keys, not pointers: keep the
            // panel open while they do, and let Escape close it.
            menu.addEventListener('keydown', function(e) {
                touchTimer();
                if (e.key === 'Escape') {
                    menu.remove();
                    PlayerIntegration._cleanupMenu();
                }
            });
            menu.addEventListener('input', function(e) {
                touchTimer();
                var slider = e.target.closest('[data-slider]');
                if (slider) {
                    PlayerIntegration._onSliderInput(menu, slider);
                }
                var advInput = e.target.closest('[data-adv-slider]');
                if (advInput) PlayerIntegration._syncSliderFill(advInput);
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
                // v1.8.3.18 - auto-pane switches. Checked before [data-preset] and
                // [data-filter] because those selectors are broad enough to swallow a
                // click that was meant for a switch.
                var autoSw = e.target.closest('[data-auto-toggle]');
                if (autoSw) {
                    PlayerIntegration._toggleAutoAspect(menu, autoSw.getAttribute('data-auto-toggle'));
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
                    } else if (action === 'auto-apply') {
                        PlayerIntegration._applyAutoNow(menu);
                    } else if (action === 'auto-refresh') {
                        PlayerIntegration._renderAutoPane(menu);
                    } else if (action === 'filter-suggest-apply') {
                        var key = target.getAttribute('data-preset-key');
                        target.disabled = true;
                        PlayerIntegration._applySuggestedFilter(key).then(function(ok) {
                            PlayerIntegration.showPlayerNotification(
                                ok ? ('Filter preset set to ' + key) : 'Nothing to apply', 'info');
                            // Re-render: the suggestion has to disappear now that it IS the
                            // current preset, otherwise it would invite an endless re-apply.
                            PlayerIntegration._renderAutoPane(menu);
                        }).catch(function(err) {
                            target.disabled = false;
                            PlayerIntegration.showPlayerNotification(
                                'Could not set the preset: ' + ((err && err.message) || 'unknown error'), 'warning');
                        });
                    } else if (action === 'filter-reset') {
                        PlayerIntegration._resetFilters(menu);
                    } else if (action === 'filter-save') {
                        PlayerIntegration._saveFilterConfig(menu, target);
                    } else if (action === 'filter-advanced-toggle') {
                        var adv = menu.querySelector('[data-adv-pane]');
                        if (adv) adv.classList.toggle('ai-menu__adv--open');
                        var advOpen = !!adv && adv.classList.contains('ai-menu__adv--open');
                        target.setAttribute('aria-expanded', advOpen ? 'true' : 'false');
                        var caret = target.querySelector('.ai-menu__adv-caret');
                        if (caret) caret.textContent = advOpen ? 'expand_less' : 'expand_more';
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
                            RealtimeUpscaler.stop();
                            var video = PlayerIntegration.findVideoElement();
                            if (video) {
                                var overrideConfig = Object.assign({}, PlayerIntegration._cachedConfig || {}, { RealtimeMode: newMode });
                                PlayerIntegration._startRtWithConfig(video, overrideConfig);
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

        // Filled part of a range track, from the neutral point (0, or the minimum) to the
        // thumb, so a +/- slider shows which way it has been pushed.
        _sliderFill: function(min, max, value) {
            var span = (max - min) || 1;
            var pos = Math.max(0, Math.min(100, (value - min) / span * 100));
            var zero = Math.max(0, Math.min(100, (0 - min) / span * 100));
            return { from: Math.min(pos, zero).toFixed(1) + '%', to: Math.max(pos, zero).toFixed(1) + '%' };
        },

        _syncSliderFill: function(input) {
            if (!input || !input.style) return;
            var f = this._sliderFill(parseFloat(input.min), parseFloat(input.max), parseFloat(input.value));
            input.style.setProperty('--ai-from', f.from);
            input.style.setProperty('--ai-to', f.to);
        },

        _buildFiltersPane: function(st) {
            // Preset tiles. Each carries a small canvas that _paintPresetPreviews fills
            // with the current frame under that preset's filter.
            var chipsHtml = '';
            for (var i = 0; i < PRESET_LABELS.length; i++) {
                var key = PRESET_LABELS[i][0];
                var label = PRESET_LABELS[i][1];
                var isOn = st.preset === key;
                chipsHtml += '<button class="ai-menu__preset' + (isOn ? ' ai-menu__preset--active' : '') + '" data-preset="' + key +
                    '" aria-pressed="' + (isOn ? 'true' : 'false') + '">' +
                    '<canvas class="ai-menu__preset-preview" data-preset-preview="' + key + '" width="96" height="54" aria-hidden="true"></canvas>' +
                    '<span class="ai-menu__preset-name">' + label + '</span></button>';
            }

            // Live sliders (CSS filter on <video>)
            var slidersHtml = '';
            for (var j = 0; j < LIVE_SLIDERS.length; j++) {
                var s = LIVE_SLIDERS[j];
                var v = st[s.key];
                var fill = this._sliderFill(s.min, s.max, v);
                slidersHtml +=
                    '<div class="ai-menu__slider-row">' +
                        '<label class="ai-menu__slider-label" for="aiMenuSlider-' + s.key + '">' +
                            '<span class="material-icons ai-menu__slider-icon" aria-hidden="true">' + s.icon + '</span>' +
                            '<span>' + s.label + '</span>' +
                            '<span class="ai-menu__slider-val" data-slider-val="' + s.key + '">' + (v > 0 ? '+' : '') + v + '</span>' +
                        '</label>' +
                        '<input type="range" class="ai-menu__slider" id="aiMenuSlider-' + s.key + '" data-slider="' + s.key + '"' +
                               ' min="' + s.min + '" max="' + s.max + '" step="1" value="' + v + '"' +
                               ' style="--ai-from:' + fill.from + ';--ai-to:' + fill.to + '">' +
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
                var afill = this._sliderFill(a.min, a.max, a.val);
                advHtml +=
                    '<div class="ai-menu__adv-row">' +
                        '<label class="ai-menu__adv-label" for="aiMenuAdv-' + a.key + '">' + a.label +
                            '<span class="ai-menu__adv-val" data-adv-val="' + a.key + '">' + a.val + '</span>' +
                        '</label>' +
                        '<input type="range" class="ai-menu__adv-slider" id="aiMenuAdv-' + a.key + '" data-adv-slider="' + a.key + '"' +
                               ' min="' + a.min + '" max="' + a.max + '" step="' + a.step + '" value="' + a.val + '"' +
                               ' style="--ai-from:' + afill.from + ';--ai-to:' + afill.to + '">' +
                    '</div>';
            }

            return '' +
                '<div class="ai-menu__section">' +
                    '<div class="ai-menu__section-title">' +
                        '<span>Look</span>' +
                        '<span class="ai-menu__section-sub">live, no transcoding</span>' +
                    '</div>' +
                    '<div class="ai-menu__presets">' + chipsHtml + '</div>' +
                '</div>' +
                '<div class="ai-menu__section">' +
                    '<div class="ai-menu__section-title"><span>Fine-tune</span></div>' +
                    '<div class="ai-menu__sliders">' + slidersHtml + '</div>' +
                '</div>' +
                '<div class="ai-menu__section">' +
                    '<button class="ai-menu__adv-toggle" data-action="filter-advanced-toggle" aria-expanded="false">' +
                        '<span>Advanced <span class="ai-menu__adv-note">applies on next seek</span></span>' +
                        '<span class="material-icons ai-menu__adv-caret" aria-hidden="true">expand_more</span>' +
                    '</button>' +
                    '<div class="ai-menu__adv" data-adv-pane>' + advHtml + '</div>' +
                '</div>' +
                '<div class="ai-menu__filter-actions">' +
                    '<button class="ai-menu__filter-btn ai-menu__filter-btn--secondary" data-action="filter-reset">Reset</button>' +
                    '<button class="ai-menu__filter-btn ai-menu__filter-btn--primary" data-action="filter-save"' + (st.canSave ? '' : ' disabled title="Admin privileges required"') + '>Save</button>' +
                '</div>';
        },

        // ==================================================================
        // v1.8.3.18 — AUTO PANE
        //
        // Auto mode has been the default since v1.8.3.12, but the in-player panel
        // only ever offered the Custom-mode questions ("pick a model", "pick a
        // filter"). What it could not answer was the one that matters while a video
        // is running: what did auto decide for THIS file, why, and how do I turn it
        // off without leaving the player. Everything below writes to the plugin
        // config and takes effect immediately — no full configuration page, no reload.
        // ==================================================================

        // Everything the auto pane interpolates goes through this. Most of it is
        // server-generated, but ActiveFilterPreset and FaceRestoreModel are config
        // strings an admin can set to anything, and they land in innerHTML.
        // Same escape set as _escHtml in configurationpage.html.
        _escapeHtml: function(s) {
            return String(s == null ? '' : s).replace(/[&<>"']/g, function(c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        },

        // Ask the backend what auto would do for the video that is playing right now.
        // Resolves with the raw /recommend-model payload or null; never rejects, because
        // a panel that fails to open is worse than one showing "service unreachable".
        _fetchAutoDecision: function() {
            var video = this.findVideoElement();
            var w = (video && video.videoWidth) | 0;
            var h = (video && video.videoHeight) | 0;
            // v1.8.3.19 - whether the answer is about THIS file or a generic default.
            // With no video element the resolver is handed 0x0 and falls through to its
            // HD branch; presenting that as "running now" would state a default as a fact.
            var known = w > 0 && h > 0;
            var itemId = this._getPlayingItemId();
            var fetchItem = itemId && window.ApiClient
                ? window.ApiClient.getItem(ApiClient.getCurrentUserId(), itemId).catch(function() { return null; })
                : Promise.resolve(null);

            return fetchItem.then(function(item) {
                var genres = (item && Array.isArray(item.Genres)) ? item.Genres.join(',') : '';
                var url = ApiClient.getUrl('Upscaler/recommend-model') +
                    '?width=' + w + '&height=' + h + '&isBatch=false' +
                    (genres ? ('&genres=' + encodeURIComponent(genres)) : '');
                return ApiClient.ajax({ type: 'GET', url: url, dataType: 'json' })
                    .then(function(res) {
                        if (res) { res._sourceKnown = known; }
                        return res;
                    });
            }).catch(function() { return null; });
        },

        _autoRow: function(key, label, sub, on, disabled) {
            return '<div class="ai-menu__auto-row' + (disabled ? ' ai-menu__auto-row--off' : '') + '">' +
                       '<div class="ai-menu__auto-row-text">' +
                           '<span class="ai-menu__auto-row-label">' + label + '</span>' +
                           '<span class="ai-menu__auto-row-sub">' + sub + '</span>' +
                       '</div>' +
                       '<button class="ai-menu__switch ai-menu__switch--sm' + (on ? ' ai-menu__switch--on' : '') +
                           '" data-auto-toggle="' + key + '" role="switch" aria-checked="' + (on ? 'true' : 'false') +
                           '" aria-label="' + label + '"></button>' +
                   '</div>';
        },

        _renderAutoPane: function(menu) {
            var body = menu.querySelector('[data-auto-body]');
            if (!body) return;

            Promise.all([this.getPluginConfig(), this._fetchAutoDecision()]).then(function(res) {
                var cfg = res[0] || {};
                var pick = res[1];
                var autoOn = cfg.EnableAutoModelSelection !== false;
                var esc = PlayerIntegration._escapeHtml;

                var dot = menu.querySelector('[data-auto-live-dot]');
                if (dot) dot.classList.toggle('ai-menu__tab-live--on', autoOn);

                var html = '';

                // 1. The master switch. First, big, and honest about what "off" means.
                html += '<div class="ai-menu__auto-master' + (autoOn ? ' ai-menu__auto-master--on' : '') + '">' +
                            '<div class="ai-menu__auto-master-text">' +
                                '<span class="ai-menu__auto-master-title">Auto mode' +
                                    '<span class="ai-menu__auto-state">' + (autoOn ? 'ON' : 'OFF') + '</span></span>' +
                                '<span class="ai-menu__auto-master-sub">' + (autoOn
                                    ? 'Model is chosen per video from content, resolution and what your hardware can run.'
                                    : 'Your manual model, scale and filter settings apply exactly as configured.') + '</span>' +
                            '</div>' +
                            '<button class="ai-menu__switch' + (autoOn ? ' ai-menu__switch--on' : '') +
                                '" data-auto-toggle="master" role="switch" aria-checked="' + (autoOn ? 'true' : 'false') +
                                '" aria-label="Auto mode"></button>' +
                        '</div>';

                // 2. What it decided for THIS video, with the reasoning the resolver returns.
                if (pick && pick.success) {
                    var scaleNum = parseInt(pick.recommended_scale, 10) || 0;
                    var over = pick.substitution_reason
                        ? '<div class="ai-menu__auto-warn">' + esc(pick.substitution_reason) + '</div>' : '';
                    var sig = (pick.signals && pick.signals.length)
                        ? '<details class="ai-menu__auto-signals"><summary>What it looked at</summary><ul>' +
                          pick.signals.map(function(s) { return '<li>' + esc(s) + '</li>'; }).join('') +
                          '</ul></details>'
                        : '';
                    // v1.8.3.19 - three honest headings instead of one confident one.
                    // Without video dimensions the answer is a default, not a decision
                    // about this file, and the output size is unknowable - so it is
                    // omitted rather than printed as the string "unknown output size".
                    var head = !pick._sourceKnown ? 'Default pick - start playback for this file'
                             : autoOn ? 'Running now' : 'Auto would pick';
                    html += '<div class="ai-menu__auto-card">' +
                                '<div class="ai-menu__auto-card-head">' +
                                    '<span class="ai-menu__auto-card-title">' + head + '</span>' +
                                    // Coerced rather than escaped: this is the only numeric
                                    // interpolation in the pane, and "every value entering
                                    // innerHTML is escaped or coerced" is a rule you can audit
                                    // at a glance - "this one is safe because of where it comes
                                    // from" is not.
                                    (scaleNum ? '<span class="ai-menu__auto-scale">' + scaleNum + '&times;</span>' : '') +
                                '</div>' +
                                '<div class="ai-menu__auto-model">' + esc(pick.recommended_model || '—') + '</div>' +
                                (pick._sourceKnown && pick.output_size ? '<div class="ai-menu__auto-size">' + esc(pick.output_size) + '</div>' : '') +
                                (pick._sourceKnown && pick.reason ? '<div class="ai-menu__auto-reason">' + esc(pick.reason) + '</div>' : '') +
                                over + sig +
                            '</div>';
                } else {
                    html += '<div class="ai-menu__auto-card ai-menu__auto-card--muted">' +
                                'No decision available — the AI service did not answer. ' +
                                'Auto never blocks playback: your configured model is used instead.' +
                            '</div>';
                }

                // 3. The filter SUGGESTION. Shown only when auto actually has a different
                //    opinion than your current preset - a suggestion that repeats what you
                //    already chose is noise. Applying is one click and goes through the
                //    same /filter-config save path as a manual pick; nothing happens on
                //    its own. Not shown in Custom mode: there is no auto decision to offer.
                var suggested = (pick && pick.recommended_filter) || '';
                var currentPreset = (cfg.ActiveFilterPreset || '').toLowerCase();
                if (autoOn && suggested && suggested !== 'none' && suggested.toLowerCase() !== currentPreset) {
                    html += '<div class="ai-menu__auto-suggest" data-filter-suggestion>' +
                                '<div class="ai-menu__auto-suggest-text">' +
                                    '<span class="ai-menu__auto-suggest-label">Suggested look' +
                                        (pick.filter_reason ? ' - ' + esc(pick.filter_reason) : '') + '</span>' +
                                    '<span class="ai-menu__auto-suggest-value">' + esc(suggested) +
                                        (currentPreset ? ' <span class="ai-menu__auto-suggest-cur">(now: ' + esc(cfg.ActiveFilterPreset) + ')</span>' : '') +
                                    '</span>' +
                                '</div>' +
                                '<button class="ai-menu__filter-btn ai-menu__filter-btn--primary" ' +
                                    'data-action="filter-suggest-apply" data-preset-key="' + esc(suggested) + '">Apply</button>' +
                            '</div>';
                }

                // 3. Live switches for the four things auto touches. Filters are listed
                //    but deliberately described as a suggestion: v1.8.3.14 stopped auto
                //    overwriting a preset the user picked, and this text must not
                //    promise otherwise.
                html += '<div class="ai-menu__section-title" style="margin-top:14px"><span>Live controls</span>' +
                        '<span class="ai-menu__section-sub">applies immediately</span></div>';
                html += PlayerIntegration._autoRow('filters', 'Video filters',
                            (cfg.ActiveFilterPreset && cfg.ActiveFilterPreset !== 'none')
                                ? 'Your preset: ' + esc(cfg.ActiveFilterPreset) + ' — auto only suggests, never overwrites'
                                : 'Auto may suggest a look for this content',
                            cfg.EnableVideoFilters === true, false);
                html += PlayerIntegration._autoRow('face', 'Face restoration',
                            esc(cfg.FaceRestoreModel || 'gfpgan-v1.4') + ' — sharpens faces, costs extra time per frame',
                            cfg.EnableFaceRestore === true, false);
                html += PlayerIntegration._autoRow('realtime', 'Real-time upscaling',
                            'Upscale during playback instead of only in batch jobs',
                            cfg.EnableRealtimeUpscaling !== false, false);
                html += PlayerIntegration._autoRow('mask', 'Cover objects',
                            esc(cfg.ObjectMaskClasses || 'animals') + ' — ' +
                            (cfg.ObjectMaskMode === 'blur' ? 'blurred' : 'covered with a box') +
                            '. Replaces upscaling on this stream: two inference passes per frame ' +
                            'do not keep up with playback',
                            cfg.EnableObjectMasking === true, false);

                html += '<div class="ai-menu__auto-actions">' +
                            '<button class="ai-menu__filter-btn ai-menu__filter-btn--primary" data-action="auto-apply"' +
                                (autoOn ? '' : ' disabled title="Turn auto mode on first"') + '>Re-apply to this video</button>' +
                            '<button class="ai-menu__filter-btn ai-menu__filter-btn--secondary" data-action="auto-refresh">Refresh</button>' +
                        '</div>';

                body.innerHTML = html;
            }).catch(function(err) {
                body.innerHTML = '<div class="ai-menu__auto-card ai-menu__auto-card--muted">Could not read the auto state: ' +
                    PlayerIntegration._escapeHtml((err && err.message) || 'unknown error') + '</div>';
            });
        },

        // One switch -> one config field -> immediate effect. Each branch states what
        // "immediate" means for that field, because they differ: two are read per frame,
        // one is read when the RT loop starts.
        _toggleAutoAspect: function(menu, key) {
            var map = {
                master: 'EnableAutoModelSelection',
                filters: 'EnableVideoFilters',
                face: 'EnableFaceRestore',
                realtime: 'EnableRealtimeUpscaling',
                mask: 'EnableObjectMasking'
            };
            var field = map[key];
            if (!field) return;

            this.getPluginConfig().then(function(cfg) {
                var next = !(key === 'realtime' ? (cfg.EnableRealtimeUpscaling !== false)
                          : key === 'master'   ? (cfg.EnableAutoModelSelection !== false)
                          : cfg[field] === true);
                var patch = {};
                patch[field] = next;
                return PlayerIntegration.updatePluginConfig(patch).then(function() {
                    PlayerIntegration.showPlayerNotification(
                        (key === 'master' ? 'Auto mode' :
                         key === 'filters' ? 'Video filters' :
                         key === 'face' ? 'Face restoration' : key === 'mask' ? 'Object masking' : 'Real-time upscaling') +
                        (next ? ' on' : ' off'), 'info');

                    // Real-time upscaling reads its config when the loop starts, so a
                    // toggle only takes hold if the loop is restarted.
                    if (key === 'realtime' || key === 'mask') {
                        PlayerIntegration.startRealtimeUpscaling();
                    }
                    PlayerIntegration._renderAutoPane(menu);
                });
            }).catch(function(err) {
                PlayerIntegration.showPlayerNotification(
                    'Could not save: ' + ((err && err.message) || 'unknown error'), 'warning');
            });
        },

        // Re-run the decision and hand it to the running upscaler, without a reload.
        _applyAutoNow: function(menu) {
            var generation = RealtimeUpscaler._generation;
            return this.getPluginConfig().then(function(cfg) {
                if (generation !== RealtimeUpscaler._generation) return;
                var video = PlayerIntegration.findVideoElement();
                if (!video) {
                    PlayerIntegration.showPlayerNotification('No video is playing', 'warning');
                    return;
                }
                return PlayerIntegration._autoSelectForVideo(video, cfg).then(function(pick) {
                    if (generation !== RealtimeUpscaler._generation) return;
                    if (!pick || !pick.model) {
                        PlayerIntegration.showPlayerNotification('Auto had nothing to apply', 'warning');
                        return;
                    }
                    // v1.8.3.20 - the model is applied, the filter is not: it is only
                    // ever offered as a suggestion below.
                    PlayerIntegration._startRtWithConfig(video, Object.assign({}, cfg, { Model: pick.model }));
                    var msg = 'Applied ' + pick.model + (pick.reason ? ' — ' + pick.reason : '');
                    PlayerIntegration.showPlayerNotification(msg, pick.substitutedFrom ? 'warning' : 'info');
                    PlayerIntegration._renderAutoPane(menu);
                });
            }).catch(function(err) {
                PlayerIntegration.showPlayerNotification(
                    'Apply failed: ' + ((err && err.message) || 'unknown error'), 'warning');
            });
        },

        _switchTab: function(menu, tabName) {
            var tabs = menu.querySelectorAll('[data-tab]');
            var panes = menu.querySelectorAll('[data-pane]');
            for (var i = 0; i < tabs.length; i++) {
                var on = tabs[i].getAttribute('data-tab') === tabName;
                tabs[i].classList.toggle('ai-menu__tab--active', on);
                tabs[i].setAttribute('aria-selected', on ? 'true' : 'false');
            }
            for (var j = 0; j < panes.length; j++) {
                panes[j].classList.toggle('ai-menu__pane--active', panes[j].getAttribute('data-pane') === tabName);
            }
            // v1.8.3.18 - the auto pane describes the video that is playing right now,
            // so it is re-read on every visit rather than cached from when the panel
            // opened. Seeking to a different episode changes the answer.
            if (tabName === 'auto') this._renderAutoPane(menu);
            if (tabName === 'filters') this._paintPresetPreviews(menu);
        },

        // Highlight one preset tile (or none, when the sliders made a custom look).
        _markPreset: function(menu, preset) {
            var presets = menu.querySelectorAll('[data-preset]');
            for (var i = 0; i < presets.length; i++) {
                var on = presets[i].getAttribute('data-preset') === preset;
                presets[i].classList.toggle('ai-menu__preset--active', on);
                presets[i].setAttribute('aria-pressed', on ? 'true' : 'false');
            }
        },

        // Paint each preset tile with the frame on screen right now, under that preset's
        // CSS filter, so the choice is "this, on my video" instead of a name. Drawing a
        // video into a canvas never reads pixels back, so a cross-origin stream is fine.
        _paintPresetPreviews: function(menu) {
            var tiles = menu.querySelectorAll('canvas[data-preset-preview]');
            var video = this.findVideoElement();
            var live = !!(video && video.readyState >= 2 && video.videoWidth > 0);
            for (var i = 0; i < tiles.length; i++) {
                var c = tiles[i];
                var ctx = c.getContext && c.getContext('2d');
                if (!ctx) continue;
                var drawn = false;
                if (live) {
                    try {
                        // Cover-crop the frame to the tile's shape.
                        var vw = video.videoWidth, vh = video.videoHeight, r = c.width / c.height;
                        var cw = Math.min(vw, vh * r), ch = cw / r;
                        ctx.drawImage(video, (vw - cw) / 2, (vh - ch) / 2, cw, ch, 0, 0, c.width, c.height);
                        drawn = true;
                    } catch (e) { drawn = false; }
                }
                if (!drawn) {
                    var g = ctx.createLinearGradient(0, 0, 0, c.height);
                    g.addColorStop(0, '#1d3b5a'); g.addColorStop(0.6, '#b7715a'); g.addColorStop(1, '#f2c37e');
                    ctx.fillStyle = g;
                    ctx.fillRect(0, 0, c.width, c.height);
                }
                c.style.filter = PRESET_CSS[c.getAttribute('data-preset-preview')] || 'none';
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
            this._syncSliderFill(slider);
            this._markPreset(menu, null);
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
            this._syncSliderFill(slider);
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
            this._markPreset(menu, preset);
            var sliders = menu.querySelectorAll('[data-slider]');
            for (var j = 0; j < sliders.length; j++) {
                sliders[j].value = 0;
                this._syncSliderFill(sliders[j]);
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
            // A realtime client-side upscaler (Anime4K / WebGL / WebGPU) renders to a
            // canvas overlay (z-index 999) that covers the — often opacity:0 — <video>.
            // The CSS filter on the hidden video then has no visible effect, so apply it
            // to the visible canvas too. Without this the filters "don't take" whenever
            // realtime upscaling is on (which auto-starts on every video since 10.11).
            var parent = video.parentElement;
            if (parent) {
                var canvases = parent.querySelectorAll('canvas');
                for (var i = 0; i < canvases.length; i++) canvases[i].style.filter = css || '';
            }
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
                        self._markPreset(menu, self._filterState.preset);
                        // The pane was rendered before this answer arrived, with Save
                        // disabled; the server has now answered, so let the POST decide.
                        var save = menu.querySelector('[data-action="filter-save"]');
                        if (save) { save.disabled = false; save.removeAttribute('title'); }
                    }
                }
            }).catch(function(err) {
                console.warn('AI Upscaler: filter-config fetch failed:', err);
            });
        },

        _resetFilters: function(menu) {
            var canSave = !!(this._filterState && this._filterState.canSave);
            this._filterState = this._defaultFilterState();
            this._filterState.canSave = canSave;
            // Reflect default state in all controls
            var sliders = menu.querySelectorAll('[data-slider]');
            for (var i = 0; i < sliders.length; i++) {
                sliders[i].value = 0;
                this._syncSliderFill(sliders[i]);
                var k = sliders[i].getAttribute('data-slider');
                var valEl = menu.querySelector('[data-slider-val="' + k + '"]');
                if (valEl) valEl.textContent = '0';
            }
            // The advanced sliders reset too, so what they show matches what Save sends.
            var advSliders = menu.querySelectorAll('[data-adv-slider]');
            for (var a = 0; a < advSliders.length; a++) {
                var ak = advSliders[a].getAttribute('data-adv-slider');
                advSliders[a].value = this._filterState[ak];
                this._syncSliderFill(advSliders[a]);
                var advVal = menu.querySelector('[data-adv-val="' + ak + '"]');
                if (advVal) advVal.textContent = String(this._filterState[ak]);
            }
            this._markPreset(menu, 'none');
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
            // A pinned favorite has two rows (Favorites and its category); both follow the load.
            var modelBtns = menu ? Array.prototype.slice.call(menu.querySelectorAll('[data-model="' + model + '"]')) : [];
            var slots = menu ? Array.prototype.slice.call(menu.querySelectorAll('[data-state-slot="' + model + '"]')) : [];
            function showState(kind, html) {
                slots.forEach(function(slot) {
                    slot.classList.remove('ai-menu__state--ready', 'ai-menu__state--need-dl', 'ai-menu__state--busy', 'ai-menu__state--err');
                    slot.classList.add('ai-menu__state--' + kind);
                    slot.innerHTML = html;
                });
            }
            var state = (this._modelStates && this._modelStates[model]) || null;
            var needsDownload = state && !state.downloaded && state.available;

            // Block if model is not available at all
            if (state && !state.available) {
                this.showPlayerNotification(model + ' is not yet available', 'warning');
                return;
            }

            // Model loading changes the service used by the frame loop. Stop that
            // loop now and bind this entire startup to the same cancellation token.
            RealtimeUpscaler.stop();
            var generation = RealtimeUpscaler._generation;
            var loading = new AbortController();
            RealtimeUpscaler._startupController = loading;
            function current() { return generation === RealtimeUpscaler._generation && !loading.signal.aborted; }

            // Show inline spinner on the clicked model; keep menu open
            modelBtns.forEach(function(b) { b.classList.add('ai-menu__model--loading'); });
            showState('busy', '<div class="ai-menu__spinner"></div>');
            this.showPlayerNotification(
                (needsDownload ? 'Downloading ' : 'Loading ') + model + (needsDownload ? ' (may take 30-120s)' : '...'),
                'info'
            );

            return this.updatePluginConfig({ Model: model }).then(function() {
                if (!current()) return;
                var loadUrl = ApiClient.getUrl('Upscaler/models/load') + '?model_name=' + encodeURIComponent(model);
                return fetch(loadUrl, {
                    method: 'POST',
                    headers: { 'Authorization': 'MediaBrowser Token="' + ApiClient.accessToken() + '"' },
                    credentials: 'include',
                    signal: loading.signal
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
                if (!current()) return;
                // Update active styling + refresh states
                if (menu) {
                    menu.querySelectorAll('.ai-menu__model').forEach(function(b) {
                        b.classList.remove('ai-menu__model--active');
                        b.setAttribute('aria-pressed', 'false');
                    });
                }
                modelBtns.forEach(function(b) {
                    b.classList.remove('ai-menu__model--loading');
                    b.classList.add('ai-menu__model--active');
                    b.setAttribute('aria-pressed', 'true');
                });
                showState('ready', self._stateGlyph('ready'));
                if (self._modelStates && self._modelStates[model]) {
                    self._modelStates[model].downloaded = true;
                    self._modelStates[model].loaded = true;
                }
                self.showPlayerNotification('Model ready: ' + model, 'success');
                var video = self.findVideoElement();
                if (video) {
                    return self.getPluginConfig().then(function(cfg) {
                        if (!current()) return;
                        // Recheck HDR/driver/masking rules and benchmark this model;
                        // a result measured for the previous model is not reusable.
                        return self._startRtWithConfig(video, cfg);
                    });
                }
            }).catch(function(err) {
                if (!current()) return;
                console.error('AI Upscaler: quickSetModel failed', err);
                modelBtns.forEach(function(b) { b.classList.remove('ai-menu__model--loading'); });
                showState('err', self._stateGlyph('err'));
                var msg = (err && err.message) ? err.message : 'unknown error';
                // Surface config-specific hint when AI service token is missing
                if (/API_TOKEN|403|401/.test(msg)) {
                    msg = 'AI service auth not configured. Open Full Configuration → AI Service → set API Token.';
                }
                self.showPlayerNotification('Failed: ' + msg, 'error');
            }).finally(function() {
                if (RealtimeUpscaler._startupController === loading) RealtimeUpscaler._startupController = null;
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
                var sw = document.querySelector('#aiUpscalerQuickMenu [data-action="toggle"]');
                if (sw) {
                    sw.classList.toggle('ai-menu__switch--on', newState);
                    sw.setAttribute('aria-checked', newState ? 'true' : 'false');
                    sw.title = (newState ? 'Disable' : 'Enable') + ' upscaling';
                }
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

        // v1.8.3.30, issue #77 - same defect as the config page had: every notification was
        // its own position:fixed element at the same corner, so a second one covered the
        // first instead of stacking. Host does the stacking; an identical message still on
        // screen restarts its timer rather than being duplicated; the stack is capped.
        _notifHost: function() {
            var host = document.querySelector('.ai-notif-host');
            if (!host) {
                host = document.createElement('div');
                host.className = 'ai-notif-host';
                document.body.appendChild(host);
            }
            return host;
        },

        showPlayerNotification: function(message, type) {
            type = type || 'info';
            var host = this._notifHost();

            var existing = host.querySelectorAll('.ai-notif');
            for (var i = 0; i < existing.length; i++) {
                if (existing[i].textContent === message) {
                    clearTimeout(existing[i]._aiTimer);
                    existing[i]._aiTimer = setTimeout(function (el) {
                        return function () { if (el.parentElement) el.remove(); };
                    }(existing[i]), 3000);
                    return;
                }
            }

            var notification = document.createElement('div');
            notification.className = 'ai-notif ai-notif--' + type;
            notification.textContent = message;
            host.appendChild(notification);

            while (host.children.length > 4) {
                clearTimeout(host.children[0]._aiTimer);
                host.children[0].remove();
            }

            notification._aiTimer = setTimeout(function() {
                if (notification.parentElement) notification.remove();
                var h = document.querySelector('.ai-notif-host');
                if (h && !h.children.length) h.remove();
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
                        // v1.8.3.13 - the auto decision used to land in the developer
                        // console only; carry it out so the UI can show it.
                        var autoMsg = 'Auto: ' + res.recommended_model +
                            (res.reason ? ' — ' + res.reason : '');
                        if (res.substituted_from) {
                            autoMsg += ' (' + res.substituted_from + ' unavailable — stand-in used)';
                        }
                        console.log('AI Upscaler ' + autoMsg +
                            ' (' + w + 'x' + h + ', genres=' + (genres || 'none') + ')');
                        return {
                            model: res.recommended_model,
                            filter: res.recommended_filter,
                            reason: res.reason,
                            substitutedFrom: res.substituted_from
                        };
                    });
            }).catch(function(err) {
                console.warn('AI Upscaler Auto: recommend-model failed —', err && (err.message || err));
                return null;
            });
        },

        // v1.8.3.20 - auto no longer applies a filter, at all.
        //
        // The UI promised "AI picks model + filter per video" and the code half-kept it:
        // v1.8.3.14 stopped auto OVERWRITING a preset you had chosen, but it still wrote
        // its own choice whenever the field was empty - so the first playback silently
        // decided your look, and the setting then read as if you had picked it.
        //
        // Models are technique: swapping one for a hardware reason is a correction the
        // user cannot make better themselves. A filter is taste. Auto now surfaces its
        // suggestion in the Auto tab with an Apply button and writes nothing on its own.
        // ResolveFilterForVideo keeps its single caller - the display endpoint - which is
        // exactly what a suggestion needs.
        _applySuggestedFilter: function(presetKey) {
            if (!presetKey || presetKey === 'none') return Promise.resolve(false);
            return ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl('Upscaler/filter-config'),
                // v1.8.3.22 - the DTO is FilterConfigUpdate { Enabled, Preset, ... }
                // (UpscalerController.cs). This posted ActiveFilterPreset/EnableVideoFilters -
                // the CONFIG field names - so ASP.NET bound nothing, every property stayed
                // null, and the endpoint still answered success:true. The Apply button has
                // therefore never worked since it shipped in v1.8.3.20: the toast claimed the
                // preset was set, the config was untouched, and the suggestion came straight
                // back on the next render.
                data: JSON.stringify({ Preset: presetKey, Enabled: true }),
                contentType: 'application/json',
                dataType: 'json'
            }).then(function() { return true; });
        },

        startRealtimeUpscaling: function() {
            RealtimeUpscaler.stop();
            var generation = RealtimeUpscaler._generation;
            return this.getPluginConfig().then(function(config) {
                if (generation !== RealtimeUpscaler._generation) return;
                if (config.EnableRealtimeUpscaling === false && config.EnableObjectMasking !== true) return;

                var video = PlayerIntegration.findVideoElement();
                if (!video) {
                    console.log('AI Upscaler RT: No video element found');
                    return;
                }

                if (config.ClientDriverUpscalingActive === true || config.EnableObjectMasking === true) {
                    return PlayerIntegration._startRtWithConfig(video, config);
                }

                // Auto-Mode hook: if the user opted in, let the plugin pick model+filter
                // for *this* video based on genres + resolution. Overrides config.Model
                // for this session only — does not persist back to config.
                return PlayerIntegration._autoSelectForVideo(video, config).then(function(pick) {
                    if (generation !== RealtimeUpscaler._generation) return;
                    if (pick && pick.model) {
                        config = Object.assign({}, config, { Model: pick.model });
                        // v1.8.3.13 - say WHY, and never swap the model silently: a
                        // substitution (preferred model has no public ONNX) is shown as
                        // a warning instead of looking like a wrong pick.
                        // v1.8.3.20 - this used to read "Auto: <model> + <filter>", which
                        // was true only while auto applied the filter itself. It no longer
                        // does, so the notice names the model it really set and points at
                        // the tab where the look is OFFERED rather than implying it is on.
                        var notice = 'Auto: ' + pick.model;
                        if (pick.reason) notice += ' — ' + pick.reason;
                        if (pick.substitutedFrom) notice += ' (' + pick.substitutedFrom + ' unavailable — stand-in used)';
                        if (pick.filter && pick.filter !== 'none') notice += ' · look suggested in the Auto tab';
                        PlayerIntegration.showPlayerNotification(notice, pick.substitutedFrom ? 'warning' : 'info');
                    }
                    return PlayerIntegration._startRtWithConfig(video, config);
                });
            }).catch(function(err) {
                console.error('AI Upscaler: config fetch failed for RT upscaling', err);
            });
        },

        // Extracted from startRealtimeUpscaling so auto-select can supply an overridden config.
        _readPlayingVideoStream: function() {
            var itemId = this._getPlayingItemId();
            if (!itemId) return Promise.resolve(null);
            return ApiClient.getItem(ApiClient.getCurrentUserId(), itemId).then(function(item) {
                var streams = item && (item.MediaStreams || (item.MediaSources && item.MediaSources[0] && item.MediaSources[0].MediaStreams)) || [];
                return streams.find(function(s) { return s.Type === 'Video'; }) || null;
            }).catch(function() { return null; });
        },

        _startRtWithConfig: function(video, config) {
                RealtimeUpscaler.stop();
                var generation = RealtimeUpscaler._generation;
                if (config.ClientDriverUpscalingActive === true && config.EnableObjectMasking !== true) {
                    RealtimeUpscaler.start(video, config, null);
                    return Promise.resolve();
                }
                return this._readPlayingVideoStream().then(function(stream) {
                if (generation !== RealtimeUpscaler._generation) return;
                var transfer = ((stream && stream.ColorTransfer) || '').toLowerCase();
                var range = ((stream && (stream.VideoRangeType || stream.VideoRange)) || '').toLowerCase();
                if (!stream || (range && range !== 'sdr') ||
                    (transfer && ['bt709', 'srgb', 'iec61966-2-1'].indexOf(transfer) === -1) ||
                    (!transfer && Number(stream.BitDepth) > 8) ||
                    ((stream.ColorPrimaries || '').toLowerCase() === 'bt2020')) {
                    RealtimeUpscaler._mode = 'off';
                    RealtimeUpscaler._reason = !stream ? 'Video color metadata unavailable; realtime processing cannot be validated' :
                        'HDR realtime and masking are not supported. Use the PQ RGB16 batch pipeline; HLG is not supported.';
                    PlayerIntegration.showPlayerNotification(RealtimeUpscaler._reason, 'warning');
                    return;
                }
                var mode = (config.RealtimeMode || 'auto').toLowerCase();

                // Guards apply before model warmup, recommendation or benchmark work.
                if (config.ClientDriverUpscalingActive === true || config.EnableObjectMasking === true ||
                    ['auto', 'server'].indexOf(mode) === -1) {
                    RealtimeUpscaler.start(video, config, null);
                    return;
                }

                // Auto + Server modes require an active server-side model before the
                // benchmark (otherwise /benchmark-frame returns 400 "No model loaded",
                // the auto-tier picks WebGL, and server-mode never engages).
                var startup = new AbortController();
                RealtimeUpscaler._startupController = startup;
                function current() { return generation === RealtimeUpscaler._generation && !startup.signal.aborted; }
                var captureW = config.RealtimeCaptureWidth || 480;
                var captureH = Math.round(captureW * (video.videoHeight / video.videoWidth));
                var modelName = config.Model || 'fsrcnn-x2';
                var authHeaders = { 'Authorization': 'MediaBrowser Token="' + ApiClient.accessToken() + '"' };

                var runBenchmarkAndStart = function() {
                    if (!current()) return;
                    fetch(ApiClient.getUrl('Upscaler/benchmark-frame') + '?width=' + captureW + '&height=' + captureH, {
                        headers: authHeaders, signal: startup.signal
                    }).then(function(r) { return r.ok ? r.json() : Promise.reject(new Error('HTTP ' + r.status)); })
                        .then(function(bench) {
                            if (!current()) return;
                            bench.videoFps = stream && (stream.AverageFrameRate || stream.RealFrameRate);
                            console.log('AI Upscaler RT: Benchmark result', bench);
                            RealtimeUpscaler.start(video, config, bench);
                        })
                        .catch(function(err) {
                            if (!current()) return;
                            console.warn('AI Upscaler RT: Benchmark failed, using WebGL', err);
                            RealtimeUpscaler.start(video, config, { error: 'benchmark failed' });
                        });
                };

                fetch(ApiClient.getUrl('Upscaler/models/load') + '?model_name=' + encodeURIComponent(modelName), {
                    method: 'POST',
                    headers: authHeaders, signal: startup.signal
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
            // Redesign: a smoked-glass panel over the picture. Jellyfin's own accent
            // (#00a4dc, the player's progress bar) marks "on" and "selected"; green, amber
            // and red are reserved for the live frame-rate readout. Tokens live on .ai-menu
            // so nothing leaks into jellyfin-web. No web fonts: the panel uses the font
            // jellyfin-web already ships (Noto Sans), which also works on offline servers.
            styles.textContent = [
                /* Player toolbar button */
                '#aiUpscalerButton{display:inline-flex!important;align-items:center;justify-content:center;color:#e6e8ec;cursor:pointer;transition:color .15s}',
                '#aiUpscalerButton:hover{color:#00a4dc}',
                '#aiUpscalerButton .material-icons{font-size:24px}',

                /* Panel shell */
                '.ai-menu{--ai-glass:rgba(12,14,19,.8);--ai-solid:#0f1217;--ai-raise:rgba(255,255,255,.055);--ai-raise-2:rgba(255,255,255,.09);--ai-line:rgba(255,255,255,.09);--ai-line-2:rgba(255,255,255,.18);--ai-text:#f1f3f6;--ai-dim:#aab2bf;--ai-faint:#7d8594;--ai-accent:#00a4dc;--ai-accent-ink:#7fd3f2;--ai-accent-bg:rgba(0,164,220,.16);--ai-good:#3ddc97;--ai-warn:#f5b94a;--ai-bad:#ff6b6b;--ai-mono:ui-monospace,"SF Mono","Cascadia Mono","Roboto Mono",Menlo,Consolas,monospace;' +
                    'position:fixed;bottom:96px;z-index:100000;width:400px;max-width:calc(100vw - 32px);max-height:calc(100vh - 128px);display:flex;flex-direction:column;background:var(--ai-solid);color:var(--ai-text);border:1px solid var(--ai-line);border-radius:16px;box-shadow:0 24px 60px rgba(0,0,0,.55),0 2px 8px rgba(0,0,0,.35);overflow:hidden;font-family:"Noto Sans",-apple-system,"Segoe UI",system-ui,sans-serif;font-size:13px;line-height:1.45;text-align:left;animation:aiMenuIn .2s cubic-bezier(.2,.8,.2,1)}',
                '@supports ((-webkit-backdrop-filter:blur(1px)) or (backdrop-filter:blur(1px))){.ai-menu{background:var(--ai-glass);-webkit-backdrop-filter:blur(22px) saturate(1.35);backdrop-filter:blur(22px) saturate(1.35)}}',
                '.ai-menu--right{right:24px}.ai-menu--left{left:24px}.ai-menu--center{left:50%;transform:translateX(-50%);animation-name:aiMenuInCenter}',
                '@keyframes aiMenuIn{from{opacity:0;transform:translateY(8px) scale(.985)}to{opacity:1;transform:none}}',
                '@keyframes aiMenuInCenter{from{opacity:0;transform:translateX(-50%) translateY(8px)}to{opacity:1;transform:translateX(-50%)}}',
                '.ai-menu *{box-sizing:border-box}',
                '.ai-menu .material-icons{font-size:18px;line-height:1}',
                '.ai-menu__switch,.ai-menu__close,.ai-menu__tab,.ai-menu__chip,.ai-menu__model,.ai-menu__scale,.ai-menu__preset,.ai-menu__adv-toggle,.ai-menu__filter-btn,.ai-menu__action{font-family:inherit;-webkit-appearance:none;appearance:none;margin:0}',
                /* Focus: visible for keyboards and TV remotes; hidden for mouse clicks where supported */
                '.ai-menu button:focus,.ai-menu input:focus,.ai-menu summary:focus{outline:2px solid var(--ai-accent);outline-offset:2px}',
                '.ai-menu button:focus:not(:focus-visible),.ai-menu input:focus:not(:focus-visible),.ai-menu summary:focus:not(:focus-visible){outline:none}',

                /* Header */
                '.ai-menu__grip{display:none}',
                '.ai-menu__header{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:14px 12px 12px 16px;flex-shrink:0}',
                '.ai-menu__brand{display:flex;align-items:center;gap:11px;min-width:0}',
                '.ai-menu .ai-menu__logo{width:32px;height:32px;border-radius:10px;display:flex;align-items:center;justify-content:center;flex-shrink:0;background:var(--ai-accent-bg);color:var(--ai-accent-ink);font-size:18px}',
                '.ai-menu__title{font-size:15px;font-weight:700;letter-spacing:.005em;line-height:1.15}',
                '.ai-menu__version{margin-top:3px;font:500 11px/1 var(--ai-mono);color:var(--ai-faint)}',
                '.ai-menu__header-right{display:flex;align-items:center;gap:6px}',
                '.ai-menu__close{width:34px;height:34px;padding:0;border:0;border-radius:10px;background:transparent;color:var(--ai-dim);cursor:pointer;display:flex;align-items:center;justify-content:center;transition:background .15s,color .15s}',
                '.ai-menu__close:hover{background:var(--ai-raise-2);color:var(--ai-text)}',
                '.ai-menu .ai-menu__close .material-icons{font-size:20px}',

                /* Switches */
                '.ai-menu__switch{position:relative;width:44px;height:26px;padding:0;flex-shrink:0;border-radius:13px;border:1px solid var(--ai-line-2);background:rgba(255,255,255,.08);cursor:pointer;transition:background .18s,border-color .18s}',
                '.ai-menu__switch::after{content:"";position:absolute;top:3px;left:3px;width:18px;height:18px;border-radius:50%;background:#c9cfd8;box-shadow:0 1px 3px rgba(0,0,0,.45);transition:transform .18s cubic-bezier(.2,.8,.2,1),background .18s}',
                '.ai-menu__switch--on{background:var(--ai-accent);border-color:var(--ai-accent)}',
                '.ai-menu__switch--on::after{transform:translateX(18px);background:#fff}',
                '.ai-menu__switch--sm{width:38px;height:22px;border-radius:11px}',
                '.ai-menu__switch--sm::after{width:14px;height:14px}',
                '.ai-menu__switch--sm.ai-menu__switch--on::after{transform:translateX(16px)}',

                /* Live readout: frame rate, its trend against the video rate, state, engine, model */
                '.ai-menu__status{margin:0 12px;padding:12px 14px 11px;border-radius:12px;background:var(--ai-raise);border:1px solid var(--ai-line);flex-shrink:0}',
                '.ai-menu__readout{display:flex;align-items:center;gap:12px}',
                '.ai-menu__status-fps{display:flex;align-items:baseline;gap:5px;min-width:70px;font:500 12px/1 var(--ai-mono);color:var(--ai-faint);white-space:nowrap}',
                '.ai-menu__status-fps b{font:600 28px/1 var(--ai-mono);letter-spacing:-.03em;color:var(--ai-text);font-variant-numeric:tabular-nums}',
                '.ai-menu__status-fps--warn b{color:var(--ai-warn)}',
                '.ai-menu__status-fps--err b{color:var(--ai-bad)}',
                '.ai-menu__spark{flex:1;min-width:0;height:36px;overflow:visible}',
                '.ai-menu__spark-line{fill:none;stroke:var(--ai-good);stroke-width:1.75;stroke-linejoin:round;stroke-linecap:round;vector-effect:non-scaling-stroke}',
                '.ai-menu__spark--warn .ai-menu__spark-line{stroke:var(--ai-warn)}',
                '.ai-menu__spark--err .ai-menu__spark-line{stroke:var(--ai-bad)}',
                '.ai-menu__spark-target{stroke:var(--ai-line-2);stroke-width:1;stroke-dasharray:3 3;vector-effect:non-scaling-stroke}',
                '.ai-menu__pill{display:inline-flex;align-items:center;gap:6px;flex-shrink:0;padding:5px 9px;border-radius:999px;background:rgba(255,255,255,.07)}',
                '.ai-menu__status-dot{width:7px;height:7px;border-radius:50%;flex-shrink:0;background:var(--ai-faint)}',
                '.ai-menu__status-dot--on{background:var(--ai-good);box-shadow:0 0 0 3px rgba(61,220,151,.18);animation:aiPulse 2.4s ease-in-out infinite}',
                '.ai-menu__status-dot--off{background:var(--ai-faint)}',
                '.ai-menu__status-dot--warn{background:var(--ai-warn)}',
                '.ai-menu__status-dot--err{background:var(--ai-bad)}',
                '@keyframes aiPulse{0%,100%{opacity:1}50%{opacity:.55}}',
                '.ai-menu__status-state{font:700 10.5px/1 var(--ai-mono);letter-spacing:.08em;color:var(--ai-text)}',
                '.ai-menu__status-meta{display:flex;align-items:center;gap:7px;min-width:0;margin-top:10px;font-size:12px;color:var(--ai-dim)}',
                '.ai-menu__status-mode{flex-shrink:0;font-weight:700;color:var(--ai-accent-ink);white-space:nowrap}',
                '.ai-menu__status-sep{color:var(--ai-faint)}',
                '.ai-menu__status-model{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font:500 12px/1.3 var(--ai-mono)}',

                /* Tabs: one segmented control */
                '.ai-menu__tabs{display:flex;gap:4px;margin:12px 12px 0;padding:4px;flex-shrink:0;border-radius:12px;background:rgba(0,0,0,.28);border:1px solid var(--ai-line)}',
                '.ai-menu__tab{position:relative;flex:1;min-width:0;min-height:38px;display:flex;align-items:center;justify-content:center;gap:6px;padding:6px 4px;border:0;border-radius:9px;background:transparent;color:var(--ai-dim);font-size:12.5px;font-weight:600;white-space:nowrap;cursor:pointer;transition:background .15s,color .15s}',
                '.ai-menu .ai-menu__tab .material-icons{font-size:17px}',
                '.ai-menu__tab:hover{color:var(--ai-text);background:rgba(255,255,255,.05)}',
                '.ai-menu__tab--active,.ai-menu__tab--active:hover{background:var(--ai-raise-2);color:var(--ai-text);box-shadow:inset 0 0 0 1px var(--ai-line)}',
                '.ai-menu .ai-menu__tab--active .material-icons{color:var(--ai-accent-ink)}',
                '.ai-menu__tab-live{position:absolute;top:6px;right:7px;width:6px;height:6px;border-radius:50%;background:transparent}',
                '.ai-menu__tab-live--on{background:var(--ai-good)}',

                /* Body and panes */
                '.ai-menu__body{flex:1;min-height:0;overflow-y:auto;overscroll-behavior:contain;padding:14px 12px 14px;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.2) transparent}',
                '.ai-menu__body::-webkit-scrollbar{width:10px}',
                '.ai-menu__body::-webkit-scrollbar-thumb{background:rgba(255,255,255,.16);border:3px solid transparent;border-radius:5px;background-clip:padding-box}',
                '.ai-menu__body::-webkit-scrollbar-track{background:transparent}',
                '.ai-menu__pane{display:none}',
                '.ai-menu__pane--active{display:block;animation:aiFade .16s ease-out}',
                '@keyframes aiFade{from{opacity:0}to{opacity:1}}',
                '.ai-menu__section{margin-top:18px}',
                '.ai-menu__section:first-child{margin-top:0}',
                '.ai-menu__section-title{display:flex;align-items:baseline;justify-content:space-between;gap:10px;padding:0 2px 9px;font-size:11px;font-weight:700;letter-spacing:.09em;text-transform:uppercase;color:var(--ai-faint)}',
                '.ai-menu__section-sub{font-size:11px;font-weight:500;letter-spacing:0;text-transform:none;color:var(--ai-faint);text-align:right}',

                /* Models: filter chips, groups, rows */
                '.ai-menu__chips{display:flex;flex-wrap:wrap;gap:6px;margin-bottom:14px}',
                '.ai-menu__chip{min-height:32px;padding:5px 13px;border-radius:999px;border:1px solid var(--ai-line);background:transparent;color:var(--ai-dim);font-size:12.5px;font-weight:600;cursor:pointer;transition:background .15s,color .15s,border-color .15s}',
                '.ai-menu__chip:hover{color:var(--ai-text);border-color:var(--ai-line-2)}',
                '.ai-menu__chip--active,.ai-menu__chip--active:hover{background:var(--ai-accent-bg);border-color:rgba(0,164,220,.55);color:var(--ai-accent-ink)}',
                '.ai-menu__cat{margin-bottom:16px}',
                '.ai-menu__cat:last-child{margin-bottom:0}',
                '.ai-menu__cat-head{display:flex;align-items:baseline;gap:8px;padding:0 4px 7px}',
                '.ai-menu__cat-name{font-size:12.5px;font-weight:700;color:var(--ai-text)}',
                '.ai-menu__cat-desc{font-size:11.5px;color:var(--ai-faint)}',
                '.ai-menu__model{display:flex;align-items:center;gap:10px;width:100%;min-height:44px;margin:0 0 4px;padding:8px 10px 8px 12px;border-radius:10px;border:1px solid transparent;background:var(--ai-raise);color:var(--ai-text);font-size:13px;text-align:left;cursor:pointer;transition:background .12s,border-color .12s}',
                '.ai-menu__model:hover{background:var(--ai-raise-2)}',
                '.ai-menu__model--active,.ai-menu__model--active:hover{background:var(--ai-accent-bg);border-color:rgba(0,164,220,.5)}',
                '.ai-menu__model--loading{opacity:.65;pointer-events:none}',
                '.ai-menu__radio{width:16px;height:16px;flex-shrink:0;border-radius:50%;border:2px solid var(--ai-line-2);transition:border-color .15s}',
                '.ai-menu__model--active .ai-menu__radio{border-color:var(--ai-accent);background:radial-gradient(circle,var(--ai-accent) 0,var(--ai-accent) 3.5px,transparent 4.5px)}',
                '.ai-menu__model-name{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:500}',
                '.ai-menu__badge{flex-shrink:0;padding:3px 7px;border-radius:6px;background:rgba(255,255,255,.07);color:var(--ai-dim);font-size:10px;font-weight:700;letter-spacing:.05em;text-transform:uppercase}',
                '.ai-menu__model--active .ai-menu__badge{background:rgba(0,164,220,.22);color:var(--ai-accent-ink)}',
                '.ai-menu__model-scale{flex-shrink:0;min-width:26px;text-align:right;font:600 12px/1 var(--ai-mono);color:var(--ai-dim)}',
                '.ai-menu__state{width:20px;height:20px;flex-shrink:0;display:flex;align-items:center;justify-content:center;color:var(--ai-faint)}',
                '.ai-menu .ai-menu__state .material-icons{font-size:18px}',
                '.ai-menu__state--ready{color:var(--ai-good)}',
                '.ai-menu__state--need-dl{color:var(--ai-faint)}',
                '.ai-menu__state--busy{color:var(--ai-accent-ink)}',
                '.ai-menu__state--err{color:var(--ai-bad)}',
                '.ai-menu__spinner{width:14px;height:14px;border-radius:50%;border:2px solid rgba(255,255,255,.14);border-top-color:var(--ai-accent);animation:aiSpin .7s linear infinite}',
                '@keyframes aiSpin{to{transform:rotate(360deg)}}',
                '.ai-menu__scales{display:flex;gap:4px;padding:4px;border-radius:12px;background:rgba(0,0,0,.28);border:1px solid var(--ai-line)}',
                '.ai-menu__scale{flex:1;min-height:38px;border:0;border-radius:9px;background:transparent;color:var(--ai-dim);font:600 13px/1 var(--ai-mono);cursor:pointer;transition:background .15s,color .15s}',
                '.ai-menu__scale:hover{color:var(--ai-text)}',
                '.ai-menu__scale--active,.ai-menu__scale--active:hover{background:var(--ai-raise-2);color:var(--ai-text);box-shadow:inset 0 0 0 1px var(--ai-line)}',

                /* Filters: preset tiles with a live preview of the current frame */
                '.ai-menu__presets{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}',
                '.ai-menu__preset{display:flex;flex-direction:column;gap:6px;padding:4px 4px 7px;min-width:0;border-radius:11px;border:1px solid transparent;background:var(--ai-raise);color:var(--ai-dim);font-size:11.5px;font-weight:600;text-align:center;cursor:pointer;transition:background .12s,border-color .12s,color .12s}',
                '.ai-menu__preset:hover{background:var(--ai-raise-2);color:var(--ai-text)}',
                '.ai-menu__preset--active,.ai-menu__preset--active:hover{border-color:var(--ai-accent);background:var(--ai-accent-bg);color:var(--ai-text)}',
                '.ai-menu__preset-preview{display:block;width:100%;height:auto;border-radius:8px;background:#1b2330}',
                '.ai-menu__preset-name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
                '.ai-menu__sliders{display:flex;flex-direction:column;gap:14px}',
                '.ai-menu__slider-row{display:flex;flex-direction:column;gap:6px}',
                '.ai-menu__slider-label{display:flex;align-items:center;gap:8px;font-size:12.5px;font-weight:600;color:var(--ai-text)}',
                '.ai-menu .ai-menu__slider-icon{font-size:17px;color:var(--ai-faint)}',
                '.ai-menu__slider-val{margin-left:auto;min-width:40px;text-align:right;font:600 12px/1 var(--ai-mono);color:var(--ai-accent-ink);font-variant-numeric:tabular-nums}',
                '.ai-menu__slider,.ai-menu__adv-slider{-webkit-appearance:none;appearance:none;width:100%;height:4px;margin:7px 0;border-radius:2px;cursor:pointer;background:linear-gradient(to right,rgba(255,255,255,.15) var(--ai-from,0%),var(--ai-accent) var(--ai-from,0%),var(--ai-accent) var(--ai-to,0%),rgba(255,255,255,.15) var(--ai-to,0%))}',
                '.ai-menu__slider::-webkit-slider-thumb,.ai-menu__adv-slider::-webkit-slider-thumb{-webkit-appearance:none;appearance:none;width:18px;height:18px;border-radius:50%;border:0;background:#fff;box-shadow:0 1px 4px rgba(0,0,0,.5),0 0 0 4px rgba(0,164,220,.25);cursor:pointer}',
                '.ai-menu__slider::-moz-range-thumb,.ai-menu__adv-slider::-moz-range-thumb{width:18px;height:18px;border-radius:50%;border:0;background:#fff;box-shadow:0 1px 4px rgba(0,0,0,.5),0 0 0 4px rgba(0,164,220,.25);cursor:pointer}',
                '.ai-menu__adv-toggle{display:flex;align-items:center;justify-content:space-between;gap:10px;width:100%;min-height:42px;padding:8px 10px 8px 12px;border-radius:10px;border:1px solid var(--ai-line);background:transparent;color:var(--ai-text);font-size:12.5px;font-weight:600;text-align:left;cursor:pointer;transition:border-color .15s,background .15s}',
                '.ai-menu__adv-toggle:hover{border-color:var(--ai-line-2);background:var(--ai-raise)}',
                '.ai-menu__adv-note{margin-left:6px;font-weight:500;color:var(--ai-faint)}',
                '.ai-menu .ai-menu__adv-caret{font-size:20px;color:var(--ai-faint)}',
                '.ai-menu__adv{display:flex;flex-direction:column;gap:8px;max-height:0;overflow:hidden;transition:max-height .28s ease}',
                '.ai-menu__adv--open{max-height:640px;padding-top:10px}',
                '.ai-menu__adv-row{display:flex;flex-direction:column;gap:4px;padding:9px 12px;border-radius:10px;background:var(--ai-raise)}',
                '.ai-menu__adv-label{display:flex;justify-content:space-between;gap:10px;font-size:12px;font-weight:600;color:var(--ai-dim)}',
                '.ai-menu__adv-val{font:600 12px/1 var(--ai-mono);color:var(--ai-accent-ink);font-variant-numeric:tabular-nums}',

                /* Buttons */
                '.ai-menu__filter-actions{display:flex;gap:8px;margin-top:18px}',
                '.ai-menu__filter-btn{flex:1;min-height:40px;padding:9px 14px;border-radius:10px;font-size:13px;font-weight:700;cursor:pointer;transition:background .15s,border-color .15s,color .15s}',
                '.ai-menu__filter-btn:disabled{opacity:.4;cursor:not-allowed}',
                '.ai-menu__filter-btn--secondary{background:transparent;border:1px solid var(--ai-line-2);color:var(--ai-text)}',
                '.ai-menu__filter-btn--secondary:hover:not(:disabled){background:var(--ai-raise)}',
                /* Dark ink on the accent: white on #00a4dc is under 3:1 */
                '.ai-menu__filter-btn--primary{background:var(--ai-accent);border:1px solid var(--ai-accent);color:#03141d}',
                '.ai-menu__filter-btn--primary:hover:not(:disabled){background:#1cb4e8;border-color:#1cb4e8}',

                /* Auto pane */
                '.ai-menu__auto-loading{padding:26px 4px;color:var(--ai-faint);font-size:12.5px;text-align:center}',
                '.ai-menu__auto-master{display:flex;align-items:center;gap:14px;padding:14px;border-radius:12px;background:var(--ai-raise);border:1px solid var(--ai-line)}',
                '.ai-menu__auto-master--on{background:linear-gradient(135deg,rgba(0,164,220,.17),rgba(0,164,220,.05));border-color:rgba(0,164,220,.45)}',
                '.ai-menu__auto-master-text{display:flex;flex-direction:column;gap:5px;min-width:0;flex:1}',
                '.ai-menu__auto-master-title{display:flex;align-items:center;gap:8px;font-size:14.5px;font-weight:700}',
                '.ai-menu__auto-state{padding:4px 6px;border-radius:5px;background:rgba(255,255,255,.08);color:var(--ai-dim);font:700 10px/1 var(--ai-mono);letter-spacing:.1em}',
                '.ai-menu__auto-master--on .ai-menu__auto-state{background:rgba(0,164,220,.22);color:var(--ai-accent-ink)}',
                '.ai-menu__auto-master-sub{font-size:12px;line-height:1.5;color:var(--ai-dim)}',
                '.ai-menu__auto-card{margin-top:10px;padding:14px;border-radius:12px;background:var(--ai-raise);border:1px solid var(--ai-line)}',
                '.ai-menu__auto-card--muted{color:var(--ai-dim);font-size:12.5px;line-height:1.55}',
                '.ai-menu__auto-card-head{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-bottom:7px}',
                '.ai-menu__auto-card-title{font-size:11px;font-weight:700;letter-spacing:.09em;text-transform:uppercase;color:var(--ai-faint)}',
                '.ai-menu__auto-scale{padding:4px 7px;border-radius:6px;background:rgba(0,164,220,.18);color:var(--ai-accent-ink);font:700 12px/1 var(--ai-mono)}',
                '.ai-menu__auto-model{font:600 18px/1.25 var(--ai-mono);letter-spacing:-.01em;word-break:break-all}',
                '.ai-menu__auto-size{margin-top:5px;font:500 12px/1.3 var(--ai-mono);color:var(--ai-dim)}',
                '.ai-menu__auto-reason{margin-top:9px;font-size:12.5px;line-height:1.55;color:var(--ai-dim)}',
                '.ai-menu__auto-warn{margin-top:9px;padding:8px 10px;border-radius:8px;background:rgba(245,185,74,.1);color:var(--ai-warn);font-size:12px;line-height:1.5}',
                '.ai-menu__auto-signals{margin-top:9px}',
                '.ai-menu__auto-signals summary{cursor:pointer;font-size:12px;color:var(--ai-faint)}',
                '.ai-menu__auto-signals ul{margin:6px 0 0;padding-left:18px;font-size:12px;line-height:1.6;color:var(--ai-dim)}',
                '.ai-menu__auto-row{display:flex;align-items:center;gap:12px;padding:11px 4px;border-bottom:1px solid var(--ai-line)}',
                '.ai-menu__auto-row:last-of-type{border-bottom:0}',
                '.ai-menu__auto-row--off{opacity:.45}',
                '.ai-menu__auto-row-text{display:flex;flex-direction:column;gap:3px;min-width:0;flex:1}',
                '.ai-menu__auto-row-label{font-size:13px;font-weight:600}',
                '.ai-menu__auto-row-sub{font-size:11.5px;line-height:1.45;color:var(--ai-faint)}',
                '.ai-menu__auto-suggest{display:flex;align-items:center;gap:12px;margin-top:10px;padding:12px 12px 12px 14px;border-radius:12px;background:rgba(61,220,151,.07);border:1px solid rgba(61,220,151,.3)}',
                '.ai-menu__auto-suggest-text{display:flex;flex-direction:column;gap:3px;min-width:0;flex:1}',
                '.ai-menu__auto-suggest-label{font-size:11px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:var(--ai-faint)}',
                '.ai-menu__auto-suggest-value{font-size:13.5px;font-weight:600}',
                '.ai-menu__auto-suggest-cur{font-size:12px;font-weight:500;color:var(--ai-faint)}',
                '.ai-menu__auto-suggest .ai-menu__filter-btn{flex:0 0 auto}',
                '.ai-menu__auto-actions{display:flex;gap:8px;margin-top:16px}',

                /* Realtime pane */
                '.ai-menu__rt-card{padding:14px;border-radius:12px;background:var(--ai-raise);border:1px solid var(--ai-line)}',
                '.ai-menu__rt-status{display:flex;align-items:center;gap:10px;min-width:0}',
                '.ai-menu__rt-indicator{width:9px;height:9px;flex-shrink:0;border-radius:50%;background:var(--ai-faint)}',
                '.ai-menu__rt-indicator--on{background:var(--ai-good);box-shadow:0 0 0 4px rgba(61,220,151,.16);animation:aiPulse 2.4s ease-in-out infinite}',
                '.ai-menu__rt-label{font-size:11px;font-weight:700;letter-spacing:.09em;text-transform:uppercase;color:var(--ai-faint)}',
                '.ai-menu__rt-value{margin-left:auto;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font:600 13.5px/1.2 var(--ai-mono);color:var(--ai-dim)}',
                '.ai-menu__rt-value--on{color:var(--ai-good)}',
                '.ai-menu__rt-row{display:flex;gap:8px;margin-top:14px}',
                '.ai-menu__engines{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:4px}',
                '.ai-menu__engine{display:flex;flex-direction:column;gap:2px;padding:10px 12px;border-radius:10px;border:1px solid transparent;background:var(--ai-raise)}',
                '.ai-menu__engine--on{border-color:rgba(61,220,151,.4);background:rgba(61,220,151,.07)}',
                '.ai-menu__engine-name{font-size:13px;font-weight:600}',
                '.ai-menu__engine--on .ai-menu__engine-name::after{content:"running";margin-left:8px;padding:2px 6px;border-radius:5px;background:rgba(61,220,151,.18);color:var(--ai-good);font:700 9.5px/1 var(--ai-mono);letter-spacing:.08em;text-transform:uppercase;vertical-align:2px}',
                '.ai-menu__engine-desc{font-size:11.5px;line-height:1.45;color:var(--ai-faint)}',

                /* Footer */
                '.ai-menu__footer{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:10px 10px 10px 16px;flex-shrink:0;border-top:1px solid var(--ai-line)}',
                '.ai-menu__summary{display:flex;align-items:center;gap:8px;min-width:0;font-size:12px;color:var(--ai-dim)}',
                '.ai-menu__summary-dot{width:7px;height:7px;flex-shrink:0;border-radius:50%;background:var(--ai-good)}',
                '.ai-menu__summary-dot--off{background:var(--ai-faint)}',
                '.ai-menu__summary-strong{font-family:var(--ai-mono);font-weight:600;color:var(--ai-text)}',
                '.ai-menu__action{display:inline-flex;align-items:center;gap:2px;min-height:34px;padding:6px 6px 6px 12px;border:0;border-radius:9px;background:transparent;color:var(--ai-accent-ink);font-size:12.5px;font-weight:700;white-space:nowrap;cursor:pointer;transition:background .15s}',
                '.ai-menu__action:hover{background:var(--ai-accent-bg)}',

                /* Phones: a bottom sheet across the full width */
                '@media (max-width:600px){' +
                    '.ai-menu,.ai-menu--left,.ai-menu--right,.ai-menu--center{left:0;right:0;bottom:0;width:auto;max-width:none;max-height:80vh;border-radius:18px 18px 0 0;border-bottom:0;transform:none;animation-name:aiSheetIn;padding-bottom:env(safe-area-inset-bottom,0px)}' +
                    '.ai-menu__grip{display:block;width:38px;height:4px;margin:8px auto 0;border-radius:2px;background:rgba(255,255,255,.25)}' +
                    '.ai-menu__header{padding-top:8px}' +
                    '.ai-menu__tab{gap:4px;font-size:12px}' +
                '}',
                '@keyframes aiSheetIn{from{opacity:0;transform:translateY(24px)}to{opacity:1;transform:none}}',
                /* Short windows: less offset from the bottom, more room for content */
                '@media (max-height:620px) and (min-width:601px){.ai-menu{bottom:72px;max-height:calc(100vh - 88px)}}',
                /* TVs and large monitors, usually read from a distance. zoom scales the
                   panel's own lengths too, so its offset and height cap are divided back. */
                '@media (min-width:1800px){.ai-menu{zoom:1.15;bottom:84px;max-height:calc((100vh - 150px) / 1.15)}}',
                '@media (prefers-reduced-motion:reduce){.ai-menu,.ai-menu *,.ai-notif{animation:none!important;transition:none!important}}',

                /* Notifications */
                '.ai-notif-host{position:fixed;top:20px;right:20px;z-index:100001;display:flex;flex-direction:column;align-items:flex-end;gap:8px;pointer-events:none;max-height:80vh}',
                '.ai-notif{position:static;display:flex;align-items:center;gap:10px;max-width:360px;padding:10px 14px;border-radius:12px;background:rgba(15,18,23,.94);border:1px solid rgba(255,255,255,.1);box-shadow:0 12px 32px rgba(0,0,0,.45);color:#f1f3f6;font:500 13px/1.4 "Noto Sans",-apple-system,"Segoe UI",system-ui,sans-serif;pointer-events:none;animation:aiNotifIn .22s ease-out}',
                '.ai-notif::before{content:"";width:8px;height:8px;flex-shrink:0;border-radius:50%;background:#00a4dc}',
                '.ai-notif--success::before{background:#3ddc97}',
                '.ai-notif--warning::before{background:#f5b94a}',
                '.ai-notif--error::before{background:#ff6b6b}',
                '@keyframes aiNotifIn{from{opacity:0;transform:translateY(-6px)}to{opacity:1;transform:none}}'
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
