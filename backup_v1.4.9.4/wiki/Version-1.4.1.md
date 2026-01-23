# 🚀 Version 1.4.1 STABLE - Build Fix & Performance Update

## 🎉 **Release Information**

- **Release Date:** January 9, 2026
- **Version:** 1.4.1 STABLE
- **Compatibility:** Jellyfin 10.10.x+
- **Status:** Production (Stable)

---

## 🔥 **Key Improvements**

### **Stability & Fixes**
*   **Build Error Resolution**: Fixed critical build issues where `CacheResult` and other models were missing or incorrectly referenced.
*   **Settings Persistence**: Improved the reliability of saving configuration values by ensuring exact mapping between frontend and backend properties.
*   **Security Update**: Updated `SixLabors.ImageSharp` to v3.1.11 to address moderate severity security vulnerabilities (NU1902).

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

---

## 🛠️ **Technical Changes**
- **Refactored Models**: Added missing model classes to `UpscalerModels.cs`.
- **CacheManager Refinement**: Cleaned up redundant and broken cache methods to ensure high performance and stability.
- **Updated Dependencies**: Unified package versions across the project.

---

**Version 1.4.1 resolves initial stability issues of the 1.4.x branch and provides a secure, robust foundation for AI upscaling.**
