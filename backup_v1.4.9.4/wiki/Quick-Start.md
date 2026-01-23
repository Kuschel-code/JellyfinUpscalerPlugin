# ⚡ Quick Start

Follow these steps to get your system ready in less than 5 minutes.

## 1. Installation
Install the plugin via the Jellyfin catalog (see [Installation](Installation)). Restart the server.

## 2. Provide Models
The plugin is shipped without models. Upload at least one `.onnx` model (e.g., `realesrgan.onnx`) to the folder `plugins/configurations/JellyfinUpscalerPlugin/models/`.

## 3. Check Hardware
Go to **Dashboard -> Plugins -> AI Upscaler Plugin**.
Click on **"Hardware Benchmark"**. The plugin will now analyze your CPU and GPU and automatically set the recommended values.

## 4. Save Configuration
Scroll down and click on **"💾 Save Configuration"**.

## 5. Movie On!
Open a movie in your browser. In the control bar at the bottom right, you will now find the **🎮 AI** button. Click on it to activate upscaling.

---
**Tip:** If the video stutters, choose a lower scaling factor (2x instead of 4x) or a faster model like `FSRCNN` in the plugin settings.
