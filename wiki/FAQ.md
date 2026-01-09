# ❓ Frequently Asked Questions (FAQ)

Questions and answers about the Jellyfin AI Upscaler Plugin v1.4.1 STABLE.

---

## 🔥 General Questions

### **What is the AI Upscaler Plugin?**
It is an extension for Jellyfin that uses artificial intelligence to enhance low-resolution videos in real-time or via pre-processing (e.g., from SD to 4K).

### **Is the plugin free?**
**Yes!** The plugin is open-source and absolutely free under the MIT license.

### **What's new in v1.4.1 STABLE?**
Unlike earlier versions, v1.4.1 uses **real hardware detection** (ONNX Runtime, nvidia-smi) to ensure that settings are perfectly matched to your server's actual capabilities.

---

## 🖥️ Hardware & Performance

### **What hardware do I need?**
*   **Minimum**: A CPU with at least 4 cores or an entry-level GPU (GTX 1050).
*   **Recommended**: NVIDIA RTX 3060 or better for 4K real-time upscaling.
*   **NAS**: Works best with pre-processing (advance calculation).

### **Does it support integrated graphics cards?**
**Yes!** Thanks to **DirectML**, the plugin also runs on Intel UHD/Iris, AMD APUs, and Apple Silicon (M1/M2/M3).

### **Will my server become slow?**
Upscaling is computationally intensive. However, when using hardware acceleration (GPU), the CPU remains free for other tasks like transcoding.

---

## 🎮 Operation

### **Why don't I see an AI button in the player?**
1. Ensure that "Show Player Button" is enabled in the plugin settings.
2. Clear the browser cache (Ctrl+F5).
3. Check if the plugin is listed as "Active" in the Jellyfin Dashboard.

### **Does the plugin not save my settings?**
Please ensure you have version **1.4.1** installed. In older versions, there was a bug in saving settings that has been fixed in the stable version.

---

## 📞 Support
If you have further questions, use our [GitHub Discussions](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions).
