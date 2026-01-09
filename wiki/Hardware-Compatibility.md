# 🎯 Hardware Compatibility

The AI Upscaler Plugin v1.4.1 uses **ONNX Runtime** to enable cross-platform hardware acceleration.

## 🟢 NVIDIA Graphics Cards (Recommended)
NVIDIA cards offer the best performance through the **CUDA Execution Provider**.
- **RTX 40 Series**: Excellent (supports AV1, high-speed 4K upscaling).
- **RTX 30 Series**: Excellent (very stable CUDA performance).
- **RTX 20 Series**: Very good.
- **GTX 10/16 Series**: Good (requires at least 4GB VRAM for 1080p).

## 🔵 Intel & AMD Graphics Cards
Under Windows, these cards use the **DirectML Execution Provider**.
- **Intel Arc Series**: Very good (excellent ONNX compatibility).
- **AMD Radeon RX 6000/7000**: Very good.
- **AMD Radeon RX 500/5000**: Good.
- **Intel UHD/Iris Xe**: Satisfactory (recommended only for 720p enhancement).

## 🖥️ CPU Processing (Fallback)
If no compatible GPU is found, the plugin uses optimized multi-threaded CPU processing.
- **High-End (12+ Cores)**: Can handle real-time 720p upscaling.
- **Mid-Range (6-8 Cores)**: Recommended for 480p -> 720p or background pre-processing.
- **Entry-Level/NAS (2-4 Cores)**: Background pre-processing is strongly recommended.

## 💾 Memory Requirements
- **1080p Upscaling**: approx. 2GB VRAM / 4GB System RAM.
- **4K Upscaling**: approx. 6GB VRAM / 8GB System RAM.
- **8K Preview**: approx. 12GB VRAM / 16GB System RAM.

## 🐧 Linux Support
Linux users should ensure they have the latest **NVIDIA drivers** and the `nvidia-container-toolkit` installed (if using Docker). Support for open-source drivers (Mesa) is currently via CPU or experimental Vulkan providers.
