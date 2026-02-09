# 📋 Changelog

Complete version history of the Jellyfin AI Upscaler Plugin.

---

## v1.5.1.1 (Hotfix) – February 2026
- 🔧 **Fixed**: SSH configuration was not being saved/loaded correctly
- ✨ **Added**: "Test SSH Connection" button now functional
- 🔌 **Added**: Backend API endpoint `/api/upscaler/ssh/test` for connection testing

## v1.5.1.0 (Remote Transcoding / SSH) – January 2026
> **⚠️ TEST VERSION** – Introduces SSH remote transcoding

- 🚀 **Remote Transcoding**: Connects to Docker via SSH to execute FFmpeg
- ☁️ **Multi-Architecture**: Docker images for NVIDIA, AMD, Intel, Apple Silicon, CPU
- 📂 **Path Mapping**: Map local media paths to remote Docker paths
- 🔒 **SSH Authentication**: Support for SSH Keys and Password auth
- ✨ **Enhanced UI**: New configuration section for Remote Transcoding

## v1.5.0.9
- 🔧 **Fixed**: 'selectedModelId is undefined' error preventing models from loading

## v1.5.0.8
- 🔧 **Fixed**: Localization issues with 'Settings saved' message

## v1.5.0.7
- 🔧 **Fixed**: 'require is not defined' error in settings page

## v1.5.0.6
- 🔧 **Fixed**: Dynamic URL resolution for AI Service

## v1.5.0.5
- 🔧 **Fixed**: Loading spinner compatibility for Jellyfin <10.9
- 📊 **Improved**: Dashboard hardware status & connection checks

## v1.5.0.3 – v1.5.0.4
- 🔧 **Fixed**: Save Configuration button issues
- ✨ **Added**: Test Connection button

## v1.5.0.2
- 🔧 **Fixed**: Settings not saving (#36) – AiServiceUrl now persists correctly

## v1.5.0.1 (Hotfix)
- 🔧 **Fixed #34**: Plugin initialization error (HardwareBenchmarkService DI)
- 🔧 **Fixed #33**: Checksum mismatch during installation
- 🔷 **Added #32**: Intel GPU/iGPU support via OpenVINO (Dockerfile.intel)

## v1.5.0.0 (Docker Microservice) – January 2026
> **🐳 Major Architecture Change**

- 🐳 **Docker Microservice Architecture**: AI processing in separate container
- 📦 **~1.6 MB instead of 417 MB**: No more native DLLs in plugin
- 🔧 **OpenCV DNN Models**: FSRCNN, ESPCN, LapSRN, EDSR
- 🌐 **Web UI**: Model management at http://localhost:5000
- ✅ **Fixed version format**: 4-part version for Jellyfin compatibility

---

## v1.4.x (Legacy)

### v1.4.9.4
- Settings Page Fix
- Cross-Platform Support
- Complete DI Registration

### v1.4.1 STABLE
- Improved hardware detection
- UI refinements
- Bug fixes

### v1.4.0 STABLE
- Redesigned UI for Jellyfin 10.10+
- Real hardware detection (ONNX Runtime, nvidia-smi)
- Side-by-side comparison preview
- 14 AI model support

> **Note:** v1.4.x used native DLLs bundled in the plugin (417 MB). This approach was abandoned in v1.5.0 due to `BadImageFormatException` conflicts with Jellyfin's assembly loader.
