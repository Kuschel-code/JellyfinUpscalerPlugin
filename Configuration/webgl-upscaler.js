// WebGL/WebGPU Client-Side Upscaling Shader
// Provides real-time video enhancement without server processing

(function() {
    'use strict';
    
    const WebGLUpscaler = {
        
        enabled: false,
        canvas: null,
        gl: null,
        program: null,
        videoElement: null,
        animationFrameId: null,
        sharpness: 0.5,
        onFpsUpdate: null,
        _fpsFrameCount: 0,
        _fpsLastTime: 0,
        _explicitWidth: 0,
        _explicitHeight: 0,
        _vertexShader: null,
        _fragmentShader: null,
        _uniformLocations: null,
        
        // Shader sources
        vertexShaderSource: `
            attribute vec2 a_position;
            attribute vec2 a_texCoord;
            varying vec2 v_texCoord;
            
            void main() {
                gl_Position = vec4(a_position, 0.0, 1.0);
                v_texCoord = a_texCoord;
            }
        `,
        
        // Lanczos2 resampling shader — real sub-pixel reconstruction
        // Samples a 4x4 neighborhood from the source texture using a Lanczos kernel
        // with window size 2, then applies optional CAS (Contrast Adaptive Sharpening)
        fragmentShaderSource: `
            precision highp float;

            uniform sampler2D u_texture;
            uniform vec2 u_resolution;  // output (canvas) size
            uniform float u_sharpness;  // 0.0 – 1.0
            varying vec2 v_texCoord;

            #define PI 3.14159265359

            // sinc(x) = sin(pi*x) / (pi*x), sinc(0)=1
            float sinc(float x) {
                if (abs(x) < 1e-5) return 1.0;
                float px = PI * x;
                return sin(px) / px;
            }

            // Lanczos kernel, a = 2 (4-tap per axis, 16-tap total)
            float lanczos2(float x) {
                if (abs(x) >= 2.0) return 0.0;
                return sinc(x) * sinc(x / 2.0);
            }

            // Lanczos2 resample: reconstruct one pixel from 4x4 source neighbourhood
            vec3 lanczosResample(vec2 uv, vec2 srcTexelSize) {
                // Map output UV to source pixel coordinate
                vec2 srcCoord = uv / srcTexelSize - 0.5;
                vec2 center = floor(srcCoord) + 0.5;
                vec2 f = srcCoord - center; // fractional offset in [-0.5, 0.5)

                vec3 color = vec3(0.0);
                float totalWeight = 0.0;

                for (int j = -1; j <= 2; j++) {
                    for (int i = -1; i <= 2; i++) {
                        vec2 offset = vec2(float(i), float(j));
                        float w = lanczos2(f.x - offset.x + 1.0) * lanczos2(f.y - offset.y + 1.0);
                        vec2 sampleUV = (center + offset) * srcTexelSize;
                        color += texture2D(u_texture, sampleUV).rgb * w;
                        totalWeight += w;
                    }
                }
                return color / totalWeight;
            }

            // CAS (Contrast Adaptive Sharpening) pass
            vec3 casSharpening(vec2 uv, vec3 center, vec2 texelSize, float strength) {
                vec3 n = texture2D(u_texture, uv + vec2(0.0, -texelSize.y)).rgb;
                vec3 s = texture2D(u_texture, uv + vec2(0.0,  texelSize.y)).rgb;
                vec3 e = texture2D(u_texture, uv + vec2( texelSize.x, 0.0)).rgb;
                vec3 w = texture2D(u_texture, uv + vec2(-texelSize.x, 0.0)).rgb;

                vec3 minRGB = min(min(n, s), min(e, w));
                vec3 maxRGB = max(max(n, s), max(e, w));
                // Adaptive sharpening weight based on local contrast
                vec3 d = 1.0 / (maxRGB - minRGB + 0.05);
                d = clamp(d * (-0.125), -0.1, 0.0);
                float peak = mix(-0.125, -0.04, strength);
                d = max(d, vec3(peak));

                vec3 result = (center + (n + s + e + w) * d) / (1.0 + 4.0 * d);
                return clamp(result, 0.0, 1.0);
            }

            void main() {
                // Source texel size (from the video/texture, not the output canvas)
                vec2 srcTexelSize = 1.0 / u_resolution;

                // Lanczos2 reconstruction
                vec3 color = lanczosResample(v_texCoord, srcTexelSize);

                // Optional CAS sharpening pass (strength driven by u_sharpness slider)
                if (u_sharpness > 0.01) {
                    color = casSharpening(v_texCoord, color, srcTexelSize, u_sharpness);
                }

                gl_FragColor = vec4(color, 1.0);
            }
        `,
        
        // Initialize WebGL context
        init: function(videoElement) {
            try {
                console.log('AI Upscaler: Initializing WebGL upscaler...');
                
                this.videoElement = videoElement;
                
                // Create canvas overlay
                this.canvas = document.createElement('canvas');
                this.canvas.id = 'aiUpscalerCanvas';
                this.canvas.style.position = 'absolute';
                this.canvas.style.top = '0';
                this.canvas.style.left = '0';
                this.canvas.style.width = '100%';
                this.canvas.style.height = '100%';
                this.canvas.style.pointerEvents = 'none';
                this.canvas.style.zIndex = '1000';
                
                // Get WebGL context
                this.gl = this.canvas.getContext('webgl2') || this.canvas.getContext('webgl');
                
                if (!this.gl) {
                    console.error('AI Upscaler: WebGL not supported');
                    return false;
                }
                
                // Compile shaders
                if (!this.compileShaders()) {
                    console.error('AI Upscaler: Failed to compile shaders');
                    return false;
                }
                
                // Setup geometry
                this.setupGeometry();
                
                // Insert canvas into video container
                const videoContainer = videoElement.parentElement;
                if (videoContainer) {
                    videoContainer.style.position = 'relative';
                    videoContainer.appendChild(this.canvas);
                }
                
                // Handle WebGL context loss and restoration
                this.canvas.addEventListener('webglcontextlost', function(e) {
                    e.preventDefault();
                    console.warn('AI Upscaler: WebGL context lost');
                    WebGLUpscaler.disable();
                }, false);

                this.canvas.addEventListener('webglcontextrestored', function() {
                    console.log('AI Upscaler: WebGL context restored, reinitializing...');
                    WebGLUpscaler.gl = WebGLUpscaler.canvas.getContext('webgl2') || WebGLUpscaler.canvas.getContext('webgl');
                    if (WebGLUpscaler.gl && WebGLUpscaler.compileShaders()) {
                        WebGLUpscaler.setupGeometry();
                        WebGLUpscaler.texture = WebGLUpscaler.gl.createTexture();
                        WebGLUpscaler.gl.bindTexture(WebGLUpscaler.gl.TEXTURE_2D, WebGLUpscaler.texture);
                        WebGLUpscaler.gl.texParameteri(WebGLUpscaler.gl.TEXTURE_2D, WebGLUpscaler.gl.TEXTURE_WRAP_S, WebGLUpscaler.gl.CLAMP_TO_EDGE);
                        WebGLUpscaler.gl.texParameteri(WebGLUpscaler.gl.TEXTURE_2D, WebGLUpscaler.gl.TEXTURE_WRAP_T, WebGLUpscaler.gl.CLAMP_TO_EDGE);
                        WebGLUpscaler.gl.texParameteri(WebGLUpscaler.gl.TEXTURE_2D, WebGLUpscaler.gl.TEXTURE_MIN_FILTER, WebGLUpscaler.gl.LINEAR);
                        console.log('AI Upscaler: WebGL context restored successfully');
                    }
                }, false);

                console.log('AI Upscaler: WebGL upscaler initialized successfully');
                return true;
                
            } catch (error) {
                console.error('AI Upscaler: Initialization failed:', error);
                return false;
            }
        },
        
        // Compile shaders
        compileShaders: function() {
            const gl = this.gl;
            
            // Vertex shader
            this._vertexShader = gl.createShader(gl.VERTEX_SHADER);
            gl.shaderSource(this._vertexShader, this.vertexShaderSource);
            gl.compileShader(this._vertexShader);

            if (!gl.getShaderParameter(this._vertexShader, gl.COMPILE_STATUS)) {
                console.error('Vertex shader error:', gl.getShaderInfoLog(this._vertexShader));
                return false;
            }

            // Fragment shader
            this._fragmentShader = gl.createShader(gl.FRAGMENT_SHADER);
            gl.shaderSource(this._fragmentShader, this.fragmentShaderSource);
            gl.compileShader(this._fragmentShader);

            if (!gl.getShaderParameter(this._fragmentShader, gl.COMPILE_STATUS)) {
                console.error('Fragment shader error:', gl.getShaderInfoLog(this._fragmentShader));
                return false;
            }

            // Link program
            this.program = gl.createProgram();
            gl.attachShader(this.program, this._vertexShader);
            gl.attachShader(this.program, this._fragmentShader);
            gl.linkProgram(this.program);
            
            if (!gl.getProgramParameter(this.program, gl.LINK_STATUS)) {
                console.error('Program link error:', gl.getProgramInfoLog(this.program));
                return false;
            }

            // Cache uniform locations once (avoids per-frame GPU roundtrips)
            this._uniformLocations = {
                resolution: gl.getUniformLocation(this.program, 'u_resolution'),
                sharpness: gl.getUniformLocation(this.program, 'u_sharpness')
            };

            return true;
        },
        
        // Setup geometry buffers
        setupGeometry: function() {
            const gl = this.gl;
            
            // Full-screen quad
            const positions = new Float32Array([
                -1, -1,
                 1, -1,
                -1,  1,
                 1,  1
            ]);
            
            const texCoords = new Float32Array([
                0, 1,
                1, 1,
                0, 0,
                1, 0
            ]);
            
            // Position buffer
            const positionBuffer = gl.createBuffer();
            gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
            gl.bufferData(gl.ARRAY_BUFFER, positions, gl.STATIC_DRAW);
            
            const positionLocation = gl.getAttribLocation(this.program, 'a_position');
            gl.enableVertexAttribArray(positionLocation);
            gl.vertexAttribPointer(positionLocation, 2, gl.FLOAT, false, 0, 0);
            
            // Texture coordinate buffer
            const texCoordBuffer = gl.createBuffer();
            gl.bindBuffer(gl.ARRAY_BUFFER, texCoordBuffer);
            gl.bufferData(gl.ARRAY_BUFFER, texCoords, gl.STATIC_DRAW);
            
            const texCoordLocation = gl.getAttribLocation(this.program, 'a_texCoord');
            gl.enableVertexAttribArray(texCoordLocation);
            gl.vertexAttribPointer(texCoordLocation, 2, gl.FLOAT, false, 0, 0);

            // Store buffer references for cleanup
            this._positionBuffer = positionBuffer;
            this._texCoordBuffer = texCoordBuffer;

            // Create texture
            this.texture = gl.createTexture();
            gl.bindTexture(gl.TEXTURE_2D, this.texture);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        },
        
        // Render frame
        render: function() {
            if (!this.enabled || !this.videoElement || !this.gl || this.gl.isContextLost()) {
                return;
            }
            
            const gl = this.gl;
            const video = this.videoElement;
            
            // Update canvas size (use explicit size if set, else video native)
            var targetW = this._explicitWidth || video.videoWidth;
            var targetH = this._explicitHeight || video.videoHeight;
            if (this.canvas.width !== targetW || this.canvas.height !== targetH) {
                this.canvas.width = targetW;
                this.canvas.height = targetH;
                gl.viewport(0, 0, targetW, targetH);
            }
            
            // Upload video frame to texture
            gl.bindTexture(gl.TEXTURE_2D, this.texture);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB, gl.RGB, gl.UNSIGNED_BYTE, video);
            
            // Use shader program
            gl.useProgram(this.program);
            
            // Set uniforms (using cached locations)
            // u_resolution must be SOURCE (video) size for Lanczos kernel to sample correctly
            gl.uniform2f(this._uniformLocations.resolution, video.videoWidth, video.videoHeight);
            gl.uniform1f(this._uniformLocations.sharpness, this.sharpness);

            // Draw
            gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);

            // FPS tracking
            this._fpsFrameCount++;
            var now = performance.now();
            if (now - this._fpsLastTime >= 1000) {
                var fps = Math.round(this._fpsFrameCount * 1000 / (now - this._fpsLastTime));
                this._fpsFrameCount = 0;
                this._fpsLastTime = now;
                if (typeof this.onFpsUpdate === 'function') {
                    this.onFpsUpdate(fps);
                }
            }

            // Continue rendering
            this.animationFrameId = requestAnimationFrame(() => this.render());
        },
        
        // Enable upscaling
        enable: function() {
            if (!this.canvas || !this.gl) {
                console.error('AI Upscaler: WebGL not initialized');
                return;
            }

            this.enabled = true;
            this._fpsLastTime = performance.now();
            this._fpsFrameCount = 0;
            this.canvas.style.display = 'block';
            this.videoElement.style.opacity = '0';
            this.render();
            
            console.log('AI Upscaler: WebGL upscaling enabled');
        },
        
        // Disable upscaling
        disable: function() {
            this.enabled = false;
            
            if (this.canvas) {
                this.canvas.style.display = 'none';
            }
            
            if (this.videoElement) {
                this.videoElement.style.opacity = '1';
            }
            
            if (this.animationFrameId) {
                cancelAnimationFrame(this.animationFrameId);
                this.animationFrameId = null;
            }
            
            console.log('AI Upscaler: WebGL upscaling disabled');
        },
        
        // Toggle upscaling
        toggle: function() {
            if (this.enabled) {
                this.disable();
            } else {
                this.enable();
            }
        },
        
        // Set sharpness (0.0 to 1.0)
        setSharpness: function(value) {
            this.sharpness = Math.max(0, Math.min(1, value));
        },

        // Set explicit canvas output size (0 = use video native)
        setCanvasSize: function(w, h) {
            this._explicitWidth = w || 0;
            this._explicitHeight = h || 0;
        },

        // Cleanup
        destroy: function() {
            this.disable();
            
            if (this.canvas && this.canvas.parentElement) {
                this.canvas.parentElement.removeChild(this.canvas);
            }
            
            if (this._positionBuffer) this.gl.deleteBuffer(this._positionBuffer);
            if (this._texCoordBuffer) this.gl.deleteBuffer(this._texCoordBuffer);

            if (this.gl && this.texture) {
                this.gl.deleteTexture(this.texture);
            }
            
            if (this.gl && this._vertexShader) {
                this.gl.deleteShader(this._vertexShader);
            }
            if (this.gl && this._fragmentShader) {
                this.gl.deleteShader(this._fragmentShader);
            }
            if (this.gl && this.program) {
                this.gl.deleteProgram(this.program);
            }

            this.canvas = null;
            this.gl = null;
            this.program = null;
            this._vertexShader = null;
            this._fragmentShader = null;
            this.videoElement = null;
        }
    };
    
    // Export to global scope
    window.AIUpscalerWebGL = WebGLUpscaler;
    
    console.log('AI Upscaler: WebGL shader module loaded');
})();
