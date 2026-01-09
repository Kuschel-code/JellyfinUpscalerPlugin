# 🔍 Troubleshooting

Here you will find solutions for common problems with the AI Upscaler Plugin.

---

## ❌ Common Issues

### 🚫 Plugin Not Working
**Symptoms:** No image enhancement, button missing in the player.
**Solutions:**
1. Restart the Jellyfin server.
2. Check if the plugin is enabled in the dashboard.
3. Verify hardware compatibility (see [Hardware](Hardware-Compatibility)).
4. Update graphics card drivers to the latest version.

### 🐌 Poor Performance
**Symptoms:** Stuttering, delays, high CPU load.
**Solutions:**
1. Lower the quality preset (High → Medium or Low).
2. Reduce the scaling factor (4x → 2x).
3. Enable "Hardware Acceleration" in the settings.
4. Check if other computationally intensive tasks are running on the server.

### 🎨 Image Defects (Artifacts)
**Symptoms:** Blur, ghosting, incorrect colors.
**Solutions:**
1. Try a different AI model (e.g., SwinIR instead of Real-ESRGAN).
2. Ensure that the model files (.onnx) are not corrupted.
3. Update the plugin to the latest version.

---

## 🛠️ Advanced Analysis

### 📊 Performance Diagnosis
Check the Jellyfin logs (`Dashboard -> Logs`) for entries with the keyword `AI Upscaler`. There you will find detailed error messages regarding hardware initialization.

### 🔧 Reset Configuration
If the plugin is running unstable:
1. Stop Jellyfin.
2. Delete the file `JellyfinUpscalerPlugin.xml` in the configuration folder.
3. Start Jellyfin and reconfigure.

---

## 📞 Further Help
If your problem persists, please visit our [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues) or the [Community Discussions](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions).
