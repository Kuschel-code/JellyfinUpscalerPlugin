# Investigation Report - Jellyfin Upscaler Plugin

## Bug Summary
The plugin is currently in a "broken" or "skeleton" state despite reporting success in many areas. Major functional issues exist in settings persistence, API consistency, and the actual implementation of AI features.

## Root Cause Analysis
1. **Settings Mismatch**: The frontend (HTML/JS) uses different property names than the backend C# classes. Specifically, `ScaleFactor` vs `Scale`, `EnablePlugin` vs `Enabled`, etc. This prevents settings from being saved correctly via the standard Jellyfin plugin configuration system.
2. **API Chaos**: `UpscalerController.cs` contains multiple duplicate endpoints for settings (`GET/POST /settings`) with different signatures and behaviors. Some return hardcoded values, others do nothing but log.
3. **Skeleton Implementations**: Many "v1.4.0" features are currently just UI shells with backend simulations (e.g., `Task.Delay(2000)` for benchmarks, hardcoded strings for performance results).
4. **Version Confusion**: The plugin reports version 1.3.6.4, 1.3.6.7, and 1.4.0 in different parts of the code.

## Affected Components
- `PluginConfiguration.cs`: Model property names.
- `Controllers/UpscalerController.cs`: API structure and implementation.
- `Configuration/configurationpage.html`: Frontend settings logic.
- `Services/UpscalerCore.cs` & `VideoProcessor.cs`: Real implementation of AI logic.

## Proposed Solution
1. **Unify Settings Model**: Rename properties in `PluginConfiguration.cs` or update `configurationpage.html` to match. Ensure `ApiClient.updatePluginConfiguration` works correctly.
2. **Refactor API Controller**: Remove redundant endpoints. Consolidate into a clean RESTful API.
3. **Synchronize Versions**: Set all version strings to 1.4.0.
4. **Improve Implementations**: Replace simulations with actual logic where possible or at least more robust stubs that use the current configuration.
5. **Update Dependencies**: Check if `SixLabors.ImageSharp` can be updated to a secure version.

## Implementation Notes
1. **Model Alignment**: Renamed `Enabled`, `Scale`, and `Quality` in `PluginConfiguration.cs` to `EnablePlugin`, `ScaleFactor`, and `QualityLevel` to match the JS frontend. Added missing fields: `MaxVRAMUsage`, `CpuThreads`, `AutoRetryButton`, and `ButtonPosition`.
2. **API Consolidation**: Removed redundant `GET/POST /settings` endpoints in `UpscalerController.cs`. Integrated real hardware detection into `GetStatus`, `TestUpscaling`, and `RunQuickBenchmark`.
3. **Version Sync**: Updated all version strings to `1.4.0` in `manifest.json`, `meta.json`, `configurationpage.html`, and `UpscalerController.cs`.
4. **ID Fix**: Standardized Plugin ID in `config.js`.
5. **Vulnerability Mitigation**: Confirmed `SixLabors.ImageSharp` is at version `3.1.9` which addresses known vulnerabilities in `3.1.5`.

