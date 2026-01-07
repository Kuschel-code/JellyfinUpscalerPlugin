# 🧠 AI Models Guide

The plugin supports various neural network architectures via the **ONNX** format.

## 📦 Recommended Models
| Model Name | Best For | Quality | Speed |
|------------|----------|---------|-------|
| **Real-ESRGAN** | Photos, High-quality film | ⭐⭐⭐⭐⭐ | 🐢 Slow |
| **SwinIR** | Complex textures, detailed scenes | ⭐⭐⭐⭐⭐ | 🐢 Slow |
| **Waifu2x** | Anime, Cartoons, 2D art | ⭐⭐⭐⭐ | 🐎 Fast |
| **ESRGAN** | General TV shows, balanced use | ⭐⭐⭐⭐ | ⚖️ Balanced |
| **FSRCNN** | Older systems, NAS, low-res source | ⭐⭐⭐ | 🚀 Very Fast |

## 📥 Where to get models?
You can find pre-trained models in ONNX format from several community sources:
- **Upscayl**: Most models used in the Upscayl desktop app work perfectly.
- **Hugging Face**: Search for "ONNX Super Resolution".
- **Model Zoo**: Official ONNX model repositories.

## 📂 Installation
1.  Download the `.onnx` file.
2.  Rename it to match the model ID (e.g., `realesrgan.onnx`).
3.  Place it in the `models/` folder inside the plugin configuration directory.
    - **Windows**: `%AppData%\Jellyfin\plugins\configurations\JellyfinUpscalerPlugin\models`
    - **Linux**: `/var/lib/jellyfin/plugins/configurations/JellyfinUpscalerPlugin/models`
4.  Restart Jellyfin or refresh the plugin settings.

## 🛠️ Advanced: Custom Models
The plugin attempts to auto-detect input/output shapes. For best results, ensure your custom models:
- Use **NCHW** format (Batch, Channel, Height, Width).
- Accept **RGB** input normalized to `[0, 1]`.
- Provide a single output tensor with the upscaled image data.
