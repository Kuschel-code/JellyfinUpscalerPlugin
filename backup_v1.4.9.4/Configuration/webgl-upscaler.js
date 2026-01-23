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
        
        // FSR-inspired fragment shader with edge-aware upscaling
        fragmentShaderSource: `
            precision highp float;
            
            uniform sampler2D u_texture;
            uniform vec2 u_resolution;
            uniform float u_sharpness;
            varying vec2 v_texCoord;
            
            // Edge detection kernel
            vec3 detectEdge(vec2 uv, vec2 texelSize) {
                vec3 n = texture2D(u_texture, uv + vec2(0.0, -texelSize.y)).rgb;
                vec3 s = texture2D(u_texture, uv + vec2(0.0, texelSize.y)).rgb;
                vec3 e = texture2D(u_texture, uv + vec2(texelSize.x, 0.0)).rgb;
                vec3 w = texture2D(u_texture, uv + vec2(-texelSize.x, 0.0)).rgb;
                
                vec3 laplacian = abs(-4.0 * texture2D(u_texture, uv).rgb + n + s + e + w);
                return laplacian;
            }
            
            // FSR EASU (Edge-Adaptive Spatial Upsampling)
            vec3 fsrUpscale(vec2 uv, vec2 texelSize) {
                vec3 center = texture2D(u_texture, uv).rgb;
                vec3 edge = detectEdge(uv, texelSize);
                
                // Sample neighbors
                vec3 n  = texture2D(u_texture, uv + vec2(0.0, -texelSize.y)).rgb;
                vec3 s  = texture2D(u_texture, uv + vec2(0.0, texelSize.y)).rgb;
                vec3 e  = texture2D(u_texture, uv + vec2(texelSize.x, 0.0)).rgb;
                vec3 w  = texture2D(u_texture, uv + vec2(-texelSize.x, 0.0)).rgb;
                vec3 ne = texture2D(u_texture, uv + vec2(texelSize.x, -texelSize.y)).rgb;
                vec3 nw = texture2D(u_texture, uv + vec2(-texelSize.x, -texelSize.y)).rgb;
                vec3 se = texture2D(u_texture, uv + vec2(texelSize.x, texelSize.y)).rgb;
                vec3 sw = texture2D(u_texture, uv + vec2(-texelSize.x, texelSize.y)).rgb;
                
                // Edge-adaptive weighting
                float edgeStrength = length(edge);
                float sharpening = u_sharpness * (1.0 + edgeStrength * 2.0);
                
                // Bilateral filter with sharpening
                vec3 result = center * (1.0 + sharpening);
                result += (n + s + e + w) * -sharpening * 0.25;
                
                // Clamp to valid range
                return clamp(result, 0.0, 1.0);
            }
            
            void main() {
                vec2 texelSize = 1.0 / u_resolution;
                vec3 color = fsrUpscale(v_texCoord, texelSize);
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
            const vertexShader = gl.createShader(gl.VERTEX_SHADER);
            gl.shaderSource(vertexShader, this.vertexShaderSource);
            gl.compileShader(vertexShader);
            
            if (!gl.getShaderParameter(vertexShader, gl.COMPILE_STATUS)) {
                console.error('Vertex shader error:', gl.getShaderInfoLog(vertexShader));
                return false;
            }
            
            // Fragment shader
            const fragmentShader = gl.createShader(gl.FRAGMENT_SHADER);
            gl.shaderSource(fragmentShader, this.fragmentShaderSource);
            gl.compileShader(fragmentShader);
            
            if (!gl.getShaderParameter(fragmentShader, gl.COMPILE_STATUS)) {
                console.error('Fragment shader error:', gl.getShaderInfoLog(fragmentShader));
                return false;
            }
            
            // Link program
            this.program = gl.createProgram();
            gl.attachShader(this.program, vertexShader);
            gl.attachShader(this.program, fragmentShader);
            gl.linkProgram(this.program);
            
            if (!gl.getProgramParameter(this.program, gl.LINK_STATUS)) {
                console.error('Program link error:', gl.getProgramInfoLog(this.program));
                return false;
            }
            
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
            if (!this.enabled || !this.videoElement || !this.gl) {
                return;
            }
            
            const gl = this.gl;
            const video = this.videoElement;
            
            // Update canvas size
            if (this.canvas.width !== video.videoWidth || this.canvas.height !== video.videoHeight) {
                this.canvas.width = video.videoWidth;
                this.canvas.height = video.videoHeight;
                gl.viewport(0, 0, this.canvas.width, this.canvas.height);
            }
            
            // Upload video frame to texture
            gl.bindTexture(gl.TEXTURE_2D, this.texture);
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB, gl.RGB, gl.UNSIGNED_BYTE, video);
            
            // Use shader program
            gl.useProgram(this.program);
            
            // Set uniforms
            const resolutionLocation = gl.getUniformLocation(this.program, 'u_resolution');
            gl.uniform2f(resolutionLocation, this.canvas.width, this.canvas.height);
            
            const sharpnessLocation = gl.getUniformLocation(this.program, 'u_sharpness');
            gl.uniform1f(sharpnessLocation, 0.5); // Adjustable sharpness
            
            // Draw
            gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
            
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
        
        // Cleanup
        destroy: function() {
            this.disable();
            
            if (this.canvas && this.canvas.parentElement) {
                this.canvas.parentElement.removeChild(this.canvas);
            }
            
            if (this.gl && this.texture) {
                this.gl.deleteTexture(this.texture);
            }
            
            if (this.gl && this.program) {
                this.gl.deleteProgram(this.program);
            }
            
            this.canvas = null;
            this.gl = null;
            this.program = null;
            this.videoElement = null;
        }
    };
    
    // Export to global scope
    window.AIUpscalerWebGL = WebGLUpscaler;
    
    console.log('AI Upscaler: WebGL shader module loaded');
})();
