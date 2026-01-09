# ❓ Frequently Asked Questions (FAQ)

Questions and answers about the Jellyfin AI Upscaler Plugin v1.4.1 STABLE.

---

## 🔥 General Questions

### **What is the AI Upscaler Plugin?**
It is an extension for Jellyfin that uses artificial intelligence to improve low-resolution videos in real-time or via pre-processing (e.g., from SD to 4K).

### **Is the plugin free?**
**Yes!** The plugin is open-source and completely free under the MIT license.

### **What's new in v1.4.1 STABLE?**
Unlike previous versions, v1.4.1 uses **real hardware detection** (ONNX Runtime, nvidia-smi) to ensure settings are perfectly matched to your server's actual capabilities.

---

## 🖥️ Hardware & Performance

### **What hardware do I need?**
*   **Minimum**: A CPU with at least 4 cores or an entry-level GPU (GTX 1050).
*   **Recommended**: NVIDIA RTX 3060 or better for 4K real-time upscaling.
*   **NAS**: Works best with pre-processing (pre-calculation).

### **Does it support integrated graphics?**
**Yes!** Thanks to **DirectML**, the plugin also runs on Intel UHD/Iris, AMD APUs, and Apple Silicon (M1/M2/M3).

### **Will it slow down my server?**
Upscaling is computationally intensive. However, when using hardware acceleration (GPU), the CPU remains free for other tasks like transcoding.

---

## 🎮 Operation

### **Why don't I see an AI button in the player?**
1. Make sure "Show Player Button" is enabled in the plugin settings.
2. Clear the browser cache (Ctrl+F5).
3. Check if the plugin is listed as "Active" in the Jellyfin dashboard.

### **Does the plugin not save my settings?**
Please make sure you have installed version **1.4.1**. In older versions, there was a saving bug that has been fixed in the stable version.

---

## 📞 Support
If you have further questions, use our [GitHub Discussions](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions).
