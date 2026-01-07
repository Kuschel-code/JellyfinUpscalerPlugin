# ❓ Frequently Asked Questions - AI Upscaler Plugin v1.4.0 STABLE

Common questions and answers about the stable AI upscaling solution for Jellyfin.

---

## 🔥 **GENERAL QUESTIONS**

### **❓ What is the AI Upscaler Plugin v1.4.0 STABLE?**
A robust, production-ready AI-powered video enhancement plugin that delivers:
- **🔬 Real Hardware Probing**: Accurate detection of NVIDIA and DirectML hardware.
- **⚙️ Synchronized Configuration**: Perfect alignment between UI and backend.
- **🎨 High Quality AI Models**: Support for Real-ESRGAN and Waifu2x.
- **🌐 Clean API**: Optimized for performance and stability.

### **❓ Is it free?**
**Yes!** Completely free and open-source under the MIT license.

### **❓ What makes v1.4.0 STABLE special?**
Unlike previous versions that used simulated data, v1.4.0 uses **real hardware detection** (ONNX Runtime, nvidia-smi) to ensure settings are perfectly optimized for your server's actual capabilities.

---

## 🖥️ **HARDWARE REQUIREMENTS**

### **❓ What hardware do I need?**

| Level | GPU | VRAM | Use Case |
|-------|-----|------|----------|
| **Minimum** | GTX 1650 / RX 580 | 2GB | 1080p upscaling |
| **Recommended** | RTX 3070 / RX 6700 XT | 6GB | 4K upscaling |
| **Ultimate** | RTX 4080 / RX 7800 XT | 12GB+ | 8K upscaling |

### **❓ Does it work on integrated graphics?**
**Yes!** The plugin uses **DirectML**, which allows it to run on Intel UHD/Iris, AMD APUs, and even Apple Silicon.

---

## 📱 **COMPATIBILITY QUESTIONS**

### **❓ Which video formats are supported?**
**All major formats** supported by Jellyfin, including MKV, MP4, and AVI. It works seamlessly with H.264, H.265 (HEVC), and AV1.

### **❓ Which Jellyfin versions are supported?**
- **✅ Jellyfin 10.10.0+**: Full compatibility (Standard).
- **⚠️ Jellyfin 10.9.x**: Should work but not officially tested for 1.4.0.

---

## ⚡ **PERFORMANCE QUESTIONS**

### **❓ Does it slow down my server?**
Upscaling is a resource-intensive task. However, v1.4.0 utilizes **Hardware Acceleration** to offload the work from the CPU to the GPU, ensuring the rest of your server remains responsive.

### **❓ How much storage does caching use?**
The plugin features a configurable cache. By default, it uses a small buffer, but you can increase this in the settings if you have extra SSD space.

---

## 🚨 **TROUBLESHOOTING**

### **❓ Settings not saving?**
Make sure you have updated to version **1.4.0**. This version fixes a critical property mismatch that caused settings to fail to save in earlier releases. Clear your browser cache (Ctrl+F5) after updating.

### **❓ Plugin not appearing?**
1. **Restart Jellyfin server** completely.
2. Check the **Jellyfin Logs** for any startup errors related to the plugin.
3. Verify the plugin DLL is in the correct folder.

---

## 📞 **STILL NEED HELP?**

- **📖 Wiki:** [Complete Documentation](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)
- **🛠️ Installation:** [Step-by-step Guide](Installation)
- **💬 Discussions:** [GitHub Discussions](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions)
- **🐛 Bug Reports:** [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)

**Found your answer?** ⭐ **Star the repository** to show support!
