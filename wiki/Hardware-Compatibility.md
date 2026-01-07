# 🎯 Hardware-Kompatibilität

Das AI Upscaler Plugin v1.4.0 nutzt **ONNX Runtime**, um eine plattformübergreifende Hardware-Beschleunigung zu ermöglichen.

## 🟢 NVIDIA Grafikkarten (Empfohlen)
NVIDIA-Karten bieten die beste Leistung durch den **CUDA Execution Provider**.
- **RTX 40er Serie**: Exzellent (unterstützt AV1, Hochgeschwindigkeits-4K-Upscaling).
- **RTX 30er Serie**: Exzellent (sehr stabile CUDA-Leistung).
- **RTX 20er Serie**: Sehr gut.
- **GTX 10/16er Serie**: Gut (benötigt mindestens 4GB VRAM für 1080p).

## 🔵 Intel & AMD Grafikkarten
Unter Windows nutzen diese Karten den **DirectML Execution Provider**.
- **Intel Arc Serie**: Sehr gut (hervorragende ONNX-Kompatibilität).
- **AMD Radeon RX 6000/7000**: Sehr gut.
- **AMD Radeon RX 500/5000**: Gut.
- **Intel UHD/Iris Xe**: Befriedigend (empfohlen nur für 720p-Verbesserung).

## 🖥️ CPU-Verarbeitung (Fallback)
Wenn keine kompatible GPU gefunden wird, nutzt das Plugin eine optimierte Multi-Thread-CPU-Verarbeitung.
- **High-End (12+ Kerne)**: Kann Echtzeit-720p-Upscaling bewältigen.
- **Mittelklasse (6-8 Kerne)**: Empfohlen für 480p -> 720p oder Hintergrund-Preprocessing.
- **Einsteiger/NAS (2-4 Kerne)**: Hintergrund-Preprocessing wird dringend empfohlen.

## 💾 Speicheranforderungen
- **1080p Upscaling**: ca. 2GB VRAM / 4GB System-RAM.
- **4K Upscaling**: ca. 6GB VRAM / 8GB System-RAM.
- **8K Vorschau**: ca. 12GB VRAM / 16GB System-RAM.

## 🐧 Linux Unterstützung
Linux-Nutzer sollten sicherstellen, dass sie die neuesten **NVIDIA-Treiber** und das `nvidia-container-toolkit` installiert haben (falls Docker verwendet wird). Die Unterstützung für Open-Source-Treiber (Mesa) erfolgt derzeit über CPU oder experimentelle Vulkan-Provider.
