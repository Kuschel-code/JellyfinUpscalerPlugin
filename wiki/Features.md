# ✨ Features

Complete list of features in the Jellyfin AI Upscaler Plugin v1.5.1.

---

## 🐳 Docker Microservice Architecture

- **Isolated AI Processing** – All heavy computation runs in a Docker container, not inside Jellyfin
- **No DLL Conflicts** – Native libraries (ONNX, CUDA, OpenCV) are isolated from Jellyfin's plugin system
- **Lightweight Plugin** – Only ~1.6 MB plugin size (vs. 417 MB in v1.4.x)
- **Independent Updates** – Update AI models without restarting Jellyfin

## 🚀 Remote Transcoding via SSH

- **GPU Offloading** – Offload FFmpeg execution to GPU-accelerated Docker containers
- **Path Mapping** – Automatic translation between local and remote file paths
- **SSH Authentication** – Secure key-based authentication
- **Connection Testing** – Built-in "Test SSH Connection" button in settings
- **Cross-Platform** – Windows Jellyfin → Linux Docker (and vice versa)

## 🖥️ Multi-GPU Support

- **NVIDIA CUDA** – Full Tensor Core acceleration (RTX/GTX)
- **AMD ROCm** – Native hardware acceleration (RX 6000/7000+)
- **Intel OpenVINO** – Arc GPUs and integrated graphics (UHD/Iris Xe)
- **Apple Silicon** – ARM64-optimized for M1/M2/M3/M4
- **CPU Fallback** – Multi-threaded processing when no GPU is available

## 🤖 AI Models

- **FSRCNN** – Fast Super-Resolution Convolutional Neural Network
- **ESPCN** – Efficient Sub-Pixel Convolutional Neural Network
- **LapSRN** – Laplacian Pyramid Super-Resolution Network
- **EDSR** – Enhanced Deep Super-Resolution
- **Real-ESRGAN** – Real-world Enhanced Super-Resolution GAN
- Supports 2x, 3x, and 4x upscaling factors

## 📊 Smart System

- **Real-time Benchmarking** – Auto-detects hardware and recommends settings
- **Automatic Fallback** – Switches to efficient models during overload
- **Dynamic Memory Management** – Prevents VRAM crashes
- **Health Monitoring** – Container health checks and status dashboard

## 📺 UI Integration

- **Player Quick-Menu** – AI button directly in Jellyfin player controls
- **Side-by-Side Preview** – Compare original vs. upscaled in configuration
- **Dashboard** – Job monitoring, hardware status, and connection checks
- **TV Remote Compatible** – Works with Android TV and Smart TV remotes

## 🔧 Advanced Features

- **Pre-Processing Cache** – Pre-calculates frequently watched content
- **Performance Metrics** – Real-time FPS and processing statistics
- **Web UI** – Model management interface at `http://server:5000`
- **FFmpeg Wrapper** – Automatic filter injection for transparent upscaling
