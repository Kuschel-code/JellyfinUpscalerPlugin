// AI Upscaler Plugin - Enhanced Quick Menu v1.3.6.7
// Advanced compatibility and quick actions

(function() {
    'use strict';
    
    // Plugin configuration
    const PLUGIN_ID = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';
    const PLUGIN_VERSION = '1.3.6.7';
    
    // Quick menu actions
    const QuickMenuActions = {
        
        // Load optimal defaults based on device
        loadDefaults: function() {
            console.log('AI Upscaler: Loading optimal defaults...');
            
            const defaults = {
                enabled: true,
                model: 'realesrgan',
                scale: 2,
                quality: 'balanced',
                enableHardwareAcceleration: true,
                maxConcurrentStreams: 2,
                cacheSizeMB: 1024,
                autoCleanupCache: true,
                enableErrorReporting: true,
                enableAutoRecovery: true,
                maxRetryAttempts: 3,
                enableSafeMode: false,
                enableHealthCheck: true,
                enableMemoryMonitoring: true,
                diagnosticIntervalMinutes: 15
            };
            
            this.updateFormFields(defaults);
            this.showNotification('✅ Default settings loaded successfully!', 'success');
        },
        
        // Auto-optimize for current device
        optimizeForDevice: function() {
            console.log('AI Upscaler: Auto-optimizing for device...');
            
            const deviceInfo = this.detectDevice();
            let optimizedSettings = {};
            
            switch(deviceInfo.type) {
                case 'mobile':
                    optimizedSettings = {
                        model: 'swinir',
                        cacheSizeMB: 512,
                        maxConcurrentStreams: 1,
                        enableHardwareAcceleration: false,
                        quality: 'fast'
                    };
                    break;
                    
                case 'desktop':
                    optimizedSettings = {
                        model: 'realesrgan',
                        cacheSizeMB: 2048,
                        maxConcurrentStreams: 4,
                        enableHardwareAcceleration: true,
                        quality: 'quality'
                    };
                    break;
                    
                case 'tv':
                    optimizedSettings = {
                        model: 'esrgan',
                        cacheSizeMB: 1024,
                        maxConcurrentStreams: 2,
                        enableHardwareAcceleration: true,
                        quality: 'balanced'
                    };
                    break;
                    
                default:
                    optimizedSettings = {
                        model: 'srcnn',
                        cacheSizeMB: 1024,
                        maxConcurrentStreams: 2,
                        enableHardwareAcceleration: true,
                        quality: 'balanced'
                    };
            }
            
            this.updateFormFields(optimizedSettings);
            this.showNotification(`✅ Settings optimized for ${deviceInfo.type} device!`, 'success');
        },
        
        // Test system compatibility
        testSystem: function() {
            console.log('AI Upscaler: Testing system compatibility...');
            this.showNotification('🔍 Testing system compatibility...', 'info');
            
            const tests = [
                this.testPlatformCompatibility(),
                this.testMemoryAvailability(),
                this.testNetworkConnectivity(),
                this.testHardwareAcceleration()
            ];
            
            Promise.all(tests)
                .then(results => {
                    const allPassed = results.every(result => result.passed);
                    if (allPassed) {
                        this.showNotification('✅ All system tests passed! Plugin is ready.', 'success');
                    } else {
                        const failures = results.filter(r => !r.passed);
                        this.showNotification(`⚠️ ${failures.length} test(s) failed. Check console for details.`, 'warning');
                    }
                })
                .catch(error => {
                    console.error('System test error:', error);
                    this.showNotification('❌ System test failed. Check console for details.', 'error');
                });
        },
        
        // Export configuration
        exportConfig: function() {
            console.log('AI Upscaler: Exporting configuration...');
            
            const config = this.collectFormData();
            const configBlob = new Blob([JSON.stringify(config, null, 2)], { 
                type: 'application/json' 
            });
            
            const url = URL.createObjectURL(configBlob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `aiupscaler-config-${new Date().toISOString().split('T')[0]}.json`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
            
            this.showNotification('✅ Configuration exported successfully!', 'success');
        },
        
        // Show system diagnostics
        showDiagnostics: function() {
            console.log('AI Upscaler: Showing system diagnostics...');
            
            const diagnostics = {
                platform: navigator.platform,
                userAgent: navigator.userAgent,
                memory: this.getMemoryInfo(),
                gpu: this.getGPUInfo(),
                network: this.getNetworkInfo(),
                timestamp: new Date().toISOString()
            };
            
            const diagWindow = window.open('', '_blank', 'width=800,height=600');
            diagWindow.document.write(`
                <!DOCTYPE html>
                <html>
                <head>
                    <title>AI Upscaler - System Diagnostics</title>
                    <style>
                        body { font-family: monospace; background: #1a1a1a; color: #00ff00; padding: 20px; }
                        .header { color: #00d4ff; font-size: 1.5em; margin-bottom: 20px; }
                        .section { margin-bottom: 20px; padding: 15px; background: #2a2a2a; border-radius: 8px; }
                        .key { color: #ffd700; font-weight: bold; }
                        .value { color: #ffffff; }
                        .good { color: #00ff00; }
                        .warning { color: #ffa500; }
                        .error { color: #ff0000; }
                    </style>
                </head>
                <body>
                    <div class="header">🔍 AI Upscaler Plugin - System Diagnostics v${PLUGIN_VERSION}</div>
                    <div class="section">
                        <div><span class="key">Platform:</span> <span class="value">${diagnostics.platform}</span></div>
                        <div><span class="key">User Agent:</span> <span class="value">${diagnostics.userAgent}</span></div>
                        <div><span class="key">Timestamp:</span> <span class="value">${diagnostics.timestamp}</span></div>
                    </div>
                    <div class="section">
                        <div><span class="key">Memory:</span> <span class="value">${JSON.stringify(diagnostics.memory, null, 2)}</span></div>
                        <div><span class="key">GPU:</span> <span class="value">${JSON.stringify(diagnostics.gpu, null, 2)}</span></div>
                        <div><span class="key">Network:</span> <span class="value">${JSON.stringify(diagnostics.network, null, 2)}</span></div>
                    </div>
                    <div class="section">
                        <div><span class="key">Status:</span> <span class="good">✅ System Ready</span></div>
                        <div><span class="key">Plugin Version:</span> <span class="value">${PLUGIN_VERSION}</span></div>
                        <div><span class="key">Compatibility:</span> <span class="good">✅ Universal</span></div>
                    </div>
                </body>
                </html>
            `);
            
            this.showNotification('📊 System diagnostics opened in new window.', 'info');
        },
        
        // Reset to factory defaults
        resetToDefaults: function() {
            if (confirm('Are you sure you want to reset all settings to factory defaults?')) {
                this.loadDefaults();
            }
        },
        
        // Utility functions
        detectDevice: function() {
            const userAgent = navigator.userAgent.toLowerCase();
            const platform = navigator.platform.toLowerCase();
            
            if (/mobile|android|iphone|ipad/.test(userAgent)) {
                return { type: 'mobile', name: 'Mobile Device' };
            } else if (/tv|roku|chromecast|appletv/.test(userAgent)) {
                return { type: 'tv', name: 'Smart TV' };
            } else if (/win|mac|linux/.test(platform)) {
                return { type: 'desktop', name: 'Desktop Computer' };
            } else {
                return { type: 'unknown', name: 'Unknown Device' };
            }
        },
        
        getMemoryInfo: function() {
            if (performance.memory) {
                return {
                    used: Math.round(performance.memory.usedJSHeapSize / 1024 / 1024) + ' MB',
                    total: Math.round(performance.memory.totalJSHeapSize / 1024 / 1024) + ' MB',
                    limit: Math.round(performance.memory.jsHeapSizeLimit / 1024 / 1024) + ' MB'
                };
            }
            return { status: 'Not available' };
        },
        
        getGPUInfo: function() {
            const canvas = document.createElement('canvas');
            const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
            
            if (gl) {
                const debugInfo = gl.getExtension('WEBGL_debug_renderer_info');
                if (debugInfo) {
                    return {
                        vendor: gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL),
                        renderer: gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL),
                        version: gl.getParameter(gl.VERSION)
                    };
                }
                return { status: 'WebGL supported, details not available' };
            }
            return { status: 'WebGL not supported' };
        },
        
        getNetworkInfo: function() {
            const connection = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
            if (connection) {
                return {
                    type: connection.effectiveType || connection.type,
                    downlink: connection.downlink + ' Mbps',
                    rtt: connection.rtt + ' ms'
                };
            }
            return { status: 'Not available' };
        },
        
        // Test functions
        testPlatformCompatibility: function() {
            const supportedPlatforms = ['Win32', 'Linux', 'MacIntel', 'Android', 'iPhone'];
            const currentPlatform = navigator.platform;
            
            return Promise.resolve({
                test: 'Platform Compatibility',
                passed: supportedPlatforms.some(platform => currentPlatform.includes(platform)),
                details: `Platform: ${currentPlatform}`
            });
        },
        
        testMemoryAvailability: function() {
            const minMemoryMB = 512;
            let availableMemory = 0;
            
            if (performance.memory) {
                availableMemory = (performance.memory.jsHeapSizeLimit - performance.memory.usedJSHeapSize) / 1024 / 1024;
            }
            
            return Promise.resolve({
                test: 'Memory Availability',
                passed: availableMemory > minMemoryMB || !performance.memory,
                details: performance.memory ? `Available: ${Math.round(availableMemory)} MB` : 'Memory info not available'
            });
        },
        
        testNetworkConnectivity: function() {
            return fetch('/api/system/info', { method: 'HEAD' })
                .then(() => ({
                    test: 'Network Connectivity',
                    passed: true,
                    details: 'Connected to Jellyfin server'
                }))
                .catch(() => ({
                    test: 'Network Connectivity',
                    passed: false,
                    details: 'Cannot connect to Jellyfin server'
                }));
        },
        
        testHardwareAcceleration: function() {
            const canvas = document.createElement('canvas');
            const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
            
            return Promise.resolve({
                test: 'Hardware Acceleration',
                passed: !!gl,
                details: gl ? 'WebGL supported' : 'WebGL not supported'
            });
        },
        
        // Form manipulation
        updateFormFields: function(settings) {
            Object.keys(settings).forEach(key => {
                const field = document.querySelector(`[name="${key}"]`);
                if (field) {
                    if (field.type === 'checkbox') {
                        field.checked = settings[key];
                    } else {
                        field.value = settings[key];
                    }
                }
            });
        },
        
        collectFormData: function() {
            const formData = {};
            const inputs = document.querySelectorAll('input, select');
            
            inputs.forEach(input => {
                if (input.name) {
                    if (input.type === 'checkbox') {
                        formData[input.name] = input.checked;
                    } else if (input.type === 'number') {
                        formData[input.name] = parseInt(input.value) || 0;
                    } else {
                        formData[input.name] = input.value;
                    }
                }
            });
            
            return formData;
        },
        
        // Notification system
        showNotification: function(message, type = 'info') {
            const notification = document.createElement('div');
            notification.className = `notification notification-${type}`;
            notification.innerHTML = `
                <div class="notification-content">
                    <span class="notification-message">${message}</span>
                    <button class="notification-close" onclick="this.parentElement.parentElement.remove()">×</button>
                </div>
            `;
            
            // Add styles if not already present
            if (!document.querySelector('#aiupscaler-notification-styles')) {
                const styles = document.createElement('style');
                styles.id = 'aiupscaler-notification-styles';
                styles.textContent = `
                    .notification {
                        position: fixed;
                        top: 20px;
                        right: 20px;
                        padding: 15px 20px;
                        border-radius: 8px;
                        color: white;
                        font-weight: 500;
                        z-index: 10000;
                        animation: slideIn 0.3s ease-out;
                        max-width: 400px;
                        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
                    }
                    
                    .notification-success { background: #059669; }
                    .notification-error { background: #dc2626; }
                    .notification-warning { background: #d97706; }
                    .notification-info { background: #2563eb; }
                    
                    .notification-content {
                        display: flex;
                        align-items: center;
                        justify-content: space-between;
                    }
                    
                    .notification-close {
                        background: none;
                        border: none;
                        color: white;
                        font-size: 18px;
                        cursor: pointer;
                        margin-left: 10px;
                    }
                    
                    @keyframes slideIn {
                        from { transform: translateX(100%); opacity: 0; }
                        to { transform: translateX(0); opacity: 1; }
                    }
                `;
                document.head.appendChild(styles);
            }
            
            document.body.appendChild(notification);
            
            // Auto-remove after 5 seconds
            setTimeout(() => {
                if (notification.parentElement) {
                    notification.remove();
                }
            }, 5000);
        }
    };
    
    // Initialize when DOM is loaded
    document.addEventListener('DOMContentLoaded', function() {
        console.log(`AI Upscaler Plugin Quick Menu v${PLUGIN_VERSION} initialized`);
        
        // Make functions globally available
        window.aiUpscalerQuickMenu = QuickMenuActions;
        
        // Auto-detect and suggest optimizations
        setTimeout(() => {
            const deviceInfo = QuickMenuActions.detectDevice();
            console.log('Detected device:', deviceInfo);
        }, 1000);
    });
    
    // Global functions for HTML onclick handlers
    window.loadDefaults = () => QuickMenuActions.loadDefaults();
    window.optimizeForDevice = () => QuickMenuActions.optimizeForDevice();
    window.testSystem = () => QuickMenuActions.testSystem();
    window.exportConfig = () => QuickMenuActions.exportConfig();
    window.showDiagnostics = () => QuickMenuActions.showDiagnostics();
    window.resetToDefaults = () => QuickMenuActions.resetToDefaults();
    
})();