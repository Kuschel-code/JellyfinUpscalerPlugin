// AI Upscaler Plugin - Player Integration v1.3.6.7
// Enhanced player button and streaming integration

(function() {
    'use strict';
    
    // Plugin configuration
    const PLUGIN_ID = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';
    const PLUGIN_VERSION = '1.3.6.7';
    
    // Player integration manager
    const PlayerIntegration = {
        
        // Initialize player integration
        init: function() {
            console.log('AI Upscaler: Initializing player integration...');
            
            // Wait for Jellyfin player to be ready
            this.waitForPlayer();
            
            // Add CSS styles
            this.addStyles();
            
            // Listen for media changes
            this.attachMediaListeners();
        },
        
        // Wait for Jellyfin player to be available
        waitForPlayer: function() {
            const checkPlayer = () => {
                try {
                    if (window.ApiClient && window.playbackManager) {
                        console.log('AI Upscaler: Jellyfin player detected, integrating...');
                        this.integrateWithPlayer();
                    } else {
                        setTimeout(checkPlayer, 1000);
                    }
                } catch (error) {
                    console.error('AI Upscaler: Error waiting for player:', error);
                    setTimeout(checkPlayer, 2000);
                }
            };
            checkPlayer();
        },
        
        // Integrate with Jellyfin player
        integrateWithPlayer: function() {
            try {
                // Add upscaler button to player controls
                this.addPlayerButton();
                
                // Monitor playback events
                this.monitorPlayback();
                
                // Add keyboard shortcuts
                this.addKeyboardShortcuts();
            } catch (error) {
                console.error('AI Upscaler: Error integrating with player:', error);
            }
        },
        
        // Add upscaler button to player controls
        addPlayerButton: function() {
            const playerContainer = document.querySelector('.videoOsdBottom, .osdControls');
            if (!playerContainer) {
                setTimeout(() => this.addPlayerButton(), 2000);
                return;
            }
            
            // Create upscaler button
            const upscalerButton = document.createElement('button');
            upscalerButton.id = 'aiUpscalerButton';
            upscalerButton.className = 'paper-icon-button-light';
            upscalerButton.setAttribute('type', 'button');
            upscalerButton.setAttribute('title', 'AI Upscaler Settings');
            upscalerButton.innerHTML = `
                <span class="material-icons">auto_awesome</span>
                <span class="upscaler-status">AI</span>
            `;
            
            // Add click handler
            upscalerButton.addEventListener('click', (e) => {
                e.stopPropagation();
                this.toggleUpscalerMenu();
            });
            
            // Insert button into player controls
            const controlsContainer = playerContainer.querySelector('.mediaButton, .btnToggleFullscreen');
            if (controlsContainer && controlsContainer.parentNode) {
                controlsContainer.parentNode.insertBefore(upscalerButton, controlsContainer);
            } else {
                playerContainer.appendChild(upscalerButton);
            }
            
            console.log('AI Upscaler: Player button added');
        },
        
        // Toggle upscaler quick menu
        toggleUpscalerMenu: function() {
            const existingMenu = document.querySelector('#aiUpscalerQuickMenu');
            if (existingMenu) {
                existingMenu.remove();
                return;
            }
            
            const menu = document.createElement('div');
            menu.id = 'aiUpscalerQuickMenu';
            menu.className = 'aiUpscalerQuickMenu';
            menu.innerHTML = `
                <div class="quick-menu-header">
                    <span class="menu-title">🚀 AI Upscaler</span>
                    <button class="menu-close" onclick="this.parentElement.parentElement.remove()">×</button>
                </div>
                <div class="quick-menu-content">
                    <div class="menu-section">
                        <h4>Quick Settings</h4>
                        <div class="menu-item" onclick="PlayerIntegration.quickSetModel('realesrgan')">
                            <span class="menu-icon">🎨</span>
                            <span>Real-ESRGAN (High Quality)</span>
                        </div>
                        <div class="menu-item" onclick="PlayerIntegration.quickSetModel('swinir')">
                            <span class="menu-icon">⚡</span>
                            <span>SwinIR (Fast)</span>
                        </div>
                        <div class="menu-item" onclick="PlayerIntegration.quickSetModel('waifu2x')">
                            <span class="menu-icon">🎭</span>
                            <span>Waifu2x (Anime)</span>
                        </div>
                        <div class="menu-item" onclick="PlayerIntegration.quickSetModel('bicubic')">
                            <span class="menu-icon">🔧</span>
                            <span>Bicubic (Fallback)</span>
                        </div>
                    </div>
                    <div class="menu-section">
                        <h4>Scale Factor</h4>
                        <div class="scale-buttons">
                            <button class="scale-btn" onclick="PlayerIntegration.setScale(2)">2x</button>
                            <button class="scale-btn" onclick="PlayerIntegration.setScale(3)">3x</button>
                            <button class="scale-btn" onclick="PlayerIntegration.setScale(4)">4x</button>
                        </div>
                    </div>
                    <div class="menu-section">
                        <h4>Actions</h4>
                        <div class="menu-item" onclick="PlayerIntegration.toggleUpscaling()">
                            <span class="menu-icon">🔄</span>
                            <span>Toggle Upscaling</span>
                        </div>
                        <div class="menu-item" onclick="PlayerIntegration.showCurrentStats()">
                            <span class="menu-icon">📊</span>
                            <span>Show Statistics</span>
                        </div>
                        <div class="menu-item" onclick="PlayerIntegration.openFullConfig()">
                            <span class="menu-icon">⚙️</span>
                            <span>Full Configuration</span>
                        </div>
                    </div>
                </div>
            `;
            
            document.body.appendChild(menu);
            
            // Auto-close after 10 seconds
            setTimeout(() => {
                if (menu.parentElement) {
                    menu.remove();
                }
            }, 10000);
        },
        
        // Quick model selection
        quickSetModel: function(model) {
            console.log(`AI Upscaler: Setting model to ${model}`);
            
            // Update configuration
            this.updatePluginConfig({ model: model });
            
            // Show notification
            this.showPlayerNotification(`🎯 Model set to ${model}`, 'success');
            
            // Close menu
            const menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
        },
        
        // Set scale factor
        setScale: function(scale) {
            console.log(`AI Upscaler: Setting scale to ${scale}x`);
            
            this.updatePluginConfig({ scale: scale });
            this.showPlayerNotification(`📏 Scale set to ${scale}x`, 'success');
            
            const menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
        },
        
        // Toggle upscaling on/off
        toggleUpscaling: function() {
            const currentState = this.getPluginConfig().enabled;
            const newState = !currentState;
            
            this.updatePluginConfig({ enabled: newState });
            this.showPlayerNotification(
                `🔄 Upscaling ${newState ? 'enabled' : 'disabled'}`, 
                newState ? 'success' : 'warning'
            );
            
            // Update button status
            this.updateButtonStatus(newState);
            
            const menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
        },
        
        // Show current statistics
        showCurrentStats: function() {
            const stats = this.getCurrentStats();
            
            const statsWindow = window.open('', '_blank', 'width=600,height=400');
            statsWindow.document.write(`
                <!DOCTYPE html>
                <html>
                <head>
                    <title>AI Upscaler - Current Statistics</title>
                    <style>
                        body { font-family: monospace; background: #1a1a1a; color: #00ff00; padding: 20px; }
                        .header { color: #00d4ff; font-size: 1.5em; margin-bottom: 20px; }
                        .stat-item { margin: 10px 0; padding: 10px; background: #2a2a2a; border-radius: 5px; }
                        .stat-label { color: #ffd700; font-weight: bold; }
                        .stat-value { color: #ffffff; margin-left: 10px; }
                        .good { color: #00ff00; }
                        .warning { color: #ffa500; }
                        .error { color: #ff0000; }
                    </style>
                </head>
                <body>
                    <div class="header">📊 AI Upscaler Statistics</div>
                    <div class="stat-item">
                        <span class="stat-label">Status:</span>
                        <span class="stat-value ${stats.enabled ? 'good' : 'warning'}">
                            ${stats.enabled ? '✅ Active' : '⚠️ Inactive'}
                        </span>
                    </div>
                    <div class="stat-item">
                        <span class="stat-label">Model:</span>
                        <span class="stat-value">${stats.model}</span>
                    </div>
                    <div class="stat-item">
                        <span class="stat-label">Scale:</span>
                        <span class="stat-value">${stats.scale}x</span>
                    </div>
                    <div class="stat-item">
                        <span class="stat-label">Quality:</span>
                        <span class="stat-value">${stats.quality}</span>
                    </div>
                    <div class="stat-item">
                        <span class="stat-label">Hardware Acceleration:</span>
                        <span class="stat-value ${stats.hardwareAcceleration ? 'good' : 'warning'}">
                            ${stats.hardwareAcceleration ? '✅ Enabled' : '⚠️ Disabled'}
                        </span>
                    </div>
                    <div class="stat-item">
                        <span class="stat-label">Cache Size:</span>
                        <span class="stat-value">${stats.cacheSizeMB} MB</span>
                    </div>
                    <div class="stat-item">
                        <span class="stat-label">Performance:</span>
                        <span class="stat-value good">✅ Optimal</span>
                    </div>
                    <div class="stat-item">
                        <span class="stat-label">Last Updated:</span>
                        <span class="stat-value">${new Date().toLocaleString()}</span>
                    </div>
                </body>
                </html>
            `);
            
            const menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
        },
        
        // Open full configuration
        openFullConfig: function() {
            const configUrl = `/web/configurationpage?name=aiupscaler`;
            window.open(configUrl, '_blank');
            
            const menu = document.querySelector('#aiUpscalerQuickMenu');
            if (menu) menu.remove();
        },
        
        // Monitor playback events
        monitorPlayback: function() {
            if (window.playbackManager) {
                // Listen for playback start
                window.playbackManager.addEventListener('playbackstart', () => {
                    console.log('AI Upscaler: Playback started');
                    this.onPlaybackStart();
                });
                
                // Listen for playback stop
                window.playbackManager.addEventListener('playbackstop', () => {
                    console.log('AI Upscaler: Playback stopped');
                    this.onPlaybackStop();
                });
            }
        },
        
        // Handle playback start
        onPlaybackStart: function() {
            const config = this.getPluginConfig();
            if (config.enabled) {
                this.showPlayerNotification('🚀 AI Upscaler active', 'info');
            }
        },
        
        // Handle playback stop
        onPlaybackStop: function() {
            // Cleanup if needed
        },
        
        // Add keyboard shortcuts
        addKeyboardShortcuts: function() {
            document.addEventListener('keydown', (e) => {
                // Alt + U = Toggle upscaling
                if (e.altKey && e.key === 'u') {
                    e.preventDefault();
                    this.toggleUpscaling();
                }
                
                // Alt + M = Open quick menu
                if (e.altKey && e.key === 'm') {
                    e.preventDefault();
                    this.toggleUpscalerMenu();
                }
            });
        },
        
        // Attach media listeners
        attachMediaListeners: function() {
            // Listen for media quality changes
            document.addEventListener('mediaqualitychange', (e) => {
                console.log('AI Upscaler: Media quality changed', e.detail);
            });
        },
        
        // Configuration management
        getPluginConfig: function() {
            // Return mock configuration - in real implementation, fetch from server
            return {
                enabled: true,
                model: 'realesrgan',
                scale: 2,
                quality: 'balanced',
                hardwareAcceleration: true,
                cacheSizeMB: 1024
            };
        },
        
        updatePluginConfig: function(updates) {
            // In real implementation, update server configuration
            console.log('AI Upscaler: Updating config', updates);
        },
        
        getCurrentStats: function() {
            const config = this.getPluginConfig();
            return {
                ...config,
                timestamp: new Date().toISOString()
            };
        },
        
        // Update button status
        updateButtonStatus: function(enabled) {
            const button = document.querySelector('#aiUpscalerButton');
            if (button) {
                const status = button.querySelector('.upscaler-status');
                if (status) {
                    status.textContent = enabled ? 'ON' : 'OFF';
                    status.style.color = enabled ? '#00ff00' : '#ff6666';
                }
            }
        },
        
        // Show player notification
        showPlayerNotification: function(message, type = 'info') {
            const notification = document.createElement('div');
            notification.className = `ai-upscaler-notification notification-${type}`;
            notification.textContent = message;
            
            const videoContainer = document.querySelector('.videoContainer, .playerContainer');
            if (videoContainer) {
                videoContainer.appendChild(notification);
                
                setTimeout(() => {
                    if (notification.parentElement) {
                        notification.remove();
                    }
                }, 3000);
            }
        },
        
        // Add styles for player integration
        addStyles: function() {
            if (document.querySelector('#aiUpscalerPlayerStyles')) return;
            
            const styles = document.createElement('style');
            styles.id = 'aiUpscalerPlayerStyles';
            styles.textContent = `
                #aiUpscalerButton {
                    position: relative;
                    margin: 0 5px;
                    background: rgba(0, 0, 0, 0.7);
                    border: 1px solid rgba(255, 255, 255, 0.3);
                    border-radius: 4px;
                    color: #ffffff;
                    padding: 8px 12px;
                    font-size: 14px;
                    cursor: pointer;
                    transition: all 0.3s ease;
                }
                
                #aiUpscalerButton:hover {
                    background: rgba(0, 212, 255, 0.8);
                    border-color: #00d4ff;
                }
                
                #aiUpscalerButton .upscaler-status {
                    font-size: 10px;
                    font-weight: bold;
                    margin-left: 5px;
                }
                
                .aiUpscalerQuickMenu {
                    position: fixed;
                    top: 50%;
                    left: 50%;
                    transform: translate(-50%, -50%);
                    background: rgba(0, 0, 0, 0.95);
                    border: 2px solid #00d4ff;
                    border-radius: 12px;
                    padding: 0;
                    z-index: 10000;
                    min-width: 300px;
                    max-width: 400px;
                    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.8);
                    animation: menuSlideIn 0.3s ease-out;
                }
                
                @keyframes menuSlideIn {
                    from { transform: translate(-50%, -50%) scale(0.8); opacity: 0; }
                    to { transform: translate(-50%, -50%) scale(1); opacity: 1; }
                }
                
                .quick-menu-header {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    padding: 15px 20px;
                    background: linear-gradient(135deg, #00d4ff, #0099cc);
                    color: #000000;
                    border-radius: 10px 10px 0 0;
                }
                
                .menu-title {
                    font-weight: bold;
                    font-size: 16px;
                }
                
                .menu-close {
                    background: none;
                    border: none;
                    color: #000000;
                    font-size: 20px;
                    cursor: pointer;
                    padding: 0;
                    width: 24px;
                    height: 24px;
                    border-radius: 50%;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                }
                
                .menu-close:hover {
                    background: rgba(0, 0, 0, 0.2);
                }
                
                .quick-menu-content {
                    padding: 20px;
                }
                
                .menu-section {
                    margin-bottom: 20px;
                }
                
                .menu-section h4 {
                    color: #00d4ff;
                    margin: 0 0 10px 0;
                    font-size: 14px;
                    font-weight: 600;
                }
                
                .menu-item {
                    display: flex;
                    align-items: center;
                    padding: 10px 15px;
                    background: rgba(255, 255, 255, 0.1);
                    border-radius: 6px;
                    margin: 5px 0;
                    cursor: pointer;
                    transition: all 0.2s ease;
                    color: #ffffff;
                }
                
                .menu-item:hover {
                    background: rgba(0, 212, 255, 0.3);
                    transform: translateX(5px);
                }
                
                .menu-icon {
                    margin-right: 10px;
                    font-size: 16px;
                }
                
                .scale-buttons {
                    display: flex;
                    gap: 10px;
                }
                
                .scale-btn {
                    flex: 1;
                    padding: 8px 12px;
                    background: rgba(255, 255, 255, 0.1);
                    border: 1px solid rgba(255, 255, 255, 0.3);
                    border-radius: 4px;
                    color: #ffffff;
                    cursor: pointer;
                    transition: all 0.2s ease;
                }
                
                .scale-btn:hover {
                    background: rgba(0, 212, 255, 0.5);
                    border-color: #00d4ff;
                }
                
                .ai-upscaler-notification {
                    position: absolute;
                    top: 20px;
                    right: 20px;
                    padding: 10px 15px;
                    border-radius: 6px;
                    color: white;
                    font-weight: 500;
                    z-index: 9999;
                    animation: notificationSlideIn 0.3s ease-out;
                    pointer-events: none;
                }
                
                .notification-info { background: rgba(37, 99, 235, 0.9); }
                .notification-success { background: rgba(5, 150, 105, 0.9); }
                .notification-warning { background: rgba(217, 119, 6, 0.9); }
                .notification-error { background: rgba(220, 38, 38, 0.9); }
                
                @keyframes notificationSlideIn {
                    from { transform: translateX(100%); opacity: 0; }
                    to { transform: translateX(0); opacity: 1; }
                }
            `;
            
            document.head.appendChild(styles);
        }
    };
    
    // Initialize player integration
    document.addEventListener('DOMContentLoaded', function() {
        PlayerIntegration.init();
        
        // Make available globally
        window.PlayerIntegration = PlayerIntegration;
        
        console.log(`AI Upscaler Player Integration v${PLUGIN_VERSION} loaded`);
    });
    
    // Also try to initialize immediately if DOM is already loaded
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => PlayerIntegration.init());
    } else {
        PlayerIntegration.init();
    }
    
})();