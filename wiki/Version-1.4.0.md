# 🚀 Version 1.4.0 STABLE - Modern UI & Performance Update

## 🎉 **Release Information**

- **Release Date:** January 8, 2026
- **Version:** 1.4.0 STABLE
- **Compatibility:** Jellyfin 10.10.x+
- **Status:** Production (Stable)

---

## 🔥 **Key Improvements**

### **Modernized User Interface**
*   **Jellyfin 10.10+ Standard**: The configuration page has been completely migrated to the latest Jellyfin structure (Native JavaScript, Custom Elements).
*   **Improved Navigation**: Faster access to settings and live hardware data.
*   **Synchronized Model Lists**: All available AI models are now dynamically synchronized between the API and UI.

### **Enhanced AI Model Support**
*   **New Models**: Support for HAT (High Detail), DRLN (Noise Reduction), and optimized SRCNN variants for lower-end hardware.
*   **Dynamic Scaling**: Support for variable scaling factors (2x, 3x, 4x) depending on model capacity.

### **Optimized Hardware Benchmarking**
*   **More Precise Estimates**: Improved algorithms for performance calculation based on actual hardware capacity.
*   **Auto-Optimization**: Faster detection and application of optimal settings for the first start.

### **Improved Comparison View**
*   **Side-by-Side Preview**: New tool in the dashboard to preview AI upscaling results on your own media items before processing.
*   **Real-Time Generation**: Generates high-quality previews using the selected AI model and scale factor.

---

## 🛠️ **Technical Changes**
- **Refactored configurationpage.html**: Removed legacy HTML structures and migrated to `emby-` components.
- **UpscalerController**: Expanded the API for dynamic model queries and hardware recommendations.
- **Manifest Update**: Updated metadata and release logs for clean repository hygiene.

---

**Version 1.4.0 brings long-awaited UI stability and ensures the plugin works seamlessly with future Jellyfin versions.**
