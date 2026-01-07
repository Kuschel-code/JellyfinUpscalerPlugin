# 🎯 Hardware Compatibility Guide

The AI Upscaler Plugin v1.4.0 leverages **ONNX Runtime** to provide cross-platform hardware acceleration.

## 🟢 NVIDIA GPUs (Recommended)
NVIDIA cards provide the best performance via the **CUDA Execution Provider**.
- **RTX 40-Series**: Excellent (supports AV1, high-speed 4K upscaling).
- **RTX 30-Series**: Excellent (highly stable CUDA performance).
- **RTX 20-Series**: Very Good.
- **GTX 10/16-Series**: Good (requires at least 4GB VRAM for 1080p).

## 🔵 Intel & AMD GPUs
On Windows, these cards use the **DirectML Execution Provider**.
- **Intel Arc Series**: Very Good (excellent ONNX compatibility).
- **AMD Radeon RX 6000/7000**: Very Good.
- **AMD Radeon RX 500/5000**: Good.
- **Intel UHD/Iris Xe**: Fair (recommended for 720p enhancement only).

## 🖥️ CPU Processing (Fallback)
If no compatible GPU is found, the plugin will use optimized multi-threaded CPU processing.
- **High-End (12+ Cores)**: Can handle real-time 720p upscaling.
- **Mid-Range (6-8 Cores)**: Recommended for 480p -> 720p or background pre-processing.
- **Low-End/NAS (2-4 Cores)**: Background pre-processing is highly recommended.

## 💾 Memory Requirements
- **1080p Upscaling**: ~2GB VRAM / 4GB System RAM.
- **4K Upscaling**: ~6GB VRAM / 8GB System RAM.
- **8K Preview**: ~12GB VRAM / 16GB System RAM.

## 🐧 Linux Support
Linux users should ensure they have the latest **NVIDIA drivers** and `nvidia-container-toolkit` if running in Docker. Open-source drivers (Mesa) support is currently via CPU-only or experimental Vulkan providers.
