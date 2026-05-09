// v1.7.1 - WebGPU + ONNX Runtime Web realtime AI upscaler.
//
// Goal: real Real-ESRGAN compact inference in the browser, GPU-accelerated via WebGPU.
// Industry-comparable to mpv-upscale-2x_animejanai (TensorRT) but for any browser with
// WebGPU support (Chrome 113+, Edge 113+, Firefox 142+, Safari 26+, Opera 99+).
//
// Defensive fallback chain (any layer can fail; we never break playback):
//   WebGPU not available             -> caller falls back to Lanczos
//   onnxruntime-web load fails       -> caller falls back to Lanczos
//   Model fetch fails                -> caller falls back to Lanczos
//   Inference throws                 -> log + skip frame, do not crash render loop
//   Inference too slow (<0.8x rate)  -> auto-stop and log a hint
//
// onnxruntime-web is loaded from jsdelivr at runtime (no plugin bundling).
// Model files are fetched from HuggingFace mirrors (configured below).
(function () {
    'use strict';

    // Pinned ONNX Runtime Web version. Bump deliberately when also re-testing the integration.
    var ORT_CDN_URL = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.20.1/dist/ort.webgpu.min.js';

    // Real-ESRGAN compact (anime + general). FP16 quantized, ~3-5MB each.
    var MODEL_CATALOG = {
        'realesrgan-compact-x2': {
            scale: 2,
            urls: [
                'https://huggingface.co/onnx-community/Real-ESRGAN-Anime/resolve/main/realesr-animevideov3-x2-fp16.onnx',
                'https://cdn.jsdelivr.net/gh/onnx-community/Real-ESRGAN-Anime/realesr-animevideov3-x2-fp16.onnx'
            ]
        },
        'realesrgan-compact-x4': {
            scale: 4,
            urls: [
                'https://huggingface.co/onnx-community/Real-ESRGAN-Anime/resolve/main/realesr-animevideov3-x4-fp16.onnx'
            ]
        }
    };

    var WebGPUAIUpscaler = {
        _video: null,
        _canvas: null,
        _ctx: null,
        _session: null,
        _modelKey: null,
        _scale: 2,
        _running: false,
        _fpsCallback: null,
        _frameCount: 0,
        _lastFpsTime: 0,
        _slowFrameCount: 0,
        _onFatal: null,

        start: async function (video, opts) {
            opts = opts || {};
            this._video = video;
            this._modelKey = opts.modelKey || 'realesrgan-compact-x2';
            this._fpsCallback = opts.fpsCallback || null;
            this._onFatal = opts.onFatal || function () {};

            if (!('gpu' in navigator)) {
                console.warn('AI Upscaler RT/WebGPU: navigator.gpu missing - browser does not support WebGPU. Fallback to Lanczos.');
                return false;
            }
            try {
                var adapter = await navigator.gpu.requestAdapter();
                if (!adapter) {
                    console.warn('AI Upscaler RT/WebGPU: requestAdapter() returned null. Fallback to Lanczos.');
                    return false;
                }
            } catch (e) {
                console.warn('AI Upscaler RT/WebGPU: WebGPU adapter init threw', e);
                return false;
            }

            var ortLoaded = await this._loadOrt();
            if (!ortLoaded) return false;

            var modelEntry = MODEL_CATALOG[this._modelKey];
            if (!modelEntry) {
                console.warn('AI Upscaler RT/WebGPU: unknown model key', this._modelKey);
                return false;
            }
            this._scale = modelEntry.scale;
            var session = await this._loadModel(modelEntry.urls);
            if (!session) return false;
            this._session = session;

            this._canvas = document.createElement('canvas');
            this._canvas.style.cssText = 'position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:999;';
            this._canvas.id = 'aiWebgpuCanvas';
            this._ctx = this._canvas.getContext('2d');
            var parent = video.parentElement;
            if (parent) {
                parent.style.position = 'relative';
                parent.appendChild(this._canvas);
            }

            this._running = true;
            this._frameCount = 0;
            this._slowFrameCount = 0;
            this._lastFpsTime = performance.now();
            console.log('AI Upscaler RT/WebGPU: started with', this._modelKey, 'scale=', this._scale);
            this._renderLoop();
            return true;
        },

        stop: function () {
            this._running = false;
            try { if (this._session && typeof this._session.release === 'function') this._session.release(); } catch (e) {}
            this._session = null;
            if (this._canvas && this._canvas.parentElement) {
                this._canvas.parentElement.removeChild(this._canvas);
            }
            this._canvas = null;
            this._ctx = null;
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
                try {
                    console.log('AI Upscaler RT/WebGPU: fetching model from', urls[i]);
                    var response = await fetch(urls[i]);
                    if (!response.ok) {
                        console.warn('AI Upscaler RT/WebGPU: model fetch HTTP', response.status, urls[i]);
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
                    console.warn('AI Upscaler RT/WebGPU: model load failed for', urls[i], e);
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
                var t0 = performance.now();
                await this._processFrame();
                var elapsed = performance.now() - t0;

                if (elapsed > 50) {
                    this._slowFrameCount++;
                    if (this._slowFrameCount > 30) {
                        console.warn('AI Upscaler RT/WebGPU: 30 consecutive slow frames - hardware too weak. Stopping.');
                        this._onFatal('inference_too_slow');
                        this.stop();
                        return;
                    }
                } else {
                    this._slowFrameCount = 0;
                }

                this._frameCount++;
                var now = performance.now();
                if (now - this._lastFpsTime > 1000) {
                    var fps = this._frameCount * 1000 / (now - this._lastFpsTime);
                    if (this._fpsCallback) this._fpsCallback(fps);
                    this._frameCount = 0;
                    this._lastFpsTime = now;
                }
            } catch (e) {
                console.warn('AI Upscaler RT/WebGPU: render-loop frame error (continuing)', e);
            }
            requestAnimationFrame(function () { self._renderLoop(); });
        },

        _processFrame: async function () {
            var w = this._video.videoWidth;
            var h = this._video.videoHeight;
            if (!w || !h) return;

            var srcCanvas = document.createElement('canvas');
            srcCanvas.width = w;
            srcCanvas.height = h;
            var srcCtx = srcCanvas.getContext('2d');
            srcCtx.drawImage(this._video, 0, 0, w, h);
            var imgData = srcCtx.getImageData(0, 0, w, h);

            var tensorData = new Float32Array(3 * h * w);
            for (var y = 0; y < h; y++) {
                for (var x = 0; x < w; x++) {
                    var srcIdx = (y * w + x) * 4;
                    var dstIdx = y * w + x;
                    tensorData[0 * h * w + dstIdx] = imgData.data[srcIdx + 0] / 255.0;
                    tensorData[1 * h * w + dstIdx] = imgData.data[srcIdx + 1] / 255.0;
                    tensorData[2 * h * w + dstIdx] = imgData.data[srcIdx + 2] / 255.0;
                }
            }
            var inputTensor = new window.ort.Tensor('float32', tensorData, [1, 3, h, w]);
            var inputName = this._session.inputNames[0];
            var outputName = this._session.outputNames[0];
            var feeds = {};
            feeds[inputName] = inputTensor;
            var results = await this._session.run(feeds);
            var output = results[outputName];

            var outH = output.dims[2];
            var outW = output.dims[3];
            var outData = output.data;

            this._canvas.width = outW;
            this._canvas.height = outH;
            var outImg = this._ctx.createImageData(outW, outH);
            for (var oy = 0; oy < outH; oy++) {
                for (var ox = 0; ox < outW; ox++) {
                    var srcI = oy * outW + ox;
                    var dstI = srcI * 4;
                    outImg.data[dstI + 0] = Math.max(0, Math.min(255, outData[0 * outH * outW + srcI] * 255));
                    outImg.data[dstI + 1] = Math.max(0, Math.min(255, outData[1 * outH * outW + srcI] * 255));
                    outImg.data[dstI + 2] = Math.max(0, Math.min(255, outData[2 * outH * outW + srcI] * 255));
                    outImg.data[dstI + 3] = 255;
                }
            }
            this._ctx.putImageData(outImg, 0, 0);
        }
    };

    window.WebGPUAIUpscaler = WebGPUAIUpscaler;
})();
