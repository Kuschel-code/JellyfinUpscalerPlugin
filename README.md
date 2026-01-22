# 🎮 Jellyfin AI Upscaler Plugin v1.4.9.1

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.11.x+-00A4DC.svg)](https://jellyfin.org)

An advanced, AI-powered video enhancement plugin for Jellyfin. Improve your media in real-time or via pre-processing using state-of-the-art neural networks.

## 🚀 Key Features

- **Real-Time Upscaling**: Experience crystal-clear images during playback with WebGL client-side rendering.
- **Hardware Acceleration**: Full support for NVIDIA (CUDA), VAAPI (Linux), QSV (Intel), and DirectML (Windows).
- **Multiple AI Models**: Support for Real-ESRGAN, SwinIR, Waifu2x, and more.
- **Hardware Benchmarking**: Built-in tools for detection and optimization based on server performance.
- **Dedicated Dashboard**: AI Upscaler Dashboard in sidebar with hardware status, job monitoring, and quick actions.
- **Modern UI Integration**: Fully compatible with Jellyfin 10.10+ Dashboard and Player.
- **Comparison View**: Preview AI upscaling results before applying them to your library.
- **Real-Time Progress Hub**: SignalR-compatible progress broadcasting to Jellyfin Dashboard.
- **Auto Library Scanning**: Automatic library refresh after successful upscaling jobs.
- **FFmpeg Wrapper**: Auto-configuration with hardware-aware upscaling filter injection.
- **Job Control API**: Pause, resume, and cancel processing jobs via REST API.
- **Dependency Validation**: Platform-specific native library isolation and verification.

## 📋 Recent Updates (v1.4.8 - v1.4.9.1)

### v1.4.9.1 (Latest)
- **Settings & UI Access**: Fixed an issue where the settings page was inaccessible. The "AI Upscaler Dashboard" and "AI Upscaler Settings" now properly appear in the sidebar.
- **Improved Compatibility**: Broadened page registration to ensure better compatibility with various Jellyfin Web clients.

### v1.4.9
- **Plugin Branding**: Fixed the missing plugin logo in newer Jellyfin servers by embedding `thumb.png` correctly.
- **Repository Integrity**: Resolved initial release checksum mismatches.

### v1.4.8
- **Core Engine Upgrade**: Updated for **Jellyfin 10.11.6** and **.NET 9.0**.
- **Dynamic FFmpeg Support**: Rewritten wrapper to support dynamic paths and proper filter injection.
- **Video Processing Fix**: Resolved a critical bug where videos were limited to 30fps; now respects source framerate.
- **Stability**: Enhanced thread safety with `ConcurrentDictionary` to prevent crashes under load.

## 🛠️ Installation

### Repository Method (Recommended)
1. Open your Jellyfin Dashboard.
2. Go to **Plugins** > **Repositories**.
3. Add a new repository with the following URL:
   ```
   https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v1.4.9.1/JellyfinUpscalerPlugin-v1.4.9.1.zip
   ```
4. Go to the **Catalog**, search for "AI Upscaler Plugin", and install the latest version.
5. Restart Jellyfin.

## ⚙️ Configuration

After installation, you can find settings under **Dashboard > Plugins > AI Upscaler Plugin**.

- **Enable Plugin**: Global switch for the upscaler.
- **Scaling Factor**: Choose between 2x, 4x, or custom scaling.
- **Hardware Detection**: The plugin automatically detects available GPUs and suggests optimal settings.

## 📖 Wiki & Support

Detailed guides, hardware lists, and troubleshooting can be found in our **[GitHub Wiki](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)**.

- [Getting Started](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki/Quick-Start)
- [Hardware Compatibility](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki/Hardware-Compatibility)
- [Performance Benchmarks](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki/Performance-Benchmarks)
- [FAQ](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki/FAQ)

## 📄 License

This project is licensed under the MIT License - see [LICENSE](LICENSE) for details.
