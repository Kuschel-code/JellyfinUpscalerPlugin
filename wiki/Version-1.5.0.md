# 🐳 Version 1.5.0 – Docker Microservice Edition

> **Release Date:** January 2026
> **Status:** ⚠️ TEST VERSION

---

## Major Architecture Change

Version 1.5.0 completely redesigned the plugin architecture from a monolithic approach to a **Docker microservice**:

### Problem (v1.4.x)
```
Jellyfin Plugin (417 MB)
├── onnxruntime.dll        → BadImageFormatException
├── cuda_provider.dll      → Failed to load
├── opencv_world.dll       → Assembly format error
└── Plugin Logic (.NET)     → Disabled by Jellyfin
```

### Solution (v1.5.0)
```
Jellyfin Plugin (~1.6 MB)     Docker Container
├── Plugin Logic (.NET)        ├── Python + FastAPI
├── HTTP Client               ├── ONNX Runtime
└── UI Components             ├── OpenCV DNN
                              ├── CUDA/ROCm/OpenVINO
                              └── Web UI
```

---

## Key Changes
- **🐳 Docker Microservice** – AI processing in isolated container
- **📦 Plugin size: 1.6 MB** (down from 417 MB)
- **🔧 OpenCV DNN Models** – FSRCNN, ESPCN, LapSRN, EDSR
- **🌐 Web UI** – Model management interface
- **✅ No more DLL crashes** – Native libraries isolated in Docker
- **🖥️ Docker images** – NVIDIA, Intel, Apple Silicon, CPU

---

## Upgrade from v1.4.x

1. **Uninstall** the old plugin completely
2. **Delete** the old plugin folder
3. **Install** Docker container (see [Docker Setup](Docker-Setup))
4. **Install** v1.5.0 plugin from repository
5. **Configure** the AI Service URL

> **⚠️ Warning:** v1.4.x and v1.5.x are incompatible. Clean install required.
