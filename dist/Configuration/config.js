// Jellyfin Upscaler Plugin Configuration
define(['pluginManager', 'loading', 'dialogHelper', 'emby-select', 'emby-input', 'emby-checkbox'], function (pluginManager, loading, dialogHelper) {
    'use strict';

    var pluginId = 'f87f700e-679d-43e6-9c7c-b3a410dc3f12';

    function loadConfiguration(page) {
        loading.show();

        // Load plugin configuration
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            // Set form values
            page.querySelector('#profileSelect').value = config.selectedProfile || 'Default';
            page.querySelector('#maxFPS').value = config.maxFPSForAI || '60 FPS';
            page.querySelector('#minResolution').value = config.minResolutionForAI || '720p';
            page.querySelector('#sharpness').value = config.sharpness || 2;
            
            loading.hide();
        }).catch(function () {
            loading.hide();
        });
    }

    function saveConfiguration(page) {
        loading.show();

        var config = {
            selectedProfile: page.querySelector('#profileSelect').value,
            maxFPSForAI: page.querySelector('#maxFPS').value,
            minResolutionForAI: page.querySelector('#minResolution').value,
            sharpness: parseInt(page.querySelector('#sharpness').value)
        };

        ApiClient.updatePluginConfiguration(pluginId, config).then(function () {
            Dashboard.processPluginConfigurationUpdateResult();
            loading.hide();
        }).catch(function () {
            loading.hide();
        });
    }

    return function (view) {
        view.addEventListener('viewshow', function () {
            loadConfiguration(view);
        });

        view.querySelector('.btnSave').addEventListener('click', function () {
            saveConfiguration(view);
        });
    };
});