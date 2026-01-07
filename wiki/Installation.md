# 📥 Installation Guide

Follow these steps to install the AI Upscaler Plugin on your Jellyfin server.

## 🛠️ Prerequisites
- **Jellyfin Server**: Version 10.8.0 or higher.
- **Hardware**: 
    - **Recommended**: NVIDIA GPU (GTX 10-series or newer) for CUDA.
    - **Minimum**: Modern Intel/AMD CPU with at least 4 cores.
- **Models**: You must provide your own ONNX models (see [AI Models](AI-Models)).

## 📦 Option 1: Plugin Repository (Recommended)
1.  Open your Jellyfin Dashboard.
2.  Go to **Plugins** -> **Repositories**.
3.  Add the official AI Upscaler repository URL.
4.  Go to **Catalog**, find **AI Upscaler**, and click **Install**.
5.  Restart your Jellyfin server.

## 📂 Option 2: Manual Installation
1.  Download the latest `JellyfinUpscalerPlugin.dll` from the [Releases](https://github.com/user/JellyfinUpscalerPlugin/releases) page.
2.  Stop your Jellyfin server.
3.  Place the `.dll` file in your Jellyfin `plugins/` directory (create an `AIUpscaler` folder first).
4.  Start your Jellyfin server.

## 🧠 Adding AI Models
The plugin requires `.onnx` model files to function.
1.  Navigate to your Jellyfin data directory:
    - **Windows**: `%AppData%\Jellyfin\plugins\configurations\JellyfinUpscalerPlugin\models`
    - **Linux**: `/var/lib/jellyfin/plugins/configurations/JellyfinUpscalerPlugin/models`
2.  Place your `.onnx` files in the `models/` folder.
3.  Ensure the file names match the model IDs in the configuration (e.g., `realesrgan.onnx`).

## 🧪 Verification
After installation and adding models:
1.  Go to the Plugin settings in the Dashboard.
2.  Check if your GPU is detected in the **Live Hardware Status** section.
3.  Try the **AI Comparison Preview** to ensure models are loading correctly.
