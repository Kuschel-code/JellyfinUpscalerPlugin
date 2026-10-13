// v1.8.3.47 - WebGPU + ONNX Runtime Web realtime AI upscaler.
//
// Goal: real Real-ESRGAN compact inference in the browser, GPU-accelerated via WebGPU.
// Optimized for AMD RDNA2 / WebGPU: vectorized tensor conversions, resolution safety clamping,
// and granular status lifecycle reporting.
//
// Defensive fallback chain (any layer can fail; we never break playback):
//   WebGPU not available             -> caller falls back to Lanczos
//   onnxruntime-web load fails       -> caller falls back to Lanczos
//   Model fetch fails                -> caller falls back to Lanczos
//   Inference throws                 -> log + skip frame, do not crash render loop
//
(function () {
    'use strict';

    // Pinned ONNX Runtime Web version.
    var ORT_CDN_URL = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.20.1/dist/ort.webgpu.min.js';

    function localModelUrl(name) {
        try {
            if (window.ApiClient && typeof ApiClient.getUrl === 'function') {
                return ApiClient.getUrl('Upscaler/models/file/' + encodeURIComponent(name));
            }
        } catch (e) {}
        return '';
    }

    // Real-ESRGAN compact & NomosUni. Models natively scale 2x / 4x.
    var MODEL_CATALOG = {
        'realesrgan-compact-x2': {
            scale: 2,
            name: 'NomosUni / Real-ESRGAN Compact Anime',
            urls: [
                function() { return localModelUrl('nomosuni-compact-x2'); },
                function() { return localModelUrl('span-x2'); },
                'https://huggingface.co/nimjinwei/designerplatform-models/resolve/main/realesr-animevideov3.onnx',
                'https://huggingface.co/xiaojiaenen/lingtuan-sr-models/resolve/main/realesr-animevideov3-x4.onnx'
            ]
        },
        'nomosuni-compact-x2': {
            scale: 2,
            name: 'NomosUni Compact x2',
            urls: [
                function() { return localModelUrl('nomosuni-compact-x2'); },
                function() { return localModelUrl('span-x2'); }
            ]
        },
        'realesrgan-compact-x4': {
            scale: 4,
            name: 'Real-ESRGAN General x4',
            urls: [
                function() { return localModelUrl('nomosuni-compact-x2'); },
                'https://huggingface.co/xiaojiaenen/lingtuan-sr-models/resolve/main/realesr-animevideov3-x4.onnx',
                'https://huggingface.co/CoderViking/realesr-general-x4v3-onnx/resolve/main/realesr-general-x4v3.onnx'
            ]
        }
    };

    var WebGPUAIUpscaler = {
        _video: null,
        _canvas: null,
        _ctx: null,
        _session: null,
        _modelKey: null,
        _scale: 4,
        _running: false,
        _processing: false,
        _fpsCallback: null,
        _statusCallback: null,
        _status: 'idle',
        _frameCount: 0,
        _lastFpsTime: 0,
        _onFatal: null,
        _srcCanvas: null,
        _srcCtx: null,
        _tensorData: null,
        _tensorW: 0,
        _tensorH: 0,
        _outImg: null,
        _outPixels32: null,
        _outW: 0,
        _outH: 0,

        _setStatus: function (s) {
            this._status = s;
            if (this._statusCallback) {
                try { this._statusCallback(s); } catch (e) {}
            }
        },

        start: async function (video, opts) {
            opts = opts || {};
            this._video = video;
            this._modelKey = opts.modelKey || 'realesrgan-compact-x2';
            this._fpsCallback = opts.fpsCallback || null;
            this._statusCallback = opts.statusCallback || null;
            this._onFatal = opts.onFatal || function () {};

            this._setStatus('Checking WebGPU...');
            if (!('gpu' in navigator)) {
                console.warn('AI Upscaler RT/WebGPU: navigator.gpu missing - browser does not support WebGPU.');
                return false;
            }
            try {
                var adapter = await navigator.gpu.requestAdapter();
                if (!adapter) {
                    console.warn('AI Upscaler RT/WebGPU: requestAdapter() returned null.');
                    return false;
                }
            } catch (e) {
                console.warn('AI Upscaler RT/WebGPU: WebGPU adapter init threw', e);
                return false;
            }

            this._setStatus('Loading ONNX Runtime...');
            var ortLoaded = await this._loadOrt();
            if (!ortLoaded) return false;

            var modelEntry = MODEL_CATALOG[this._modelKey];
            if (!modelEntry) {
                console.warn('AI Upscaler RT/WebGPU: unknown model key', this._modelKey);
                return false;
            }
            this._scale = modelEntry.scale || 4;

            this._setStatus('Downloading Neural Model (2.5MB)...');
            var session = await this._loadModel(modelEntry.urls);
            if (!session) return false;
            this._session = session;

            this._setStatus('Compiling WebGPU Kernels...');
            this._canvas = document.createElement('canvas');
            this._canvas.style.cssText = 'position:absolute;top:0;left:0;width:100%;height:100%;object-fit:contain;pointer-events:none;z-index:999;';
            this._canvas.id = 'aiWebgpuCanvas';
            this._ctx = this._canvas.getContext('2d');
            var parent = video.parentElement;
            if (parent) {
                parent.style.position = 'relative';
                parent.appendChild(this._canvas);
            }

            this._running = true;
            this._processing = false;
            this._frameCount = 0;
            this._lastFpsTime = performance.now();
            this._setStatus('running');
            console.log('AI Upscaler RT/WebGPU: started with', this._modelKey, 'scale=', this._scale);
            this._renderLoop();
            return true;
        },

        stop: function () {
            this._running = false;
            this._processing = false;
            this._setStatus('idle');
            try { if (this._session && typeof this._session.release === 'function') this._session.release(); } catch (e) {}
            this._session = null;
            if (this._canvas && this._canvas.parentElement) {
                this._canvas.parentElement.removeChild(this._canvas);
            }
            this._canvas = null;
            this._ctx = null;
            this._srcCanvas = null;
            this._srcCtx = null;
            this._tensorData = null;
            this._outImg = null;
            this._outPixels32 = null;
            if (this._video && this._video.style.opacity === '0') {
                this._video.style.opacity = '';
            }
        },

        _loadOrt: async function () {
            if (window.ort) return true;
            return new Promise(function (resolve) {
                var script = document.createElement('script');
                script.src = ORT_CDN_URL;
                script.crossOrigin = 'anonymous';
                script.onload = function () {
                    if (window.ort) {
                        try {
                            window.ort.env.wasm.numThreads = 1;
                            window.ort.env.logLevel = 'warning';
                        } catch (e) {}
                        resolve(true);
                    } else {
                        console.warn('AI Upscaler RT/WebGPU: ORT loaded but window.ort missing');
                        resolve(false);
                    }
                };
                script.onerror = function () {
                    console.warn('AI Upscaler RT/WebGPU: failed to load onnxruntime-web from CDN', ORT_CDN_URL);
                    resolve(false);
                };
                document.head.appendChild(script);
            });
        },

        _loadModel: async function (urls) {
            for (var i = 0; i < urls.length; i++) {
                var url = urls[i];
                if (typeof url === 'function') {
                    try { url = url(); } catch (e) { url = ''; }
                }
                if (!url) continue;
                try {
                    console.log('AI Upscaler RT/WebGPU: fetching model from', url);
                    var headers = {};
                    if (window.ApiClient && typeof ApiClient.accessToken === 'function' && ApiClient.accessToken()) {
                        headers['Authorization'] = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';
                    }
                    var response = await fetch(url, { headers: headers });
                    if (!response.ok) {
                        console.warn('AI Upscaler RT/WebGPU: model fetch HTTP', response.status, url);
                        continue;
                    }
                    var modelBuffer = await response.arrayBuffer();
                    var session = await window.ort.InferenceSession.create(modelBuffer, {
                        executionProviders: ['webgpu', 'wasm'],
                        graphOptimizationLevel: 'all'
                    });
                    console.log('AI Upscaler RT/WebGPU: model loaded, inputs=', session.inputNames, 'outputs=', session.outputNames);
                    return session;
                } catch (e) {
                    console.warn('AI Upscaler RT/WebGPU: model load failed for', url, e);
                }
            }
            console.warn('AI Upscaler RT/WebGPU: all model URLs failed - fallback');
            return null;
        },

        _renderLoop: async function () {
            if (!this._running) return;
            var self = this;
            try {
                if (this._video.paused || this._video.ended || this._video.readyState < 2) {
                    requestAnimationFrame(function () { self._renderLoop(); });
                    return;
                }
                if (!this._processing) {
                    this._processing = true;
                    var t0 = performance.now();
                    await this._processFrame();
                    this._processing = false;

                    this._frameCount++;
                    var now = performance.now();
                    if (now - this._lastFpsTime > 1000) {
                        var fps = this._frameCount * 1000 / (now - this._lastFpsTime);
                        if (this._fpsCallback) this._fpsCallback(fps);
                        this._frameCount = 0;
                        this._lastFpsTime = now;
                    }
                }
            } catch (e) {
                this._processing = false;
                console.warn('AI Upscaler RT/WebGPU: render-loop frame error (continuing)', e);
            }
            requestAnimationFrame(function () { self._renderLoop(); });
        },

        _processFrame: async function () {
            var rawW = this._video.videoWidth;
            var rawH = this._video.videoHeight;
            if (!rawW || !rawH) return;

            // Safe 1440p target clamping:
            // Since the model scales 4x, clamp input height to 360p (so 4x output = 1440p).
            // This prevents exponential tensor explosion and keeps WebGPU inference responsive.
            var maxInH = Math.min(360, Math.round(1440 / Math.max(1, this._scale)));
            var scaleIn = Math.min(1.0, maxInH / Math.max(1, rawH));
            var inW = Math.round(rawW * scaleIn);
            var inH = Math.round(rawH * scaleIn);

            if (!this._srcCanvas) {
                this._srcCanvas = document.createElement('canvas');
                this._srcCtx = this._srcCanvas.getContext('2d', { willReadFrequently: true });
            }
            if (this._srcCanvas.width !== inW || this._srcCanvas.height !== inH) {
                this._srcCanvas.width = inW;
                this._srcCanvas.height = inH;
            }
            this._srcCtx.drawImage(this._video, 0, 0, inW, inH);
            var imgData = this._srcCtx.getImageData(0, 0, inW, inH);

            var hw = inH * inW;
            var reqLen = 3 * hw;
            if (!this._tensorData || this._tensorData.length !== reqLen) {
                this._tensorData = new Float32Array(reqLen);
            }
            var tensorData = this._tensorData;
            var data = imgData.data;
            var inv255 = 1.0 / 255.0;

            for (var i = 0; i < hw; i++) {
                var srcIdx = i * 4;
                tensorData[i] = data[srcIdx] * inv255;
                tensorData[hw + i] = data[srcIdx + 1] * inv255;
                tensorData[2 * hw + i] = data[srcIdx + 2] * inv255;
            }

            var inputTensor = new window.ort.Tensor('float32', tensorData, [1, 3, inH, inW]);
            var inputName = this._session.inputNames[0];
            var outputName = this._session.outputNames[0];
            var feeds = {};
            feeds[inputName] = inputTensor;
            var results = await this._session.run(feeds);
            var output = results[outputName];

            var outH = output.dims[2];
            var outW = output.dims[3];
            var outData = output.data;

            if (this._canvas.width !== outW || this._canvas.height !== outH) {
                this._canvas.width = outW;
                this._canvas.height = outH;
                this._outImg = this._ctx.createImageData(outW, outH);
                this._outPixels32 = new Uint32Array(this._outImg.data.buffer);
            }
            var outPixels32 = this._outPixels32;
            var totalOut = outH * outW;
            var d0 = 0;
            var d1 = totalOut;
            var d2 = totalOut * 2;

            // Vectorized 32-bit pixel packing (1 write per pixel instead of 4 byte writes)
            for (var j = 0; j < totalOut; j++) {
                var r = (outData[d0 + j] * 255.0) | 0;
                var g = (outData[d1 + j] * 255.0) | 0;
                var b = (outData[d2 + j] * 255.0) | 0;
                outPixels32[j] = 0xFF000000 |
                    ((b < 0 ? 0 : (b > 255 ? 255 : b)) << 16) |
                    ((g < 0 ? 0 : (g > 255 ? 255 : g)) << 8) |
                    (r < 0 ? 0 : (r > 255 ? 255 : r));
            }
            this._ctx.putImageData(this._outImg, 0, 0);

            if (this._video.style.opacity !== '0') {
                this._video.style.opacity = '0';
            }
        }
    };

    window.WebGPUAIUpscaler = WebGPUAIUpscaler;
})();
