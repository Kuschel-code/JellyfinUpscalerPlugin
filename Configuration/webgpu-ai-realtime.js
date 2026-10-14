// WebGPU + ONNX Runtime Web realtime AI upscaler.
//
// Goal: real Real-ESRGAN compact inference in the browser, GPU-accelerated via WebGPU,
// with reused tensors, packed 32-bit pixel writes and a bounded output size.
//
// Defensive fallback chain (any layer can fail; we never break playback):
//   WebGPU not available             -> caller falls back to Lanczos
//   onnxruntime-web load fails       -> caller falls back to Lanczos
//   Model fetch or checksum fails    -> caller falls back to Lanczos
//   Inference too slow (30 frames)   -> onFatal('inference_too_slow') -> Lanczos
//   Inference throws                 -> log + skip frame, do not crash render loop
//
(function () {
    'use strict';

    // Pinned ONNX Runtime Web version.
    var ORT_CDN_URL = 'https://cdn.jsdelivr.net/npm/onnxruntime-web@1.20.1/dist/ort.webgpu.min.js';

    // Two Real-ESRGAN SRVGGNetCompact models: 4x, NCHW RGB float32 in [0,1], any frame size,
    // converted from xinntao/Real-ESRGAN's official weights (BSD-3-Clause; provenance in each
    // repository's README). Every source is pinned to a commit and checked against its
    // SHA-256 before ONNX Runtime sees it.
    // - The onnx-community repository this used before is gone (HTTP 401), which had turned
    //   the mode off for everyone without a sign.
    // - A "resolve/main" URL follows whatever its owner uploads next.
    // - No Jellyfin token goes to these hosts.
    var MODEL_CATALOG = {
        'realesr-animevideov3-x4': {
            scale: 4,
            name: 'Real-ESRGAN AnimeVideo v3 x4',
            sources: [
                { url: 'https://huggingface.co/xiaojiaenen/lingtuan-sr-models/resolve/29adb237e42934ce2cc87a1a4887f4950cb8ab55/realesr-animevideov3-x4.onnx',
                  sha256: '14114520123405712c92c2b266a49e906f03b595335e53e3604cdc03c5a408e4' },
                { url: 'https://huggingface.co/nimjinwei/designerplatform-models/resolve/12754caf542bcf74a2cefc18d9f1dc5ffbe5ec71/realesr-animevideov3.onnx',
                  sha256: 'ca1b795ec579a82acc45644e4550e7ddf6530c1c1388a62511667c13db97fa25' }
            ]
        },
        'realesr-general-x4v3': {
            scale: 4,
            name: 'Real-ESRGAN General x4 v3',
            sources: [
                { url: 'https://huggingface.co/CoderViking/realesr-general-x4v3-onnx/resolve/c6a971706797c7502945a2b4c4274fce4900d4ab/realesr-general-x4v3.onnx',
                  sha256: '1940a93ee08283a0a7286183186357b1688fe9fa8ede74604b424586aaddf112' },
                { url: 'https://huggingface.co/nimjinwei/designerplatform-models/resolve/12754caf542bcf74a2cefc18d9f1dc5ffbe5ec71/realesr-general-x4v3.onnx',
                  sha256: '7bd8296de37d280246d7a848d01305c84e3906e73c8509d37b73563adb639122' }
            ]
        }
    };
    var DEFAULT_MODEL = 'realesr-animevideov3-x4';
    // keys used before the catalog was rebuilt
    var MODEL_ALIASES = { 'realesrgan-compact-x2': 'realesr-animevideov3-x4', 'realesrgan-compact-x4': 'realesr-general-x4v3' };

    // Output height cap. The input is shrunk to MAX_OUTPUT_LINES / scale lines (360 for a 4x
    // model) so the tensors stay small enough for real-time inference.
    var MAX_OUTPUT_LINES = 1440;

    // Hex SHA-256 of the bytes, or null where the browser has no WebCrypto: a Jellyfin
    // served over plain HTTP is not a secure context. The commit-pinned URL still holds then.
    async function sha256Hex(buffer) {
        var subtle = window.crypto && window.crypto.subtle;
        if (!subtle) return null;
        var digest = new Uint8Array(await subtle.digest('SHA-256', buffer));
        var hex = '';
        for (var i = 0; i < digest.length; i++) hex += ('0' + digest[i].toString(16)).slice(-2);
        return hex;
    }

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
        _slowFrameCount: 0,
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
            this._modelKey = MODEL_ALIASES[opts.modelKey] || opts.modelKey || DEFAULT_MODEL;
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
            this._setStatus('Downloading Neural Model (2.5MB)...');
            var session = await this._loadModel(modelEntry.sources);
            if (!session) return false;
            this._session = session;

            this._setStatus('Compiling WebGPU Kernels...');
            this._scale = await this._probeScale(session, modelEntry.scale);
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
            this._slowFrameCount = 0;
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

        _loadModel: async function (sources) {
            for (var i = 0; i < sources.length; i++) {
                var source = sources[i];
                try {
                    console.log('AI Upscaler RT/WebGPU: fetching model from', source.url);
                    // No credentials: these are third-party hosts, and the viewer's Jellyfin
                    // token must never go there.
                    var response = await fetch(source.url, { credentials: 'omit' });
                    if (!response.ok) {
                        console.warn('AI Upscaler RT/WebGPU: model fetch HTTP', response.status, source.url);
                        continue;
                    }
                    var modelBuffer = await response.arrayBuffer();
                    var digest = await sha256Hex(modelBuffer);
                    if (digest === null) {
                        console.info('AI Upscaler RT/WebGPU: no WebCrypto (page not served over HTTPS); relying on the commit-pinned URL');
                    } else if (digest !== source.sha256) {
                        console.warn('AI Upscaler RT/WebGPU: model checksum mismatch, skipping', source.url, digest);
                        continue;
                    }
                    var session = await window.ort.InferenceSession.create(modelBuffer, {
                        executionProviders: ['webgpu', 'wasm'],
                        graphOptimizationLevel: 'all'
                    });
                    console.log('AI Upscaler RT/WebGPU: model loaded, inputs=', session.inputNames, 'outputs=', session.outputNames);
                    return session;
                } catch (e) {
                    console.warn('AI Upscaler RT/WebGPU: model load failed for', source.url, e);
                }
            }
            console.warn('AI Upscaler RT/WebGPU: all model sources failed - fallback');
            return null;
        },

        // The scale the model really has, measured with a 16x16 frame (which also warms the
        // kernels up). The catalog's figure is only the fallback: an entry labelled x2 once
        // loaded 4x files, and the input cap was computed from the wrong number.
        _probeScale: async function (session, declared) {
            try {
                var feeds = {};
                feeds[session.inputNames[0]] = new window.ort.Tensor('float32', new Float32Array(3 * 16 * 16), [1, 3, 16, 16]);
                var out = (await session.run(feeds))[session.outputNames[0]];
                var measured = Math.round(out.dims[2] / 16);
                if (measured >= 1 && measured <= 8) return measured;
            } catch (e) {
                console.warn('AI Upscaler RT/WebGPU: scale probe failed, using the catalog value', e);
            }
            return declared;
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
                    if (!this._running) return;

                    // Hardware that cannot keep up hands over to Lanczos instead of stuttering
                    // for the rest of the film.
                    if (performance.now() - t0 > 50) {
                        if (++this._slowFrameCount > 30) {
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

            // Never more than MAX_OUTPUT_LINES of output. A fixed 360-line input cap also held a
            // 2x model to 720 lines out, below a 1080p source.
            var maxInH = Math.max(1, Math.floor(MAX_OUTPUT_LINES / Math.max(1, this._scale)));
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
