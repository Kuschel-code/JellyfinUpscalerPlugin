// AI Upscaler Plugin - Sidebar Integration v1.6.1.16
// Adds sidebar menu item and quick-access panel
(function() {
    'use strict';

    const PLUGIN_VERSION = '1.6.1.16';
    var _observer = null;
    var _observerAttached = false;

    // Text escaping helper for safe innerHTML (matches configurationpage.html esc())
    function esc(s) { if (!s) return ''; var d = document.createElement('div'); d.textContent = String(s); return d.innerHTML; }

    // Add sidebar menu item for AI Upscaler Plugin
    function addSidebarItem() {
        var navDrawer = document.querySelector('.navDrawer-scrollContainer');
        if (!navDrawer || document.querySelector('#aiUpscalerSidebarItem')) {
            return;
        }

        // Create the sidebar item
        var sidebarItem = document.createElement('a');
        sidebarItem.id = 'aiUpscalerSidebarItem';
        sidebarItem.className = 'navMenuOption lnkMediaFolder';
        sidebarItem.href = '#';
        sidebarItem.setAttribute('data-itemid', 'aiupscaler');

        var iconSpan = document.createElement('span');
        iconSpan.className = 'navMenuOptionIcon material-icons';
        iconSpan.textContent = 'smart_display';
        sidebarItem.appendChild(iconSpan);

        var textSpan = document.createElement('span');
        textSpan.className = 'navMenuOptionText';
        textSpan.textContent = 'AI Upscaler';
        sidebarItem.appendChild(textSpan);

        // Find the right position (after Dashboard, before other plugins)
        var dashboardItem = navDrawer.querySelector('a[href="#/dashboard.html"]');
        var parentNode = dashboardItem ? dashboardItem.parentNode : navDrawer;

        if (dashboardItem && dashboardItem.nextSibling) {
            parentNode.insertBefore(sidebarItem, dashboardItem.nextSibling);
        } else {
            parentNode.appendChild(sidebarItem);
        }

        // Add click handler
        sidebarItem.addEventListener('click', function(e) {
            e.preventDefault();
            showUpscalerPanel();
        });

        console.log('AI Upscaler: Sidebar item added successfully');
    }

    // Show the AI Upscaler settings panel
    function showUpscalerPanel() {
        // Remove existing panel if present
        var existingPanel = document.querySelector('#aiUpscalerPanel');
        if (existingPanel) {
            existingPanel.remove();
        }

        // Create main panel
        var panel = document.createElement('div');
        panel.id = 'aiUpscalerPanel';
        panel.className = 'page libraryPage backdropPage pageWithAbsoluteTabs withTabs';
        panel.style.cssText = 'position:fixed;top:0;left:0;right:0;bottom:0;background:var(--theme-body-bg);z-index:1000;overflow-y:auto;';

        panel.innerHTML = '<div class="pageHeader">' +
            '<div class="pageHeaderContent">' +
            '<button type="button" class="headerBackButton" id="aiUpscalerBack">' +
            '<span class="material-icons">arrow_back</span>' +
            '</button>' +
            '<h1 class="pageTitle">AI Upscaler Plugin v' + esc(PLUGIN_VERSION) + '</h1>' +
            '</div></div>' +
            '<div class="pageContainer"><div class="content-primary">' +

            '<!-- System Status -->' +
            '<div class="verticalSection">' +
            '<h2 class="sectionTitle">System Status</h2>' +
            '<div class="cardBox visualCardBox" style="margin:1em 0;">' +
            '<div class="cardText" id="systemStatus">' +
            '<div>Status: <span id="pluginStatus" style="color:#00d4ff;">Loading...</span></div>' +
            '<div>Version: ' + esc(PLUGIN_VERSION) + '</div>' +
            '<div>Hardware: <span id="hardwareInfo">Detecting...</span></div>' +
            '<div>AI Service: <span id="serviceInfo">Checking...</span></div>' +
            '</div></div></div>' +

            '<!-- Quick Actions -->' +
            '<div class="verticalSection">' +
            '<h2 class="sectionTitle">Quick Actions</h2>' +
            '<div style="display:flex;gap:1em;flex-wrap:wrap;margin:1em 0;">' +
            '<button type="button" class="raised button-submit" id="runBenchmarkBtn"><span>Run Hardware Test</span></button>' +
            '<button type="button" class="raised button-submit" id="autoOptimizeBtn"><span>Auto-Optimize</span></button>' +
            '<button type="button" class="raised button-submit" id="clearCacheBtn"><span>Clear Cache</span></button>' +
            '<button type="button" class="raised button-submit" id="openSettingsBtn"><span>Open Settings</span></button>' +
            '</div></div>' +

            '<!-- Benchmark Console -->' +
            '<div class="verticalSection">' +
            '<h2 class="sectionTitle">Benchmark Console</h2>' +
            '<div class="cardBox visualCardBox" style="margin:1em 0;">' +
            '<div style="background:#1a1a1a;color:#00ff00;font-family:Courier New,monospace;padding:1em;border-radius:4px;height:300px;overflow-y:auto;font-size:12px;" id="benchmarkConsole">' +
            '</div>' +
            '<div style="margin-top:0.5em;">' +
            '<input type="text" class="emby-input" id="consoleCommandInput" placeholder="Enter command (benchmark, status, optimize, clear, help)" style="width:100%;">' +
            '</div></div></div>' +

            '<!-- Hardware Information -->' +
            '<div class="verticalSection">' +
            '<h2 class="sectionTitle">Hardware Information</h2>' +
            '<div class="cardBox visualCardBox" style="margin:1em 0;">' +
            '<div class="cardText" id="hardwareDetails">' +
            '<div><strong>CPU Cores:</strong> <span id="cpuInfo">Detecting...</span></div>' +
            '<div><strong>GPU:</strong> <span id="gpuInfo">Detecting...</span></div>' +
            '<div><strong>Platform:</strong> <span id="platformInfo">Detecting...</span></div>' +
            '<div><strong>Providers:</strong> <span id="providersInfo">Detecting...</span></div>' +
            '<div><strong>Recommended Model:</strong> <span id="recommendedModel">Analyzing...</span></div>' +
            '</div></div></div>' +

            '<!-- Cache & Jobs -->' +
            '<div class="verticalSection">' +
            '<h2 class="sectionTitle">Live Stats</h2>' +
            '<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:1em;margin:1em 0;">' +
            '<div class="cardBox visualCardBox"><div class="cardText">' +
            '<div><strong>Active Jobs</strong></div>' +
            '<div style="font-size:24px;color:#00d4ff;" id="activeJobs">--</div>' +
            '</div></div>' +
            '<div class="cardBox visualCardBox"><div class="cardText">' +
            '<div><strong>Service Status</strong></div>' +
            '<div style="font-size:24px;color:#00d4ff;" id="serviceStatus">--</div>' +
            '</div></div>' +
            '<div class="cardBox visualCardBox"><div class="cardText">' +
            '<div><strong>GPU Active</strong></div>' +
            '<div style="font-size:24px;color:#00d4ff;" id="gpuActive">--</div>' +
            '</div></div>' +
            '<div class="cardBox visualCardBox"><div class="cardText">' +
            '<div><strong>Cache Size</strong></div>' +
            '<div style="font-size:24px;color:#00d4ff;" id="cacheSize">--</div>' +
            '</div></div>' +
            '</div></div>' +

            '</div></div>';

        document.body.appendChild(panel);

        // Initialize console with safe DOM
        resetConsole();

        // Add event handlers
        setupPanelHandlers();
        loadSystemInfo();
        startLiveMonitoring();
    }

    // Reset console to initial state (safe DOM, no innerHTML)
    function resetConsole() {
        var consoleEl = document.getElementById('benchmarkConsole');
        if (!consoleEl) return;
        consoleEl.textContent = '';
        appendConsoleLine(consoleEl, 'AI Upscaler Plugin v' + PLUGIN_VERSION + ' - Benchmark Console');
        appendConsoleLine(consoleEl, 'Ready for hardware testing...');
        appendConsoleLine(consoleEl, "Type 'help' for available commands");
        appendConsolePrompt(consoleEl);
    }

    var MAX_CONSOLE_LINES = 200;

    function appendConsoleLine(consoleEl, text, color) {
        var div = document.createElement('div');
        div.textContent = text;
        if (color) div.style.color = color;
        consoleEl.appendChild(div);
        // Cap console lines to prevent unbounded DOM growth
        while (consoleEl.children.length > MAX_CONSOLE_LINES) {
            consoleEl.removeChild(consoleEl.firstChild);
        }
    }

    function appendConsolePrompt(consoleEl) {
        var div = document.createElement('div');
        div.style.marginTop = '1em';
        var prompt = document.createElement('span');
        prompt.style.color = '#ffff00';
        prompt.textContent = 'upscaler@jellyfin:~$ ';
        div.appendChild(prompt);
        var input = document.createElement('span');
        input.id = 'consoleInput';
        div.appendChild(input);
        consoleEl.appendChild(div);
    }

    // Setup event handlers for the panel
    function setupPanelHandlers() {
        // Back button
        document.getElementById('aiUpscalerBack').addEventListener('click', function() {
            stopLiveMonitoring();
            document.getElementById('aiUpscalerPanel').remove();
        });

        // Quick action buttons
        document.getElementById('runBenchmarkBtn').addEventListener('click', runBenchmark);
        document.getElementById('autoOptimizeBtn').addEventListener('click', autoOptimize);
        document.getElementById('clearCacheBtn').addEventListener('click', clearCache);
        document.getElementById('openSettingsBtn').addEventListener('click', openSettings);

        // Console input
        var consoleInput = document.getElementById('consoleCommandInput');
        consoleInput.addEventListener('keypress', function(e) {
            if (e.key === 'Enter') {
                executeConsoleCommand(this.value);
                this.value = '';
            }
        });
    }

    // Console command execution
    function executeConsoleCommand(command) {
        var consoleEl = document.getElementById('benchmarkConsole');
        var cmd = command.toLowerCase().trim();

        // Add command to console (safe DOM)
        var commandLine = document.createElement('div');
        var prompt = document.createElement('span');
        prompt.style.color = '#ffff00';
        prompt.textContent = 'upscaler@jellyfin:~$ ';
        commandLine.appendChild(prompt);
        commandLine.appendChild(document.createTextNode(command));
        consoleEl.appendChild(commandLine);

        switch(cmd) {
            case 'benchmark':
                appendConsoleLine(consoleEl, 'Starting hardware benchmark...');
                runBenchmark();
                break;
            case 'status':
                appendConsoleLine(consoleEl, 'Fetching status...');
                if (!window.ApiClient) { appendConsoleLine(consoleEl, 'Error: ApiClient not available', '#ff0000'); break; }
                ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/status')).then(function(data) {
                    appendConsoleLine(consoleEl, 'Plugin Status: ' + (data.status || 'Active'));
                    appendConsoleLine(consoleEl, 'Version: ' + PLUGIN_VERSION);
                    appendConsoleLine(consoleEl, 'Processing: ' + (data.isProcessing ? 'Yes' : 'No'));
                    consoleEl.scrollTop = consoleEl.scrollHeight;
                });
                break;
            case 'optimize':
                appendConsoleLine(consoleEl, 'Running auto-optimization...');
                autoOptimize();
                break;
            case 'clear':
                resetConsole();
                return;
            case 'help':
                appendConsoleLine(consoleEl, 'Available commands:');
                appendConsoleLine(consoleEl, '- benchmark: Run hardware test');
                appendConsoleLine(consoleEl, '- status: Show plugin status');
                appendConsoleLine(consoleEl, '- optimize: Auto-optimize settings');
                appendConsoleLine(consoleEl, '- clear: Clear console');
                appendConsoleLine(consoleEl, '- help: Show this help');
                break;
            default:
                appendConsoleLine(consoleEl, "Unknown command: " + command + ". Type 'help' for available commands");
        }

        // Auto-scroll to bottom
        consoleEl.scrollTop = consoleEl.scrollHeight;
    }

    // Load system information from real API endpoints
    // Note: API calls rely on browser/fetch default timeouts; no explicit timeout is set.
    function loadSystemInfo() {
        if (!window.ApiClient) {
            console.warn('AI Upscaler: ApiClient not available yet');
            return;
        }

        // Fetch hardware info
        ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/recommendations')).then(function(data) {
            if (data && data.success) {
                var statusEl = document.getElementById('pluginStatus');
                if (statusEl) { statusEl.textContent = 'Active'; statusEl.style.color = '#00c853'; }

                var hw = data.hardware;
                if (hw) {
                    var cpuEl = document.getElementById('cpuInfo');
                    if (cpuEl) cpuEl.textContent = hw.cpuCores + ' Cores';
                    var gpuEl = document.getElementById('gpuInfo');
                    if (gpuEl) gpuEl.textContent = hw.cudaAvailable ? 'NVIDIA (CUDA)' :
                        hw.directMlAvailable ? 'AMD/Intel (DirectML)' : 'CPU Only';
                    var platEl = document.getElementById('platformInfo');
                    if (platEl) platEl.textContent = data.system ? String(data.system.platform) : 'Unknown';
                    var provEl = document.getElementById('providersInfo');
                    if (provEl) provEl.textContent =
                        (hw.availableProviders && hw.availableProviders.length > 0) ?
                        hw.availableProviders.join(', ') : 'CPU';
                }

                var rec = data.recommendations;
                if (rec) {
                    var recEl = document.getElementById('recommendedModel');
                    if (recEl) recEl.textContent = rec.recommendedModel || 'fsrcnn-x2';
                }
            }
        }).catch(function(error) {
            console.error('Failed to load system info:', error);
            var statusEl = document.getElementById('pluginStatus');
            if (statusEl) { statusEl.textContent = 'Error'; statusEl.style.color = '#ff5252'; }
        });

        // Check AI service health via backend proxy
        ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/service-health')).then(function(data) {
            var svcEl = document.getElementById('serviceInfo');
            if (svcEl) {
                if (data && data.success) {
                    svcEl.textContent = 'Connected';
                    svcEl.style.color = '#00c853';
                } else {
                    svcEl.textContent = 'Unavailable';
                    svcEl.style.color = '#ff5252';
                }
            }
        }).catch(function() {
            var svcEl = document.getElementById('serviceInfo');
            if (svcEl) { svcEl.textContent = 'Unreachable'; svcEl.style.color = '#ff5252'; }
        });
    }

    // Live monitoring interval
    var _monitorInterval = null;

    function startLiveMonitoring() {
        updateLiveStats();
        _monitorInterval = setInterval(updateLiveStats, 10000);
    }

    function stopLiveMonitoring() {
        if (_monitorInterval) {
            clearInterval(_monitorInterval);
            _monitorInterval = null;
        }
    }

    function updateLiveStats() {
        if (!window.ApiClient) return;

        // Jobs
        ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/jobs')).then(function(data) {
            if (data && data.jobs) {
                var active = data.jobs.filter(function(j) { return j.status === 'Processing'; }).length;
                var el = document.getElementById('activeJobs');
                if (el) el.textContent = active;
            }
        }).catch(function() {});

        // Cache stats
        ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/cache/stats')).then(function(data) {
            if (data) {
                var el = document.getElementById('cacheSize');
                if (el) {
                    var mb = ((data.totalSize || 0) / 1024 / 1024).toFixed(1);
                    el.textContent = mb + ' MB';
                }
            }
        }).catch(function() {});

        // Fallback status (service + GPU)
        ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/fallback')).then(function(data) {
            if (data) {
                var svcEl = document.getElementById('serviceStatus');
                if (svcEl) {
                    svcEl.textContent = data.serviceAvailable ? 'Online' : 'Offline';
                    svcEl.style.color = data.serviceAvailable ? '#00c853' : '#ff5252';
                }
                var gpuEl = document.getElementById('gpuActive');
                if (gpuEl) {
                    gpuEl.textContent = data.usingGpu ? 'Yes' : 'No';
                    gpuEl.style.color = data.usingGpu ? '#00c853' : '#ffab00';
                }
            }
        }).catch(function() {});
    }

    // Quick action functions
    function runBenchmark() {
        if (!window.ApiClient) return;
        var consoleEl = document.getElementById('benchmarkConsole');

        if (consoleEl) appendConsoleLine(consoleEl, 'Starting hardware benchmark via API...');

        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('api/Upscaler/benchmark'),
            contentType: 'application/json'
        }).then(function(data) {
            if (data && data.success) {
                var hw = data.hardware || {};
                var rec = data.recommendations || {};
                var sys = data.system || {};
                if (consoleEl) {
                    appendConsoleLine(consoleEl, '');
                    appendConsoleLine(consoleEl, 'Benchmark Results:', '#00ff00');
                    appendConsoleLine(consoleEl, '- Platform: ' + String(sys.platform || 'Unknown'), '#00ff00');
                    appendConsoleLine(consoleEl, '- CPU Cores: ' + String(hw.cpuCores || '?'), '#00ff00');
                    appendConsoleLine(consoleEl, '- GPU: ' + (hw.cudaAvailable ? 'NVIDIA CUDA' : hw.directMlAvailable ? 'DirectML' : 'CPU'), '#00ff00');
                    appendConsoleLine(consoleEl, '- Recommended Model: ' + String(rec.recommendedModel || 'fsrcnn-x2'), '#00ff00');
                    appendConsoleLine(consoleEl, '- Recommended Quality: ' + String(rec.recommendedQuality || 'medium'), '#00ff00');
                }
            } else {
                if (consoleEl) appendConsoleLine(consoleEl, 'Benchmark returned no data', '#ff0000');
            }
            if (consoleEl) consoleEl.scrollTop = consoleEl.scrollHeight;
        }).catch(function(error) {
            if (consoleEl) appendConsoleLine(consoleEl, 'API Error: ' + error, '#ff0000');
            if (consoleEl) consoleEl.scrollTop = consoleEl.scrollHeight;
        });
    }

    function autoOptimize() {
        if (!window.ApiClient) { showToast('ApiClient not available'); return; }
        // Call recommendations endpoint, then apply optimal settings
        ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/recommendations')).then(function(data) {
            if (data && data.success && data.recommendations) {
                var rec = data.recommendations;
                // Update plugin config with recommended settings
                ApiClient.getPluginConfiguration('f87f700e-679d-43e6-9c7c-b3a410dc3f22').then(function(config) {
                    config.Model = rec.recommendedModel || config.Model;
                    config.QualityLevel = rec.recommendedQuality || config.QualityLevel;
                    config.MaxConcurrentStreams = rec.maxConcurrentStreams || config.MaxConcurrentStreams;
                    config.HardwareAcceleration = rec.hardwareAcceleration !== undefined ? rec.hardwareAcceleration : config.HardwareAcceleration;
                    ApiClient.updatePluginConfiguration('f87f700e-679d-43e6-9c7c-b3a410dc3f22', config).then(function() {
                        showToast('Settings auto-optimized: ' + String(rec.recommendedModel || 'default') + ' / ' + String(rec.recommendedQuality || 'medium'));
                    });
                });
            } else {
                showToast('Could not fetch recommendations');
            }
        }).catch(function() {
            showToast('Auto-optimize failed - check AI service connection');
        });
    }

    function clearCache() {
        if (!window.ApiClient) { showToast('ApiClient not available'); return; }
        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('api/Upscaler/cache/clear'),
            contentType: 'application/json'
        }).then(function() {
            showToast('Cache cleared successfully');
            updateLiveStats();
        }).catch(function() {
            showToast('Failed to clear cache');
        });
    }

    function openSettings() {
        // Navigate to plugin config page within Jellyfin SPA
        window.location.hash = '/configurationpage?name=' + encodeURIComponent('AI Upscaler Plugin');
        var panel = document.getElementById('aiUpscalerPanel');
        if (panel) {
            stopLiveMonitoring();
            panel.remove();
        }
    }

    function showToast(message) {
        if (typeof require !== 'undefined') {
            require(['toast'], function(toast) {
                toast(message);
            });
        }
    }

    // Initialize when DOM is ready
    function init() {
        // Wait for Jellyfin UI to load
        if (typeof require === 'undefined' || !document.querySelector('.navDrawer-scrollContainer')) {
            setTimeout(init, 1000);
            return;
        }

        addSidebarItem();

        // Re-add item when navigation changes (with guard against duplicate observers)
        if (!_observerAttached) {
            _observer = new MutationObserver(function(mutations) {
                mutations.forEach(function(mutation) {
                    if (mutation.type === 'childList' && !document.querySelector('#aiUpscalerSidebarItem')) {
                        setTimeout(addSidebarItem, 500);
                    }
                });
            });

            var navDrawer = document.querySelector('.navDrawer-scrollContainer');
            if (navDrawer) {
                _observer.observe(navDrawer, { childList: true, subtree: true });
                _observerAttached = true;
            }
        }
    }

    // Cleanup function to release all resources
    function cleanup() {
        stopLiveMonitoring();
        if (_observer) {
            _observer.disconnect();
            _observer = null;
            _observerAttached = false;
        }
        var panel = document.getElementById('aiUpscalerPanel');
        if (panel) panel.remove();
    }

    // Expose cleanup for external use (e.g., plugin unload)
    window._aiUpscalerSidebarCleanup = cleanup;

    // Start initialization
    init();

    console.log('AI Upscaler Plugin: Sidebar integration v' + PLUGIN_VERSION + ' loaded');
})();
