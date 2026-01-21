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
        'jp': { icon: 'jp.svg', label: 'Japanese Audio' },
        'jp-de': { icon: 'jp-de.svg', label: 'Japanese Audio + German Subtitles' },
        'jp-en': { icon: 'jp-en.svg', label: 'Japanese Audio + English Subtitles' },
        'jp-us': { icon: 'jp-en.svg', label: 'Japanese Audio + English Subtitles' },
        'en': { icon: 'us.svg', label: 'English Audio' },
        'us': { icon: 'us.svg', label: 'English Audio' }
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
                this.checkEpisodeList();
                return;
            }

            this.currentItemId = itemId;
            this.checkItemType();
        }

        async checkItemType() {
            if (!this.currentItemId) return;

            try {
                const response = await fetch(
                    `${CONFIG.apiBaseUrl}/Users/${ApiClient.getCurrentUserId()}/Items/${this.currentItemId}`,
                    {
                        headers: {
                            'X-Emby-Token': ApiClient.accessToken()
                        }
                    }
                );

                if (!response.ok) return;

                const item = await response.json();
                
                if (item.Type === 'Episode') {
                    this.waitForPlayButton();
                } else if (item.Type === 'Series' || item.Type === 'Season') {
                    this.showSeriesLanguages(item);
                }
            } catch (error) {
                console.error('Error checking item type:', error);
                this.waitForPlayButton();
            }
        }

        async showSeriesLanguages(item) {
            try {
                const seriesId = item.Type === 'Series' ? item.Id : item.SeriesId;
                const response = await fetch(
                    `${CONFIG.apiBaseUrl}/Shows/${seriesId}/Episodes?userId=${ApiClient.getCurrentUserId()}&fields=MediaStreams`,
                    {
                        headers: {
                            'X-Emby-Token': ApiClient.accessToken()
                        }
                    }
                );

                if (!response.ok) return;

                const data = await response.json();
                const languages = this.collectLanguagesFromEpisodes(data.Items || []);
                
                if (languages.length > 0) {
                    this.renderSeriesLanguageInfo(languages);
                }
            } catch (error) {
                console.error('Error fetching series languages:', error);
            }
        }

        collectLanguagesFromEpisodes(episodes) {
            const languageSet = new Set();
            
            episodes.forEach(episode => {
                if (!episode.MediaStreams) return;
                
                const audioStreams = episode.MediaStreams.filter(s => s.Type === 'Audio');
                const subtitleStreams = episode.MediaStreams.filter(s => s.Type === 'Subtitle');
                
                audioStreams.forEach(audio => {
                    const audioLang = this.normalizeLanguage(audio.Language);
                    if (audioLang && FLAGS[audioLang]) {
                        languageSet.add(audioLang);
                    }
                    
                    subtitleStreams.forEach(sub => {
                        const subLang = this.normalizeLanguage(sub.Language);
                        if (subLang) {
                            const combined = `${audioLang}-${subLang}`;
                            if (FLAGS[combined]) {
                                languageSet.add(combined);
                            }
                        }
                    });
                });
            });
            
            return Array.from(languageSet);
        }

        normalizeLanguage(lang) {
            if (!lang) return null;
            const map = {
                'ger': 'de', 'deu': 'de', 'de': 'de',
                'jpn': 'jp', 'ja': 'jp', 'jp': 'jp',
                'eng': 'us', 'en': 'us', 'us': 'us'
            };
            return map[lang.toLowerCase()] || null;
        }

        renderSeriesLanguageInfo(languages) {
            const existingInfo = document.querySelector('.series-language-info');
            if (existingInfo) {
                existingInfo.remove();
            }

            const detailLogo = document.querySelector('.detailLogo');
            if (!detailLogo) return;

            const container = document.createElement('div');
            container.className = 'series-language-info';
            container.style.cssText = 'margin-top: 1.5em; padding: 1em; background: rgba(0,0,0,0.3); border-radius: 8px;';

            const title = document.createElement('div');
            title.textContent = 'Available Languages:';
            title.style.cssText = 'font-size: 1.1em; margin-bottom: 0.5em; color: #fff;';
            container.appendChild(title);

            const flagGroup = document.createElement('div');
            flagGroup.style.cssText = 'display: flex; gap: 0.5em; flex-wrap: wrap;';

            languages.forEach(langCode => {
                const flagConfig = FLAGS[langCode];
                if (!flagConfig) return;

                const flagItem = document.createElement('div');
                flagItem.className = 'language-info-flag';
                flagItem.style.cssText = 'display: flex; align-items: center; gap: 0.5em; padding: 0.5em 1em; background: rgba(255,255,255,0.1); border-radius: 6px;';

                const img = document.createElement('img');
                img.src = `${CONFIG.flagIconsPath}${flagConfig.icon}`;
                img.alt = flagConfig.label;
                img.style.cssText = 'width: 32px; height: 24px; border-radius: 4px;';

                const label = document.createElement('span');
                label.textContent = flagConfig.label;
                label.style.cssText = 'color: #fff; font-size: 0.9em;';

                flagItem.appendChild(img);
                flagItem.appendChild(label);
                flagGroup.appendChild(flagItem);
            });

            container.appendChild(flagGroup);
            detailLogo.parentElement.insertBefore(container, detailLogo.nextSibling);
        }

        async checkEpisodeList() {
            const episodeCards = document.querySelectorAll('.listItem[data-type="Episode"]');
            if (episodeCards.length === 0) return;

            episodeCards.forEach(async (card) => {
                if (card.querySelector('.episode-language-indicator')) return;

                const itemId = card.getAttribute('data-id');
                if (!itemId) return;

                try {
                    const response = await fetch(
                        `${CONFIG.apiBaseUrl}/Users/${ApiClient.getCurrentUserId()}/Items/${itemId}?fields=MediaStreams`,
                        {
                            headers: {
                                'X-Emby-Token': ApiClient.accessToken()
                            }
                        }
                    );

                    if (!response.ok) return;

                    const episode = await response.json();
                    const languages = this.collectLanguagesFromEpisodes([episode]);

                    if (languages.length > 0) {
                        this.addEpisodeLanguageIndicator(card, languages);
                    }
                } catch (error) {
                    console.error('Error fetching episode languages:', error);
                }
            });
        }

        addEpisodeLanguageIndicator(card, languages) {
            const existingIndicator = card.querySelector('.episode-language-indicator');
            if (existingIndicator) return;

            const cardContent = card.querySelector('.listItemBody');
            if (!cardContent) return;

            const indicator = document.createElement('div');
            indicator.className = 'episode-language-indicator';
            indicator.style.cssText = 'display: flex; gap: 0.3em; margin-top: 0.3em;';

            languages.forEach(langCode => {
                const flagConfig = FLAGS[langCode];
                if (!flagConfig) return;

                const img = document.createElement('img');
                img.src = `${CONFIG.flagIconsPath}${flagConfig.icon}`;
                img.alt = flagConfig.label;
                img.title = flagConfig.label;
                img.style.cssText = 'width: 24px; height: 18px; border-radius: 3px; opacity: 0.8;';

                indicator.appendChild(img);
            });

            cardContent.appendChild(indicator);
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
            button.setAttribute('data-subtitle-index', option.subtitleStreamIndex !== undefined && option.subtitleStreamIndex !== null ? option.subtitleStreamIndex : -1);
            button.setAttribute('title', option.displayName || this.getFlagLabel(option.flagIcon));

            const flagConfig = FLAGS[option.flagIcon] || FLAGS['de'];
            
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

        getFlagLabel(flagIcon) {
            return FLAGS[flagIcon]?.label || 'Play';
        }

        async handleFlagClick(option) {
            console.log('Starting playback with:', option);

            try {
                this.setButtonLoading(true);
                
                const userId = ApiClient.getCurrentUserId();
                const item = await ApiClient.getItem(userId, this.currentItemId);
                
                if (!item || !item.MediaSources || item.MediaSources.length === 0) {
                    throw new Error('No media sources found for this item');
                }

                const mediaSource = item.MediaSources[0];
                const userdata = item.UserData || {};
                const resumeTicks = userdata.PlaybackPositionTicks || 0;
                
                const playOptions = {
                    ids: [this.currentItemId],
                    startPositionTicks: resumeTicks,
                    mediaSourceId: mediaSource.Id,
                    audioStreamIndex: option.audioStreamIndex,
                    subtitleStreamIndex: option.subtitleStreamIndex !== undefined ? option.subtitleStreamIndex : -1
                };

                console.log('Playback options:', playOptions);

                let playbackStarted = false;

                if (window.playbackManager) {
                    console.log('Using playbackManager');
                    await window.playbackManager.play(playOptions);
                    playbackStarted = true;
                } else if (ApiClient.playbackManager) {
                    console.log('Using ApiClient.playbackManager');
                    await ApiClient.playbackManager.play(playOptions);
                    playbackStarted = true;
                } else if (window.MediaController) {
                    console.log('Using MediaController');
                    await MediaController.play(playOptions);
                    playbackStarted = true;
                } else if (typeof playbackManager !== 'undefined') {
                    console.log('Using global playbackManager');
                    await playbackManager.play(playOptions);
                    playbackStarted = true;
                }

                if (!playbackStarted) {
                    console.warn('No playback manager found, attempting fallback');
                    this.fallbackPlayback(item, playOptions);
                }

                this.setButtonLoading(false);
            } catch (error) {
                console.error('Error starting playback:', error);
                this.setButtonLoading(false);
                this.showError('Failed to start playback. Please try again.');
            }
        }

        fallbackPlayback(item, playOptions) {
            const playUrl = `${CONFIG.apiBaseUrl}/web/index.html#!/item?id=${this.currentItemId}&serverId=${ApiClient.serverId()}`;
            
            const params = new URLSearchParams();
            if (playOptions.audioStreamIndex !== undefined) {
                params.set('audioStreamIndex', playOptions.audioStreamIndex);
            }
            if (playOptions.subtitleStreamIndex !== undefined && playOptions.subtitleStreamIndex !== -1) {
                params.set('subtitleStreamIndex', playOptions.subtitleStreamIndex);
            }
            if (playOptions.startPositionTicks) {
                params.set('startPositionTicks', playOptions.startPositionTicks);
            }

            const fullUrl = params.toString() ? `${playUrl}&${params.toString()}` : playUrl;
            
            setTimeout(() => {
                const playButton = this.findPlayButton();
                if (playButton) {
                    playButton.click();
                }
            }, 100);
        }

        setButtonLoading(isLoading) {
            const buttons = document.querySelectorAll('.language-flag-button');
            buttons.forEach(btn => {
                btn.disabled = isLoading;
                btn.style.opacity = isLoading ? '0.6' : '1';
                btn.style.cursor = isLoading ? 'wait' : 'pointer';
            });
        }

        showError(message) {
            if (window.Dashboard && Dashboard.alert) {
                Dashboard.alert(message);
            } else if (window.require) {
                require(['alert'], function(alert) {
                    alert(message);
                });
            } else {
                alert(message);
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
