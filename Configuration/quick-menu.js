// AI Upscaler Plugin - Enhanced Quick Menu v1.5.2.8
// Advanced compatibility and quick actions

(function() {
    'use strict';

    // Plugin configuration
    const PLUGIN_ID = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';
    const PLUGIN_VERSION = '1.5.2.8';

    // Quick menu actions
    const QuickMenuActions = {

        // Load optimal defaults based on device
        loadDefaults: function() {
            console.log('AI Upscaler: Loading optimal defaults...');

            const defaults = {
                EnablePlugin: true,
                Model: 'realesrgan-x4',
                ScaleFactor: 2,
                QualityLevel: 'medium',
                HardwareAcceleration: true,
                MaxConcurrentStreams: 2,
                CacheSizeMB: 1024,
                EnablePreProcessingCache: true,
                EnablePerformanceMetrics: true,
                EnableAutoBenchmarking: false,
                MaxCacheAgeDays: 30
            };

            this.updateFormFields(defaults);
            this.showNotification('Default settings loaded successfully!', 'success');
        },

        // Auto-optimize for current device
        optimizeForDevice: function() {
            console.log('AI Upscaler: Auto-optimizing for device...');

            var self = this;
            var deviceInfo = this.detectDevice();

            // First try to get server-side recommendations
            if (window.ApiClient) {
                ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/recommendations')).then(function(data) {
                    if (data && data.success && data.recommendations) {
                        var rec = data.recommendations;
                        var settings = {
                            Model: rec.recommendedModel || 'fsrcnn-x2',
                            QualityLevel: rec.recommendedQuality || 'medium',
                            MaxConcurrentStreams: rec.maxConcurrentStreams || 2,
                            HardwareAcceleration: rec.hardwareAcceleration || false
                        };
                        self.updateFormFields(settings);
                        self.showNotification('Settings optimized based on server hardware!', 'success');
                    } else {
                        self._applyDeviceDefaults(deviceInfo);
                    }
                }).catch(function() {
                    self._applyDeviceDefaults(deviceInfo);
                });
            } else {
                this._applyDeviceDefaults(deviceInfo);
            }
        },

        _applyDeviceDefaults: function(deviceInfo) {
            var optimizedSettings = {};

            switch(deviceInfo.type) {
                case 'mobile':
                    optimizedSettings = {
                        Model: 'espcn-x2',
                        CacheSizeMB: 512,
                        MaxConcurrentStreams: 1,
                        HardwareAcceleration: false,
                        QualityLevel: 'low'
                    };
                    break;

                case 'desktop':
                    optimizedSettings = {
                        Model: 'realesrgan-x4',
                        CacheSizeMB: 2048,
                        MaxConcurrentStreams: 4,
                        HardwareAcceleration: true,
                        QualityLevel: 'high'
                    };
                    break;

                case 'tv':
                    optimizedSettings = {
                        Model: 'fsrcnn-x2',
                        CacheSizeMB: 1024,
                        MaxConcurrentStreams: 2,
                        HardwareAcceleration: true,
                        QualityLevel: 'medium'
                    };
                    break;

                default:
                    optimizedSettings = {
                        Model: 'fsrcnn-x2',
                        CacheSizeMB: 1024,
                        MaxConcurrentStreams: 2,
                        HardwareAcceleration: true,
                        QualityLevel: 'medium'
                    };
            }

            this.updateFormFields(optimizedSettings);
            this.showNotification('Settings optimized for ' + deviceInfo.type + ' device!', 'success');
        },

        // Test system compatibility
        testSystem: function() {
            console.log('AI Upscaler: Testing system compatibility...');
            this.showNotification('Testing system compatibility...', 'info');

            var self = this;
            var tests = [
                this.testPlatformCompatibility(),
                this.testMemoryAvailability(),
                this.testNetworkConnectivity(),
                this.testHardwareAcceleration()
            ];

            Promise.all(tests)
                .then(function(results) {
                    var allPassed = results.every(function(result) { return result.passed; });
                    if (allPassed) {
                        self.showNotification('All system tests passed! Plugin is ready.', 'success');
                    } else {
                        var failures = results.filter(function(r) { return !r.passed; });
                        self.showNotification(failures.length + ' test(s) failed. Check console for details.', 'warning');
                    }
                })
                .catch(function(error) {
                    console.error('System test error:', error);
                    self.showNotification('System test failed. Check console for details.', 'error');
                });
        },

        // Export configuration
        exportConfig: function() {
            console.log('AI Upscaler: Exporting configuration...');

            if (window.ApiClient) {
                var self = this;
                ApiClient.getPluginConfiguration(PLUGIN_ID).then(function(config) {
                    var configBlob = new Blob([JSON.stringify(config, null, 2)], {
                        type: 'application/json'
                    });

                    var url = URL.createObjectURL(configBlob);
                    var a = document.createElement('a');
                    a.href = url;
                    a.download = 'aiupscaler-config-' + new Date().toISOString().split('T')[0] + '.json';
                    document.body.appendChild(a);
                    a.click();
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);

                    self.showNotification('Configuration exported successfully!', 'success');
                });
            } else {
                this.showNotification('ApiClient not available', 'error');
            }
        },

        // Show system diagnostics
        showDiagnostics: function() {
            console.log('AI Upscaler: Showing system diagnostics...');

            var diagnostics = {
                platform: navigator.platform,
                userAgent: navigator.userAgent,
                memory: this.getMemoryInfo(),
                gpu: this.getGPUInfo(),
                network: this.getNetworkInfo(),
                timestamp: new Date().toISOString()
            };

            var diagWindow = window.open('', '_blank', 'width=800,height=600');
            if (!diagWindow) { this.showNotification('Popup blocked. Please allow popups.', 'warning'); return; }
            var doc = diagWindow.document;
            doc.open();
            doc.write('<!DOCTYPE html><html><head><title>AI Upscaler - System Diagnostics</title>' +
                '<style>' +
                'body{font-family:monospace;background:#1a1a1a;color:#00ff00;padding:20px;}' +
                '.header{color:#00d4ff;font-size:1.5em;margin-bottom:20px;}' +
                '.section{margin-bottom:20px;padding:15px;background:#2a2a2a;border-radius:8px;}' +
                '.key{color:#ffd700;font-weight:bold;}.value{color:#ffffff;}.good{color:#00ff00;}' +
                '</style></head><body><div id="diag-root"></div></body></html>');
            doc.close();
            // Build content safely via DOM
            var root = doc.getElementById('diag-root');
            function addRow(parent, key, value, valueClass) {
                var row = doc.createElement('div');
                var k = doc.createElement('span'); k.className = 'key'; k.textContent = key + ': ';
                var v = doc.createElement('span'); v.className = valueClass || 'value'; v.textContent = String(value);
                row.appendChild(k); row.appendChild(v); parent.appendChild(row);
            }
            var header = doc.createElement('div'); header.className = 'header';
            header.textContent = 'AI Upscaler Plugin - System Diagnostics v' + PLUGIN_VERSION;
            root.appendChild(header);
            var sec1 = doc.createElement('div'); sec1.className = 'section';
            addRow(sec1, 'Platform', diagnostics.platform);
            addRow(sec1, 'Timestamp', diagnostics.timestamp);
            root.appendChild(sec1);
            var sec2 = doc.createElement('div'); sec2.className = 'section';
            addRow(sec2, 'Memory', JSON.stringify(diagnostics.memory));
            addRow(sec2, 'GPU', JSON.stringify(diagnostics.gpu));
            addRow(sec2, 'Network', JSON.stringify(diagnostics.network));
            root.appendChild(sec2);
            var sec3 = doc.createElement('div'); sec3.className = 'section';
            addRow(sec3, 'Status', 'System Ready', 'good');
            addRow(sec3, 'Plugin Version', PLUGIN_VERSION);
            root.appendChild(sec3);

            this.showNotification('System diagnostics opened in new window.', 'info');
        },

        // Reset to factory defaults
        resetToDefaults: function() {
            if (confirm('Are you sure you want to reset all settings to factory defaults?')) {
                this.loadDefaults();
            }
        },

        // Utility functions
        detectDevice: function() {
            var userAgent = navigator.userAgent.toLowerCase();
            var platform = navigator.platform.toLowerCase();

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
            var canvas = document.createElement('canvas');
            var gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');

            if (gl) {
                var debugInfo = gl.getExtension('WEBGL_debug_renderer_info');
                var result;
                if (debugInfo) {
                    result = {
                        vendor: gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL),
                        renderer: gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL),
                        version: gl.getParameter(gl.VERSION)
                    };
                } else {
                    result = { status: 'WebGL supported, details not available' };
                }
                // Release WebGL context to free GPU resources
                var loseCtx = gl.getExtension('WEBGL_lose_context');
                if (loseCtx) loseCtx.loseContext();
                return result;
            }
            return { status: 'WebGL not supported' };
        },

        getNetworkInfo: function() {
            var connection = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
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
            var supportedPlatforms = ['Win32', 'Linux', 'MacIntel', 'Android', 'iPhone'];
            var currentPlatform = navigator.platform;

            return Promise.resolve({
                test: 'Platform Compatibility',
                passed: supportedPlatforms.some(function(platform) { return currentPlatform.includes(platform); }),
                details: 'Platform: ' + currentPlatform
            });
        },

        testMemoryAvailability: function() {
            var minMemoryMB = 512;
            var availableMemory = 0;

            if (performance.memory) {
                availableMemory = (performance.memory.jsHeapSizeLimit - performance.memory.usedJSHeapSize) / 1024 / 1024;
            }

            return Promise.resolve({
                test: 'Memory Availability',
                passed: availableMemory > minMemoryMB || !performance.memory,
                details: performance.memory ? 'Available: ' + Math.round(availableMemory) + ' MB' : 'Memory info not available'
            });
        },

        testNetworkConnectivity: function() {
            return fetch('/api/system/info', { method: 'HEAD' })
                .then(function() {
                    return {
                        test: 'Network Connectivity',
                        passed: true,
                        details: 'Connected to Jellyfin server'
                    };
                })
                .catch(function() {
                    return {
                        test: 'Network Connectivity',
                        passed: false,
                        details: 'Cannot connect to Jellyfin server'
                    };
                });
        },

        testHardwareAcceleration: function() {
            var canvas = document.createElement('canvas');
            var gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
            var supported = !!gl;

            // Release WebGL context to free GPU resources
            if (gl) {
                var loseCtx = gl.getExtension('WEBGL_lose_context');
                if (loseCtx) loseCtx.loseContext();
            }

            return Promise.resolve({
                test: 'Hardware Acceleration',
                passed: supported,
                details: supported ? 'WebGL supported' : 'WebGL not supported'
            });
        },

        // Form manipulation
        updateFormFields: function(settings) {
            Object.keys(settings).forEach(function(key) {
                var field = document.querySelector('[name="' + key + '"]') || document.getElementById(key);
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
            var formData = {};
            var inputs = document.querySelectorAll('input, select');

            inputs.forEach(function(input) {
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
        showNotification: function(message, type) {
            type = type || 'info';
            var notification = document.createElement('div');
            notification.className = 'notification notification-' + type;

            var content = document.createElement('div');
            content.className = 'notification-content';

            var msgSpan = document.createElement('span');
            msgSpan.className = 'notification-message';
            msgSpan.textContent = message;
            content.appendChild(msgSpan);

            var closeBtn = document.createElement('button');
            closeBtn.className = 'notification-close';
            closeBtn.textContent = 'x';
            closeBtn.addEventListener('click', function() { notification.remove(); });
            content.appendChild(closeBtn);

            notification.appendChild(content);

            // Add styles if not already present
            if (!document.querySelector('#aiupscaler-notification-styles')) {
                var styles = document.createElement('style');
                styles.id = 'aiupscaler-notification-styles';
                styles.textContent =
                    '.notification { position: fixed; top: 20px; right: 20px; padding: 15px 20px; border-radius: 8px; color: white; font-weight: 500; z-index: 10000; animation: slideIn 0.3s ease-out; max-width: 400px; box-shadow: 0 4px 12px rgba(0,0,0,0.3); }' +
                    '.notification-success { background: #059669; }' +
                    '.notification-error { background: #dc2626; }' +
                    '.notification-warning { background: #d97706; }' +
                    '.notification-info { background: #2563eb; }' +
                    '.notification-content { display: flex; align-items: center; justify-content: space-between; }' +
                    '.notification-close { background: none; border: none; color: white; font-size: 18px; cursor: pointer; margin-left: 10px; }' +
                    '@keyframes slideIn { from { transform: translateX(100%); opacity: 0; } to { transform: translateX(0); opacity: 1; } }';
                document.head.appendChild(styles);
            }

            document.body.appendChild(notification);

            // Auto-remove after 5 seconds
            setTimeout(function() {
                if (notification.parentElement) {
                    notification.remove();
                }
            }, 5000);
        }
    };

    // Initialize when DOM is loaded
    function onDomReady() {
        console.log('AI Upscaler Plugin Quick Menu v' + PLUGIN_VERSION + ' initialized');

        // Make functions globally available
        window.aiUpscalerQuickMenu = QuickMenuActions;

        // Auto-detect and suggest optimizations
        setTimeout(function() {
            var deviceInfo = QuickMenuActions.detectDevice();
            console.log('Detected device:', deviceInfo);
        }, 1000);
    }

    if (document.readyState === 'interactive' || document.readyState === 'complete') {
        onDomReady();
    } else {
        document.addEventListener('DOMContentLoaded', onDomReady);
    }

    // Global functions for HTML onclick handlers
    window.loadDefaults = function() { QuickMenuActions.loadDefaults(); };
    window.optimizeForDevice = function() { QuickMenuActions.optimizeForDevice(); };
    window.testSystem = function() { QuickMenuActions.testSystem(); };
    window.exportConfig = function() { QuickMenuActions.exportConfig(); };
    window.showDiagnostics = function() { QuickMenuActions.showDiagnostics(); };
    window.resetToDefaults = function() { QuickMenuActions.resetToDefaults(); };

})();
