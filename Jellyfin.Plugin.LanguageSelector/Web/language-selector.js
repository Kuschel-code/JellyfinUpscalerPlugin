(function() {
    'use strict';

    const CONFIG = {
        apiBaseUrl: ApiClient ? ApiClient._serverAddress : '',
        flagIconsPath: '/web/configurationpage?name=LanguageSelector/flags/',
        checkInterval: 500,
        maxChecks: 20
    };

    const FLAGS = {
        'de': { icon: 'de.svg', label: 'German Audio' },
        'jp-de': { icon: 'jp-de.svg', label: 'Japanese Audio + German Subtitles' },
        'jp-en': { icon: 'jp-en.svg', label: 'Japanese Audio + English Subtitles' },
        'en': { icon: 'us.svg', label: 'English Audio' }
    };

    class LanguageSelector {
        constructor() {
            this.currentItemId = null;
            this.languageOptions = [];
            this.observer = null;
            this.init();
        }

        init() {
            this.setupPageObserver();
            this.checkCurrentPage();
        }

        setupPageObserver() {
            if (this.observer) {
                this.observer.disconnect();
            }

            this.observer = new MutationObserver(() => {
                this.checkCurrentPage();
            });

            this.observer.observe(document.body, {
                childList: true,
                subtree: true
            });
        }

        checkCurrentPage() {
            const itemId = this.getItemIdFromUrl();
            
            if (!itemId || itemId === this.currentItemId) {
                return;
            }

            this.currentItemId = itemId;
            this.waitForPlayButton();
        }

        getItemIdFromUrl() {
            const match = window.location.hash.match(/\/item\?id=([a-f0-9]+)/i);
            return match ? match[1] : null;
        }

        waitForPlayButton() {
            let checks = 0;
            const interval = setInterval(() => {
                checks++;
                
                const playButton = this.findPlayButton();
                if (playButton || checks >= CONFIG.maxChecks) {
                    clearInterval(interval);
                    if (playButton) {
                        this.fetchLanguageOptions();
                    }
                }
            }, CONFIG.checkInterval);
        }

        findPlayButton() {
            const selectors = [
                'button[data-action="resume"]',
                'button[data-action="play"]',
                '.itemDetailPage .button-play',
                '.detailButton.btnPlay'
            ];

            for (const selector of selectors) {
                const button = document.querySelector(selector);
                if (button) {
                    return button;
                }
            }

            return null;
        }

        async fetchLanguageOptions() {
            if (!this.currentItemId) return;

            try {
                const response = await fetch(
                    `${CONFIG.apiBaseUrl}/Items/${this.currentItemId}/LanguageOptions`,
                    {
                        headers: {
                            'X-Emby-Token': ApiClient.accessToken()
                        }
                    }
                );

                if (!response.ok) {
                    console.warn('Language options not available for this item');
                    return;
                }

                const data = await response.json();
                this.languageOptions = data.options || [];
                
                if (this.languageOptions.length > 0) {
                    this.renderLanguageSelector();
                }
            } catch (error) {
                console.error('Error fetching language options:', error);
            }
        }

        renderLanguageSelector() {
            const existingSelector = document.querySelector('.language-selector-container');
            if (existingSelector) {
                existingSelector.remove();
            }

            const playButton = this.findPlayButton();
            if (!playButton) return;

            const container = this.createContainer();
            const buttonGroup = this.createButtonGroup();

            this.languageOptions.forEach(option => {
                const button = this.createFlagButton(option);
                buttonGroup.appendChild(button);
            });

            container.appendChild(buttonGroup);
            
            const parentElement = playButton.parentElement;
            if (parentElement) {
                parentElement.insertBefore(container, playButton.nextSibling);
            }
        }

        createContainer() {
            const container = document.createElement('div');
            container.className = 'language-selector-container';
            return container;
        }

        createButtonGroup() {
            const group = document.createElement('div');
            group.className = 'language-selector-buttons';
            return group;
        }

        createFlagButton(option) {
            const button = document.createElement('button');
            button.className = 'language-flag-button';
            button.setAttribute('data-audio-index', option.audioStreamIndex);
            button.setAttribute('data-subtitle-index', option.subtitleStreamIndex || -1);
            button.setAttribute('title', option.description || this.getFlagLabel(option.flagType));

            const flagConfig = FLAGS[option.flagType] || FLAGS['de'];
            
            const img = document.createElement('img');
            img.src = `${CONFIG.flagIconsPath}${flagConfig.icon}`;
            img.alt = flagConfig.label;
            img.className = 'flag-icon';

            button.appendChild(img);

            button.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.handleFlagClick(option);
            });

            return button;
        }

        getFlagLabel(flagType) {
            return FLAGS[flagType]?.label || 'Play';
        }

        async handleFlagClick(option) {
            console.log('Starting playback with:', option);

            try {
                const item = await ApiClient.getItem(ApiClient.getCurrentUserId(), this.currentItemId);
                
                const playOptions = {
                    ids: [this.currentItemId],
                    startPositionTicks: 0,
                    mediaSourceId: item.MediaSources[0].Id,
                    audioStreamIndex: option.audioStreamIndex,
                    subtitleStreamIndex: option.subtitleStreamIndex
                };

                if (window.playbackManager) {
                    await window.playbackManager.play(playOptions);
                } else if (window.MediaController) {
                    MediaController.play(playOptions);
                } else {
                    console.error('No playback manager available');
                }
            } catch (error) {
                console.error('Error starting playback:', error);
            }
        }

        destroy() {
            if (this.observer) {
                this.observer.disconnect();
            }
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            window.languageSelector = new LanguageSelector();
        });
    } else {
        window.languageSelector = new LanguageSelector();
    }
})();
