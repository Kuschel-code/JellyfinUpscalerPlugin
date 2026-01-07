// AI Upscaler Plugin - Sidebar Integration v1.4.0
// Creates sidebar panel like Playback Reporting Plugin

(function() {
    'use strict';
    
    // Plugin configuration
    const PLUGIN_ID = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';
    const PLUGIN_VERSION = '1.4.0';
    
    // Sidebar Integration Manager
    const SidebarIntegration = {
        
        // Initialize sidebar integration
        init: function() {
            console.log('AI Upscaler: Initializing sidebar integration v1.4.0...');
            
            // Wait for Jellyfin to be ready
            this.waitForJellyfin();
        },
        
        // Wait for Jellyfin dashboard to be available
        waitForJellyfin: function() {
            const checkJellyfin = () => {
                try {
                    if (window.Dashboard && window.ApiClient) {
                        console.log('AI Upscaler: Jellyfin dashboard detected, integrating sidebar...');
                        this.integrateSidebar();
                    } else {
                        setTimeout(checkJellyfin, 1000);
                    }
                } catch (error) {
                    console.error('AI Upscaler: Error waiting for Jellyfin:', error);
                    setTimeout(checkJellyfin, 2000);
                }
            };
            checkJellyfin();
        },
        
        // Integrate with Jellyfin sidebar
        integrateSidebar: function() {
            try {
                // Add CSS for sidebar styling
                this.addSidebarStyles();
                
                // Wait for sidebar to be available
                this.waitForSidebar();
                
            } catch (error) {
                console.error('AI Upscaler: Error integrating sidebar:', error);
            }
        },
        
        // Wait for sidebar navigation to be available
        waitForSidebar: function() {
            let attempts = 0;
            const maxAttempts = 50;
            
            const checkSidebar = () => {
                attempts++;
                
                try {
                    const sidebar = document.querySelector('.mainDrawer-scrollContainer') || 
                                  document.querySelector('.navDrawer-scrollContainer') ||
                                  document.querySelector('.mainDrawerButton') ||
                                  document.querySelector('.navMenuOption');
                    
                    if (sidebar) {
                        console.log('AI Upscaler: Sidebar found, adding menu item...');
                        this.addSidebarMenuItem();
                    } else if (attempts < maxAttempts) {
                        setTimeout(checkSidebar, 500);
                    } else {
                        console.warn('AI Upscaler: Could not find sidebar after', maxAttempts, 'attempts');
                        // Try alternative method
                        this.addMenuItemAlternative();
                    }
                } catch (error) {
                    console.error('AI Upscaler: Error checking for sidebar:', error);
                    if (attempts < maxAttempts) {
                        setTimeout(checkSidebar, 1000);
                    }
                }
            };
            
            checkSidebar();
        },
        
        // Add sidebar menu item
        addSidebarMenuItem: function() {
            try {
                // Find sidebar container
                const sidebarContainer = document.querySelector('.mainDrawer-scrollContainer') || 
                                       document.querySelector('.navDrawer-scrollContainer') ||
                                       document.querySelector('.navMenuContent');
                
                if (!sidebarContainer) {
                    console.warn('AI Upscaler: Sidebar container not found');
                    return;
                }
                
                // Check if our menu item already exists
                if (document.querySelector('#ai-upscaler-sidebar-item')) {
                    console.log('AI Upscaler: Sidebar menu item already exists');
                    return;
                }
                
                // Create menu item
                const menuItem = this.createSidebarMenuItem();
                
                // Find insertion point (after Plugins or Libraries)
                const insertionPoint = this.findInsertionPoint(sidebarContainer);
                
                if (insertionPoint) {
                    insertionPoint.parentNode.insertBefore(menuItem, insertionPoint.nextSibling);
                } else {
                    sidebarContainer.appendChild(menuItem);
                }
                
                console.log('AI Upscaler: Sidebar menu item added successfully');
                
            } catch (error) {
                console.error('AI Upscaler: Error adding sidebar menu item:', error);
            }
        },
        
        // Create sidebar menu item HTML
        createSidebarMenuItem: function() {
            const menuItem = document.createElement('a');
            menuItem.id = 'ai-upscaler-sidebar-item';
            menuItem.className = 'navMenuOption';
            menuItem.href = '#';
            
            menuItem.innerHTML = `
                <div class="navMenuOptionIcon">
                    <span class="material-icons">🎮</span>
                </div>
                <div class="navMenuOptionText">AI Upscaler</div>
            `;
            
            // Add click handler
            menuItem.addEventListener('click', (e) => {
                e.preventDefault();
                this.showUpscalerPanel();
            });
            
            return menuItem;
        },
        
        // Find insertion point in sidebar
        findInsertionPoint: function(container) {
            // Look for Plugins, Libraries, or similar items
            const navItems = container.querySelectorAll('.navMenuOption');
            
            for (let item of navItems) {
                const text = item.textContent.toLowerCase();
                if (text.includes('plugin') || text.includes('libraries') || text.includes('settings')) {
                    return item;
                }
            }
            
            // If not found, return last item
            return navItems[navItems.length - 1];
        },
        
        // Alternative method to add menu item
        addMenuItemAlternative: function() {
            try {
                // Try to add to dashboard navigation
                setTimeout(() => {
                    this.addDashboardMenuItem();
                }, 2000);
            } catch (error) {
                console.error('AI Upscaler: Alternative menu integration failed:', error);
            }
        },
        
        // Add menu item to dashboard
        addDashboardMenuItem: function() {
            try {
                // Create floating action button if sidebar integration fails
                const fab = document.createElement('div');
                fab.id = 'ai-upscaler-fab';
                fab.className = 'ai-upscaler-fab';
                fab.innerHTML = '🎮';
                fab.title = 'AI Upscaler Settings';
                
                fab.addEventListener('click', () => {
                    this.showUpscalerPanel();
                });
                
                document.body.appendChild(fab);
                
                console.log('AI Upscaler: Floating action button added as fallback');
                
            } catch (error) {
                console.error('AI Upscaler: Error adding dashboard menu item:', error);
            }
        },
        
        // Show upscaler settings panel
        showUpscalerPanel: function() {
            try {
                console.log('AI Upscaler: Opening settings panel...');
                
                // Remove existing panel
                const existingPanel = document.getElementById('ai-upscaler-panel');
                if (existingPanel) {
                    existingPanel.remove();
                }
                
                // Create settings panel
                const panel = this.createSettingsPanel();
                document.body.appendChild(panel);
                
                // Load current settings
                this.loadCurrentSettings();
                
                // Initialize panel functionality
                this.initializePanelFeatures();
                
            } catch (error) {
                console.error('AI Upscaler: Error showing upscaler panel:', error);
            }
        },
        
        // Create settings panel HTML
        createSettingsPanel: function() {
            const panel = document.createElement('div');
            panel.id = 'ai-upscaler-panel';
            panel.className = 'ai-upscaler-panel';
            
            panel.innerHTML = `
                <div class="ai-upscaler-panel-overlay" onclick="window.SidebarIntegration.closePanel()"></div>
                <div class="ai-upscaler-panel-content">
                    <div class="ai-upscaler-panel-header">
                        <h2>🎮 AI Upscaler Settings</h2>
                        <button class="ai-upscaler-close-btn" onclick="window.SidebarIntegration.closePanel()">×</button>
                    </div>
                    
                    <div class="ai-upscaler-panel-body">
                        <!-- Status Section -->
                        <div class="ai-upscaler-section">
                            <h3>📊 System Status</h3>
                            <div id="system-status" class="status-grid">
                                <div class="status-item">
                                    <span class="status-label">Plugin Status:</span>
                                    <span class="status-value" id="plugin-status">Loading...</span>
                                </div>
                                <div class="status-item">
                                    <span class="status-label">Hardware:</span>
                                    <span class="status-value" id="hardware-status">Detecting...</span>
                                </div>
                                <div class="status-item">
                                    <span class="status-label">Performance:</span>
                                    <span class="status-value" id="performance-status">Analyzing...</span>
                                </div>
                            </div>
                        </div>
                        
                        <!-- Quick Settings -->
                        <div class="ai-upscaler-section">
                            <h3>⚡ Quick Settings</h3>
                            <div class="settings-grid">
                                <div class="setting-item">
                                    <label for="quick-enable">Enable AI Upscaling:</label>
                                    <input type="checkbox" id="quick-enable" checked>
                                </div>
                                <div class="setting-item">
                                    <label for="quick-model">AI Model:</label>
                                    <select id="quick-model">
                                        <option value="fsrcnn">FSRCNN (Balanced)</option>
                                        <option value="fsrcnn-light">FSRCNN Light (Fast)</option>
                                        <option value="esrgan">ESRGAN (Quality)</option>
                                        <option value="realesrgan">Real-ESRGAN (Best)</option>
                                        <option value="waifu2x">Waifu2x (Anime)</option>
                                    </select>
                                </div>
                                <div class="setting-item">
                                    <label for="quick-scale">Scale Factor:</label>
                                    <select id="quick-scale">
                                        <option value="2">2x (Recommended)</option>
                                        <option value="3">3x</option>
                                        <option value="4">4x</option>
                                    </select>
                                </div>
                                <div class="setting-item">
                                    <label for="quick-quality">Quality:</label>
                                    <select id="quick-quality">
                                        <option value="performance">Performance</option>
                                        <option value="balanced" selected>Balanced</option>
                                        <option value="quality">Quality</option>
                                    </select>
                                </div>
                            </div>
                        </div>
                        
                        <!-- Hardware Benchmark -->
                        <div class="ai-upscaler-section">
                            <h3>🔬 Hardware Benchmark</h3>
                            <div class="benchmark-controls">
                                <button id="run-benchmark-btn" class="btn btn-primary">
                                    Run Hardware Test
                                </button>
                                <button id="view-results-btn" class="btn btn-secondary">
                                    View Results
                                </button>
                                <button id="auto-optimize-btn" class="btn btn-success">
                                    Auto-Optimize
                                </button>
                            </div>
                            <div id="benchmark-console" class="benchmark-console" style="display: none;">
                                <div class="console-header">Benchmark Console</div>
                                <div id="console-output" class="console-output"></div>
                            </div>
                        </div>
                        
                        <!-- Advanced Features -->
                        <div class="ai-upscaler-section">
                            <h3>🚀 Advanced Features</h3>
                            <div class="advanced-grid">
                                <div class="feature-item">
                                    <input type="checkbox" id="enable-cache">
                                    <label for="enable-cache">Pre-processing Cache</label>
                                </div>
                                <div class="feature-item">
                                    <input type="checkbox" id="enable-fallback">
                                    <label for="enable-fallback">Auto Fallback</label>
                                </div>
                                <div class="feature-item">
                                    <input type="checkbox" id="enable-comparison">
                                    <label for="enable-comparison">Comparison View</label>
                                </div>
                                <div class="feature-item">
                                    <input type="checkbox" id="enable-tv-optimization">
                                    <label for="enable-tv-optimization">TV Remote Support</label>
                                </div>
                            </div>
                        </div>
                    </div>
                    
                    <div class="ai-upscaler-panel-footer">
                        <button id="save-settings-btn" class="btn btn-primary">Save Settings</button>
                        <button id="reset-settings-btn" class="btn btn-secondary">Reset to Defaults</button>
                        <button onclick="window.SidebarIntegration.closePanel()" class="btn btn-cancel">Cancel</button>
                    </div>
                </div>
            `;
            
            return panel;
        },
        
        // Initialize panel features
        initializePanelFeatures: function() {
            try {
                // Benchmark button
                const benchmarkBtn = document.getElementById('run-benchmark-btn');
                if (benchmarkBtn) {
                    benchmarkBtn.addEventListener('click', () => {
                        this.runBenchmark();
                    });
                }
                
                // Auto-optimize button
                const optimizeBtn = document.getElementById('auto-optimize-btn');
                if (optimizeBtn) {
                    optimizeBtn.addEventListener('click', () => {
                        this.autoOptimize();
                    });
                }
                
                // Save settings button
                const saveBtn = document.getElementById('save-settings-btn');
                if (saveBtn) {
                    saveBtn.addEventListener('click', () => {
                        this.saveSettings();
                    });
                }
                
                // Reset settings button
                const resetBtn = document.getElementById('reset-settings-btn');
                if (resetBtn) {
                    resetBtn.addEventListener('click', () => {
                        this.resetSettings();
                    });
                }
                
                // View results button
                const resultsBtn = document.getElementById('view-results-btn');
                if (resultsBtn) {
                    resultsBtn.addEventListener('click', () => {
                        this.viewBenchmarkResults();
                    });
                }
                
            } catch (error) {
                console.error('AI Upscaler: Error initializing panel features:', error);
            }
        },
        
        // Load current settings
        loadCurrentSettings: function() {
            try {
                // Make API call to get current settings
                if (window.ApiClient) {
                    window.ApiClient.getJSON('/api/upscaler/status')
                        .then(response => {
                            this.updateStatusDisplay(response);
                        })
                        .catch(error => {
                            console.error('AI Upscaler: Error loading settings:', error);
                        });
                }
                
                // Get hardware recommendations
                if (window.ApiClient) {
                    window.ApiClient.getJSON('/api/upscaler/recommendations')
                        .then(response => {
                            this.updateRecommendations(response);
                        })
                        .catch(error => {
                            console.error('AI Upscaler: Error loading recommendations:', error);
                        });
                }
                
            } catch (error) {
                console.error('AI Upscaler: Error in loadCurrentSettings:', error);
            }
        },
        
        // Update status display
        updateStatusDisplay: function(status) {
            try {
                const pluginStatusEl = document.getElementById('plugin-status');
                const hardwareStatusEl = document.getElementById('hardware-status');
                const performanceStatusEl = document.getElementById('performance-status');
                
                if (pluginStatusEl) {
                    pluginStatusEl.textContent = status.enabled ? 'Active' : 'Disabled';
                    pluginStatusEl.className = 'status-value ' + (status.enabled ? 'status-active' : 'status-inactive');
                }
                
                if (hardwareStatusEl) {
                    hardwareStatusEl.textContent = status.hardwareAcceleration ? 'GPU Enabled' : 'CPU Only';
                    hardwareStatusEl.className = 'status-value ' + (status.hardwareAcceleration ? 'status-active' : 'status-warning');
                }
                
                if (performanceStatusEl) {
                    performanceStatusEl.textContent = status.performance || 'Good';
                    performanceStatusEl.className = 'status-value status-active';
                }
                
            } catch (error) {
                console.error('AI Upscaler: Error updating status display:', error);
            }
        },
        
        // Run hardware benchmark
        runBenchmark: function() {
            try {
                console.log('AI Upscaler: Starting hardware benchmark...');
                
                const benchmarkBtn = document.getElementById('run-benchmark-btn');
                const consoleEl = document.getElementById('benchmark-console');
                const outputEl = document.getElementById('console-output');
                
                if (benchmarkBtn) {
                    benchmarkBtn.disabled = true;
                    benchmarkBtn.textContent = 'Running Benchmark...';
                }
                
                if (consoleEl) {
                    consoleEl.style.display = 'block';
                }
                
                if (outputEl) {
                    outputEl.innerHTML = '<div class="console-line">Starting hardware benchmark...</div>';
                }
                
                // Make API call to run benchmark
                if (window.ApiClient) {
                    const addConsoleOutput = (message) => {
                        if (outputEl) {
                            outputEl.innerHTML += `<div class="console-line">${new Date().toLocaleTimeString()}: ${message}</div>`;
                            outputEl.scrollTop = outputEl.scrollHeight;
                        }
                    };
                    
                    addConsoleOutput('Detecting system hardware...');
                    
                    setTimeout(() => {
                        addConsoleOutput('Testing AI models performance...');
                    }, 1000);
                    
                    setTimeout(() => {
                        addConsoleOutput('Benchmarking resolution scaling...');
                    }, 2000);
                    
                    setTimeout(() => {
                        window.ApiClient.ajax({
                            type: 'POST',
                            url: '/api/upscaler/benchmark',
                            dataType: 'json'
                        })
                        .then(response => {
                            addConsoleOutput('Benchmark completed successfully!');
                            addConsoleOutput(`Total duration: ${response.results.duration.toFixed(1)}s`);
                            addConsoleOutput(`Recommended model: ${response.results.optimalSettings.RecommendedModel}`);
                            addConsoleOutput(`Recommended resolution: ${response.results.optimalSettings.RecommendedMaxResolution}`);
                            
                            if (benchmarkBtn) {
                                benchmarkBtn.disabled = false;
                                benchmarkBtn.textContent = 'Run Hardware Test';
                            }
                        })
                        .catch(error => {
                            addConsoleOutput('Benchmark failed: ' + error.message);
                            console.error('AI Upscaler: Benchmark failed:', error);
                            
                            if (benchmarkBtn) {
                                benchmarkBtn.disabled = false;
                                benchmarkBtn.textContent = 'Run Hardware Test';
                            }
                        });
                    }, 3000);
                }
                
            } catch (error) {
                console.error('AI Upscaler: Error running benchmark:', error);
            }
        },
        
        // Auto-optimize settings
        autoOptimize: function() {
            try {
                console.log('AI Upscaler: Auto-optimizing settings...');
                
                if (window.ApiClient) {
                    window.ApiClient.getJSON('/api/upscaler/recommendations')
                        .then(response => {
                            // Apply recommended settings
                            const modelSelect = document.getElementById('quick-model');
                            const qualitySelect = document.getElementById('quick-quality');
                            
                            if (modelSelect && response.recommended.model) {
                                modelSelect.value = response.recommended.model;
                            }
                            
                            if (qualitySelect && response.recommended.quality) {
                                qualitySelect.value = response.recommended.quality;
                            }
                            
                            // Show notification
                            this.showNotification('Settings optimized for your hardware!', 'success');
                        })
                        .catch(error => {
                            console.error('AI Upscaler: Error auto-optimizing:', error);
                            this.showNotification('Auto-optimization failed', 'error');
                        });
                }
                
            } catch (error) {
                console.error('AI Upscaler: Error in autoOptimize:', error);
            }
        },
        
        // Save settings
        saveSettings: function() {
            try {
                console.log('AI Upscaler: Saving settings...');
                
                const settings = {
                    enabled: document.getElementById('quick-enable')?.checked,
                    model: document.getElementById('quick-model')?.value,
                    scale: parseInt(document.getElementById('quick-scale')?.value),
                    quality: document.getElementById('quick-quality')?.value,
                    enableCache: document.getElementById('enable-cache')?.checked,
                    enableFallback: document.getElementById('enable-fallback')?.checked,
                    enableComparison: document.getElementById('enable-comparison')?.checked,
                    enableTVOptimization: document.getElementById('enable-tv-optimization')?.checked
                };
                
                if (window.ApiClient) {
                    window.ApiClient.ajax({
                        type: 'POST',
                        url: '/api/upscaler/settings',
                        data: JSON.stringify(settings),
                        contentType: 'application/json',
                        dataType: 'json'
                    })
                    .then(response => {
                        this.showNotification('Settings saved successfully!', 'success');
                    })
                    .catch(error => {
                        console.error('AI Upscaler: Error saving settings:', error);
                        this.showNotification('Failed to save settings', 'error');
                    });
                }
                
            } catch (error) {
                console.error('AI Upscaler: Error saving settings:', error);
            }
        },
        
        // Reset settings to defaults
        resetSettings: function() {
            try {
                // Reset form fields
                const quickEnable = document.getElementById('quick-enable');
                const quickModel = document.getElementById('quick-model');
                const quickScale = document.getElementById('quick-scale');
                const quickQuality = document.getElementById('quick-quality');
                
                if (quickEnable) quickEnable.checked = true;
                if (quickModel) quickModel.value = 'fsrcnn';
                if (quickScale) quickScale.value = '2';
                if (quickQuality) quickQuality.value = 'balanced';
                
                this.showNotification('Settings reset to defaults', 'info');
                
            } catch (error) {
                console.error('AI Upscaler: Error resetting settings:', error);
            }
        },
        
        // View benchmark results
        viewBenchmarkResults: function() {
            try {
                console.log('AI Upscaler: Opening benchmark results...');
                
                // In a real implementation, this would open a detailed results view
                this.showNotification('Benchmark results will be displayed here', 'info');
                
            } catch (error) {
                console.error('AI Upscaler: Error viewing benchmark results:', error);
            }
        },
        
        // Show notification
        showNotification: function(message, type = 'info') {
            try {
                // Create notification element
                const notification = document.createElement('div');
                notification.className = `ai-upscaler-notification notification-${type}`;
                notification.textContent = message;
                
                document.body.appendChild(notification);
                
                // Remove after 3 seconds
                setTimeout(() => {
                    if (notification.parentNode) {
                        notification.parentNode.removeChild(notification);
                    }
                }, 3000);
                
            } catch (error) {
                console.error('AI Upscaler: Error showing notification:', error);
            }
        },
        
        // Close panel
        closePanel: function() {
            try {
                const panel = document.getElementById('ai-upscaler-panel');
                if (panel) {
                    panel.remove();
                }
            } catch (error) {
                console.error('AI Upscaler: Error closing panel:', error);
            }
        },
        
        // Add CSS styles
        addSidebarStyles: function() {
            if (document.getElementById('ai-upscaler-sidebar-styles')) {
                return; // Styles already added
            }
            
            const style = document.createElement('style');
            style.id = 'ai-upscaler-sidebar-styles';
            style.textContent = `
                /* AI Upscaler Sidebar Styles */
                .ai-upscaler-fab {
                    position: fixed;
                    bottom: 20px;
                    right: 20px;
                    width: 56px;
                    height: 56px;
                    background: #00a4dc;
                    border-radius: 50%;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    font-size: 24px;
                    color: white;
                    cursor: pointer;
                    box-shadow: 0 4px 8px rgba(0,0,0,0.3);
                    z-index: 9999;
                    transition: all 0.3s ease;
                }
                
                .ai-upscaler-fab:hover {
                    background: #0084b4;
                    transform: scale(1.1);
                }
                
                .ai-upscaler-panel {
                    position: fixed;
                    top: 0;
                    left: 0;
                    width: 100%;
                    height: 100%;
                    z-index: 10000;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                }
                
                .ai-upscaler-panel-overlay {
                    position: absolute;
                    top: 0;
                    left: 0;
                    width: 100%;
                    height: 100%;
                    background: rgba(0,0,0,0.7);
                }
                
                .ai-upscaler-panel-content {
                    position: relative;
                    width: 90%;
                    max-width: 800px;
                    max-height: 90%;
                    background: #1e1e1e;
                    border-radius: 8px;
                    color: #ffffff;
                    overflow: hidden;
                    display: flex;
                    flex-direction: column;
                }
                
                .ai-upscaler-panel-header {
                    padding: 20px;
                    background: #2d2d2d;
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    border-bottom: 1px solid #404040;
                }
                
                .ai-upscaler-panel-header h2 {
                    margin: 0;
                    font-size: 24px;
                    color: #00a4dc;
                }
                
                .ai-upscaler-close-btn {
                    background: none;
                    border: none;
                    color: #ffffff;
                    font-size: 24px;
                    cursor: pointer;
                    padding: 4px 8px;
                    border-radius: 4px;
                    transition: background 0.3s ease;
                }
                
                .ai-upscaler-close-btn:hover {
                    background: #404040;
                }
                
                .ai-upscaler-panel-body {
                    flex: 1;
                    overflow-y: auto;
                    padding: 20px;
                }
                
                .ai-upscaler-section {
                    margin-bottom: 30px;
                }
                
                .ai-upscaler-section h3 {
                    color: #00a4dc;
                    margin-bottom: 15px;
                    font-size: 18px;
                }
                
                .status-grid {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
                    gap: 15px;
                }
                
                .status-item {
                    background: #2d2d2d;
                    padding: 15px;
                    border-radius: 6px;
                    border-left: 4px solid #00a4dc;
                }
                
                .status-label {
                    display: block;
                    font-size: 14px;
                    color: #cccccc;
                    margin-bottom: 5px;
                }
                
                .status-value {
                    font-weight: bold;
                    font-size: 16px;
                }
                
                .status-active {
                    color: #4caf50;
                }
                
                .status-warning {
                    color: #ff9800;
                }
                
                .status-inactive {
                    color: #f44336;
                }
                
                .settings-grid {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
                    gap: 15px;
                }
                
                .setting-item {
                    display: flex;
                    flex-direction: column;
                    gap: 8px;
                }
                
                .setting-item label {
                    color: #cccccc;
                    font-size: 14px;
                }
                
                .setting-item input,
                .setting-item select {
                    background: #2d2d2d;
                    border: 1px solid #404040;
                    color: #ffffff;
                    padding: 8px 12px;
                    border-radius: 4px;
                    font-size: 14px;
                }
                
                .setting-item input:focus,
                .setting-item select:focus {
                    outline: none;
                    border-color: #00a4dc;
                }
                
                .benchmark-controls {
                    display: flex;
                    gap: 10px;
                    flex-wrap: wrap;
                    margin-bottom: 15px;
                }
                
                .btn {
                    padding: 10px 20px;
                    border: none;
                    border-radius: 4px;
                    cursor: pointer;
                    font-size: 14px;
                    transition: all 0.3s ease;
                }
                
                .btn-primary {
                    background: #00a4dc;
                    color: white;
                }
                
                .btn-primary:hover {
                    background: #0084b4;
                }
                
                .btn-secondary {
                    background: #6c757d;
                    color: white;
                }
                
                .btn-secondary:hover {
                    background: #545b62;
                }
                
                .btn-success {
                    background: #28a745;
                    color: white;
                }
                
                .btn-success:hover {
                    background: #218838;
                }
                
                .btn-cancel {
                    background: #6c757d;
                    color: white;
                }
                
                .btn-cancel:hover {
                    background: #545b62;
                }
                
                .benchmark-console {
                    background: #1a1a1a;
                    border: 1px solid #404040;
                    border-radius: 4px;
                    height: 200px;
                    overflow: hidden;
                    display: flex;
                    flex-direction: column;
                }
                
                .console-header {
                    background: #2d2d2d;
                    padding: 8px 12px;
                    font-size: 12px;
                    color: #cccccc;
                    border-bottom: 1px solid #404040;
                }
                
                .console-output {
                    flex: 1;
                    padding: 10px;
                    overflow-y: auto;
                    font-family: 'Courier New', monospace;
                    font-size: 12px;
                    line-height: 1.4;
                }
                
                .console-line {
                    color: #00ff00;
                    margin-bottom: 2px;
                }
                
                .advanced-grid {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
                    gap: 15px;
                }
                
                .feature-item {
                    display: flex;
                    align-items: center;
                    gap: 8px;
                    padding: 10px;
                    background: #2d2d2d;
                    border-radius: 4px;
                }
                
                .feature-item input[type="checkbox"] {
                    width: 18px;
                    height: 18px;
                    margin: 0;
                }
                
                .feature-item label {
                    color: #cccccc;
                    cursor: pointer;
                    margin: 0;
                }
                
                .ai-upscaler-panel-footer {
                    padding: 20px;
                    background: #2d2d2d;
                    border-top: 1px solid #404040;
                    display: flex;
                    gap: 10px;
                    justify-content: flex-end;
                    flex-wrap: wrap;
                }
                
                .ai-upscaler-notification {
                    position: fixed;
                    top: 20px;
                    right: 20px;
                    padding: 12px 20px;
                    border-radius: 4px;
                    color: white;
                    font-size: 14px;
                    z-index: 10001;
                    opacity: 0;
                    animation: slideInNotification 0.3s ease forwards;
                }
                
                .notification-success {
                    background: #28a745;
                }
                
                .notification-error {
                    background: #dc3545;
                }
                
                .notification-info {
                    background: #17a2b8;
                }
                
                @keyframes slideInNotification {
                    from {
                        opacity: 0;
                        transform: translateX(100%);
                    }
                    to {
                        opacity: 1;
                        transform: translateX(0);
                    }
                }
                
                /* Mobile responsiveness */
                @media (max-width: 768px) {
                    .ai-upscaler-panel-content {
                        width: 95%;
                        height: 95%;
                    }
                    
                    .status-grid,
                    .settings-grid,
                    .advanced-grid {
                        grid-template-columns: 1fr;
                    }
                    
                    .benchmark-controls {
                        flex-direction: column;
                    }
                    
                    .ai-upscaler-panel-footer {
                        flex-direction: column;
                    }
                }
            `;
            
            document.head.appendChild(style);
        }
    };
    
    // Make closePanel globally available
    window.SidebarIntegration = SidebarIntegration;
    
    // Initialize when page loads
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            SidebarIntegration.init();
        });
    } else {
        SidebarIntegration.init();
    }
    
    // Also initialize on navigation changes (for SPA behavior)
    let lastUrl = location.href;
    new MutationObserver(() => {
        const url = location.href;
        if (url !== lastUrl) {
            lastUrl = url;
            setTimeout(() => {
                SidebarIntegration.init();
            }, 1000);
        }
    }).observe(document, { subtree: true, childList: true });
    
})();