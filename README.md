# Jellyfin AI Upscaler Plugin v1.4.0 (Stable)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Jellyfin Version](https://img.shields.io/badge/Jellyfin-10.10.x-00A4DC.svg)](https://jellyfin.org)

An advanced AI-powered video upscaling plugin for Jellyfin. Enhance your low-resolution media in real-time or via pre-processing using state-of-the-art neural networks.

## 🚀 Key Features

- **Real-Time Upscaling**: Experience improved clarity while watching.
- **Hardware Acceleration**: Full support for NVIDIA (CUDA) and DirectML (AMD/Intel).
- **Multiple AI Models**: Support for Real-ESRGAN, Waifu2x, and specialized Anime models.
- **Hardware Benchmarking**: Built-in tools to detect and optimize for your server's capabilities.
- **Seamless Integration**: Native-feeling configuration dashboard and quick-access player menu.

## 🛠️ Installation

### Repository Method (Recommended)
1. Open your Jellyfin Dashboard.
2. Navigate to **Plugins** > **Repositories**.
3. Add a new repository with the following URL:
   `https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json`
4. Go to **Catalog** and search for "AI Upscaler Plugin".
5. Install version **1.4.0** and restart Jellyfin.

### Manual Installation
1. Download the latest `JellyfinUpscalerPlugin.dll` from the [Releases](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases) page.
2. Place it in your Jellyfin `plugins/AI-Upscaler` directory.
3. Restart your Jellyfin server.

## ⚙️ Configuration

Once installed, you can configure the plugin under **Dashboard > Plugins > AI Upscaler Plugin**.

- **Enable Plugin**: Globally toggle the upscaler.
- **Scale Factor**: Choose between 2x, 4x, or custom scaling.
- **Quality Level**: Balance between performance and visual fidelity.
- **Hardware Detection**: The plugin automatically detects available GPUs.

## 📖 Wiki & Support

For detailed guides, hardware compatibility lists, and troubleshooting, visit our **[GitHub Wiki](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)**.

- [Getting Started](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki/Quick-Start)
- [Hardware Compatibility](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki/Hardware-Compatibility)
- [Performance Benchmarks](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki/Performance-Benchmarks)
- [FAQ](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki/FAQ)

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
