// AI Upscaler Plugin - Sidebar Integration v1.5.2.7
// Adds sidebar menu item and quick-access panel
(function() {
    'use strict';

    const PLUGIN_VERSION = '1.5.2.7';

    // Add sidebar menu item for AI Upscaler Plugin
    function addSidebarItem() {
        const navDrawer = document.querySelector('.navDrawer-scrollContainer');
        if (!navDrawer || document.querySelector('#aiUpscalerSidebarItem')) {
            return;
        }

        // Create the sidebar item
        const sidebarItem = document.createElement('a');
        sidebarItem.id = 'aiUpscalerSidebarItem';
        sidebarItem.className = 'navMenuOption lnkMediaFolder';
        sidebarItem.href = '#';
        sidebarItem.setAttribute('data-itemid', 'aiupscaler');

        sidebarItem.innerHTML = `
            <span class="navMenuOptionIcon material-icons">smart_display</span>
            <span class="navMenuOptionText">AI Upscaler</span>
        `;

        // Find the right position (after Dashboard, before other plugins)
        const dashboardItem = navDrawer.querySelector('a[href="#/dashboard.html"]');
        const parentNode = dashboardItem ? dashboardItem.parentNode : navDrawer;

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
        const existingPanel = document.querySelector('#aiUpscalerPanel');
        if (existingPanel) {
            existingPanel.remove();
        }

        // Create main panel
        const panel = document.createElement('div');
        panel.id = 'aiUpscalerPanel';
        panel.className = 'page libraryPage backdropPage pageWithAbsoluteTabs withTabs';
        panel.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: var(--theme-body-bg);
            z-index: 1000;
            overflow-y: auto;
        `;

        panel.innerHTML = `
            <div class="pageHeader">
                <div class="pageHeaderContent">
                    <button type="button" class="headerBackButton" id="aiUpscalerBack">
                        <span class="material-icons">arrow_back</span>
                    </button>
                    <h1 class="pageTitle">AI Upscaler Plugin v${PLUGIN_VERSION}</h1>
                </div>
            </div>

            <div class="pageContainer">
                <div class="content-primary">

                    <!-- System Status -->
                    <div class="verticalSection">
                        <h2 class="sectionTitle">System Status</h2>
                        <div class="cardBox visualCardBox" style="margin: 1em 0;">
                            <div class="cardText" id="systemStatus">
                                <div>Status: <span id="pluginStatus" style="color: #00a4dc;">Loading...</span></div>
                                <div>Version: ${PLUGIN_VERSION}</div>
                                <div>Hardware: <span id="hardwareInfo">Detecting...</span></div>
                                <div>AI Service: <span id="serviceInfo">Checking...</span></div>
                            </div>
                        </div>
                    </div>

                    <!-- Quick Actions -->
                    <div class="verticalSection">
                        <h2 class="sectionTitle">Quick Actions</h2>
                        <div style="display: flex; gap: 1em; flex-wrap: wrap; margin: 1em 0;">
                            <button type="button" class="raised button-submit" id="runBenchmarkBtn">
                                <span>Run Hardware Test</span>
                            </button>
                            <button type="button" class="raised button-submit" id="autoOptimizeBtn">
                                <span>Auto-Optimize</span>
                            </button>
                            <button type="button" class="raised button-submit" id="clearCacheBtn">
                                <span>Clear Cache</span>
                            </button>
                            <button type="button" class="raised button-submit" id="openSettingsBtn">
                                <span>Open Settings</span>
                            </button>
                        </div>
                    </div>

                    <!-- Benchmark Console -->
                    <div class="verticalSection">
                        <h2 class="sectionTitle">Benchmark Console</h2>
                        <div class="cardBox visualCardBox" style="margin: 1em 0;">
                            <div style="background: #1a1a1a; color: #00ff00; font-family: 'Courier New', monospace; padding: 1em; border-radius: 4px; height: 300px; overflow-y: auto; font-size: 12px;" id="benchmarkConsole">
                                <div>AI Upscaler Plugin v${PLUGIN_VERSION} - Benchmark Console</div>
                                <div>Ready for hardware testing...</div>
                                <div>Type 'help' for available commands</div>
                                <div style="margin-top: 1em;">
                                    <span style="color: #ffff00;">upscaler@jellyfin:~$</span> <span id="consoleInput"></span>
                                </div>
                            </div>
                            <div style="margin-top: 0.5em;">
                                <input type="text" class="emby-input" id="consoleCommandInput" placeholder="Enter command (benchmark, status, optimize, clear, help)" style="width: 100%;">
                            </div>
                        </div>
                    </div>

                    <!-- Hardware Information -->
                    <div class="verticalSection">
                        <h2 class="sectionTitle">Hardware Information</h2>
                        <div class="cardBox visualCardBox" style="margin: 1em 0;">
                            <div class="cardText" id="hardwareDetails">
                                <div><strong>CPU Cores:</strong> <span id="cpuInfo">Detecting...</span></div>
                                <div><strong>GPU:</strong> <span id="gpuInfo">Detecting...</span></div>
                                <div><strong>Platform:</strong> <span id="platformInfo">Detecting...</span></div>
                                <div><strong>Providers:</strong> <span id="providersInfo">Detecting...</span></div>
                                <div><strong>Recommended Model:</strong> <span id="recommendedModel">Analyzing...</span></div>
                            </div>
                        </div>
                    </div>

                    <!-- Cache & Jobs -->
                    <div class="verticalSection">
                        <h2 class="sectionTitle">Live Stats</h2>
                        <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1em; margin: 1em 0;">
                            <div class="cardBox visualCardBox">
                                <div class="cardText">
                                    <div><strong>Active Jobs</strong></div>
                                    <div style="font-size: 24px; color: #00a4dc;" id="activeJobs">--</div>
                                </div>
                            </div>
                            <div class="cardBox visualCardBox">
                                <div class="cardText">
                                    <div><strong>Service Status</strong></div>
                                    <div style="font-size: 24px; color: #00a4dc;" id="serviceStatus">--</div>
                                </div>
                            </div>
                            <div class="cardBox visualCardBox">
                                <div class="cardText">
                                    <div><strong>GPU Active</strong></div>
                                    <div style="font-size: 24px; color: #00a4dc;" id="gpuActive">--</div>
                                </div>
                            </div>
                            <div class="cardBox visualCardBox">
                                <div class="cardText">
                                    <div><strong>Cache Size</strong></div>
                                    <div style="font-size: 24px; color: #00a4dc;" id="cacheSize">--</div>
                                </div>
                            </div>
                        </div>
                    </div>

                </div>
            </div>
        `;

        document.body.appendChild(panel);

        // Add event handlers
        setupPanelHandlers();
        loadSystemInfo();
        startLiveMonitoring();
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

        // Add command to console
        var commandLine = document.createElement('div');
        var prompt = document.createElement('span');
        prompt.style.color = '#ffff00';
        prompt.textContent = 'upscaler@jellyfin:~$ ';
        commandLine.appendChild(prompt);
        commandLine.appendChild(document.createTextNode(command));
        consoleEl.appendChild(commandLine);

        var response = document.createElement('div');

        switch(cmd) {
            case 'benchmark':
                response.textContent = 'Starting hardware benchmark...';
                consoleEl.appendChild(response);
                runBenchmark();
                break;
            case 'status':
                response.textContent = 'Fetching status...';
                consoleEl.appendChild(response);
                ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/status')).then(function(data) {
                    var statusDiv = document.createElement('div');
                    statusDiv.innerHTML = 'Plugin Status: ' + (data.status || 'Active') + '<br>' +
                        'Version: ' + PLUGIN_VERSION + '<br>' +
                        'Processing: ' + (data.isProcessing ? 'Yes' : 'No');
                    consoleEl.appendChild(statusDiv);
                    consoleEl.scrollTop = consoleEl.scrollHeight;
                });
                break;
            case 'optimize':
                response.textContent = 'Running auto-optimization...';
                consoleEl.appendChild(response);
                autoOptimize();
                break;
            case 'clear':
                consoleEl.innerHTML = '<div>AI Upscaler Plugin v' + PLUGIN_VERSION + ' - Benchmark Console</div>' +
                    '<div>Console cleared</div>' +
                    '<div style="margin-top: 1em;">' +
                    '<span style="color: #ffff00;">upscaler@jellyfin:~$</span>' +
                    '</div>';
                return;
            case 'help':
                response.innerHTML = 'Available commands:<br>' +
                    '- benchmark: Run hardware test<br>' +
                    '- status: Show plugin status<br>' +
                    '- optimize: Auto-optimize settings<br>' +
                    '- clear: Clear console<br>' +
                    '- help: Show this help';
                consoleEl.appendChild(response);
                break;
            default:
                response.textContent = 'Unknown command: ' + command + ". Type 'help' for available commands";
                consoleEl.appendChild(response);
        }

        // Auto-scroll to bottom
        consoleEl.scrollTop = consoleEl.scrollHeight;
    }

    // Load system information from real API endpoints
    function loadSystemInfo() {
        // Fetch hardware info
        ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/recommendations')).then(function(data) {
            if (data && data.success) {
                document.getElementById('pluginStatus').textContent = 'Active';
                document.getElementById('pluginStatus').style.color = '#00c853';

                var hw = data.hardware;
                if (hw) {
                    document.getElementById('cpuInfo').textContent = hw.cpuCores + ' Cores';
                    document.getElementById('gpuInfo').textContent = hw.cudaAvailable ? 'NVIDIA (CUDA)' :
                        hw.directMlAvailable ? 'AMD/Intel (DirectML)' : 'CPU Only';
                    document.getElementById('platformInfo').textContent = data.system ? data.system.platform : 'Unknown';
                    document.getElementById('providersInfo').textContent =
                        (hw.availableProviders && hw.availableProviders.length > 0) ?
                        hw.availableProviders.join(', ') : 'CPU';
                }

                var rec = data.recommendations;
                if (rec) {
                    document.getElementById('recommendedModel').textContent = rec.recommendedModel || 'fsrcnn-x2';
                }
            }
        }).catch(function(error) {
            console.error('Failed to load system info:', error);
            document.getElementById('pluginStatus').textContent = 'Error';
            document.getElementById('pluginStatus').style.color = '#ff5252';
        });

        // Check AI service health via backend proxy
        ApiClient.getJSON(ApiClient.getUrl('api/Upscaler/service-health')).then(function(data) {
            if (data && data.success) {
                document.getElementById('serviceInfo').textContent = 'Connected';
                document.getElementById('serviceInfo').style.color = '#00c853';
            } else {
                document.getElementById('serviceInfo').textContent = 'Unavailable';
                document.getElementById('serviceInfo').style.color = '#ff5252';
            }
        }).catch(function() {
            document.getElementById('serviceInfo').textContent = 'Unreachable';
            document.getElementById('serviceInfo').style.color = '#ff5252';
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
        var consoleEl = document.getElementById('benchmarkConsole');

        var startMsg = document.createElement('div');
        startMsg.textContent = 'Starting hardware benchmark via API...';
        if (consoleEl) consoleEl.appendChild(startMsg);

        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('api/Upscaler/benchmark'),
            contentType: 'application/json'
        }).then(function(data) {
            if (data && data.success) {
                var hw = data.hardware || {};
                var rec = data.recommendations || {};
                var sys = data.system || {};
                var resultDiv = document.createElement('div');
                resultDiv.innerHTML = '<div style="color: #00ff00; margin-top: 1em;">' +
                    'Benchmark Results:<br>' +
                    '- Platform: ' + (sys.platform || 'Unknown') + '<br>' +
                    '- CPU Cores: ' + (hw.cpuCores || '?') + '<br>' +
                    '- GPU: ' + (hw.cudaAvailable ? 'NVIDIA CUDA' : hw.directMlAvailable ? 'DirectML' : 'CPU') + '<br>' +
                    '- Recommended Model: ' + (rec.recommendedModel || 'fsrcnn-x2') + '<br>' +
                    '- Recommended Quality: ' + (rec.recommendedQuality || 'medium') +
                    '</div>';
                if (consoleEl) consoleEl.appendChild(resultDiv);
            } else {
                var errorDiv = document.createElement('div');
                errorDiv.textContent = 'Benchmark returned no data';
                errorDiv.style.color = '#ff0000';
                if (consoleEl) consoleEl.appendChild(errorDiv);
            }
            if (consoleEl) consoleEl.scrollTop = consoleEl.scrollHeight;
        }).catch(function(error) {
            var errorDiv = document.createElement('div');
            errorDiv.textContent = 'API Error: ' + error;
            errorDiv.style.color = '#ff0000';
            if (consoleEl) consoleEl.appendChild(errorDiv);
            if (consoleEl) consoleEl.scrollTop = consoleEl.scrollHeight;
        });
    }

    function autoOptimize() {
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
                        showToast('Settings auto-optimized: ' + (rec.recommendedModel || 'default') + ' / ' + (rec.recommendedQuality || 'medium'));
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

        // Re-add item when navigation changes
        var observer = new MutationObserver(function(mutations) {
            mutations.forEach(function(mutation) {
                if (mutation.type === 'childList' && !document.querySelector('#aiUpscalerSidebarItem')) {
                    setTimeout(addSidebarItem, 500);
                }
            });
        });

        var navDrawer = document.querySelector('.navDrawer-scrollContainer');
        if (navDrawer) {
            observer.observe(navDrawer, { childList: true, subtree: true });
        }
    }

    // Start initialization
    init();

    console.log('AI Upscaler Plugin: Sidebar integration v' + PLUGIN_VERSION + ' loaded');
})();
