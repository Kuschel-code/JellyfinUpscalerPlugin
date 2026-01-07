// Jellyfin Upscaler Plugin Configuration
define(['pluginManager', 'loading', 'dialogHelper', 'emby-select', 'emby-input', 'emby-checkbox'], function (pluginManager, loading, dialogHelper) {
    'use strict';

    var pluginId = 'f87f700e-679d-43e6-9c7c-b3a410dc3f22';

    function loadConfiguration(page) {
        loading.show();

        // Load plugin configuration
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            // Set form values
            page.querySelector('#EnablePlugin').checked = config.EnablePlugin || false;
            page.querySelector('#Model').value = config.Model || 'realesrgan';
            page.querySelector('#ScaleFactor').value = config.ScaleFactor || 2;
            page.querySelector('#QualityLevel').value = config.QualityLevel || 'balanced';
            
            loading.hide();
        }).catch(function () {
            loading.hide();
        });
    }

    function saveConfiguration(page) {
        loading.show();

        var config = {
            EnablePlugin: page.querySelector('#EnablePlugin').checked,
            Model: page.querySelector('#Model').value,
            ScaleFactor: parseInt(page.querySelector('#ScaleFactor').value),
            QualityLevel: page.querySelector('#QualityLevel').value
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