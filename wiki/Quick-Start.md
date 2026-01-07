# ⚡ Quick Start Guide - AI Upscaler Plugin v1.4.0 STABLE

Get your AI upscaling running in **under 5 minutes**!

---

## 🚀 **5-MINUTE SETUP**

### **Step 1: Install Plugin (2 minutes)**

**📋 Copy this repository URL:**
```
https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json
```

**🔧 In Jellyfin Dashboard:**
1. **Plugins** → **Repositories** → **Add Repository**
2. **Paste URL** → **Save**
3. **Catalog** → **"AI Upscaler Plugin"** → **Install**
4. **Restart Jellyfin**

### **Step 2: Configure (1 minute)**

1. **Dashboard** → **Plugins** → **"AI Upscaler Plugin"**
2. The plugin will automatically detect your hardware (NVIDIA/DirectML).
3. Set your preferred **Scale Factor** (e.g., 2x) and **Quality Level**.
4. **Save** the settings.

### **Step 3: Test & Enjoy (30 seconds)**

1. **Play any video** in Jellyfin.
2. The upscaler works automatically in the background based on your settings.
3. Access the **Quick Menu** in the player for on-the-fly adjustments.

---

## 🎯 **RECOMMENDED SETTINGS**

### **🎮 Balanced (Good for most)**
```
✅ Enable Plugin: On
✅ Scale Factor: 2.0x
✅ Quality Level: Medium
```

### **🏠 Home Theater (High End)**
```
✅ Enable Plugin: On
✅ Scale Factor: 4.0x
✅ Quality Level: High
```

### **📱 Low Power / Mobile**
```
✅ Enable Plugin: On
✅ Scale Factor: 1.5x
✅ Quality Level: Low
```

---

## 💡 **INSTANT TIPS**

### **🔥 Performance Boost**
- **NVIDIA Users**: Ensure your drivers are up to date to utilize CUDA.
- **Intel/AMD Users**: The plugin uses DirectML for hardware acceleration.
- **Hardware Test**: Run the built-in benchmark to see your system's capabilities.

### **🎨 Quality Enhancement**
- Higher **Quality Levels** provide better results but require more GPU power.
- **Scale Factor** directly impacts the final resolution (720p @ 2x = 1440p).

---

## 🚨 **TROUBLESHOOTING (30 seconds)**

### **❌ Settings not saving?**
- Ensure you are on version **1.4.0**.
- Refresh your browser cache (Ctrl+F5).

### **⚠️ Poor Performance?**
- Lower the **Quality Level** in the settings.
- Reduce the **Scale Factor**.

### **🔧 Not Working at All?**
- Restart Jellyfin completely.
- Check the logs in **Dashboard → Logs**.
- Verify that your hardware supports ONNX Runtime or NVIDIA CUDA.

---

## 📞 **Need More Help?**

- **📖 [Home](Home)** - Main Wiki page
- **❓ [FAQ](FAQ)** - Common questions answered
- **🔧 [Troubleshooting](Troubleshooting)** - Fix any issues
- **💬 [Community Help](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions)** - Ask questions

**🎉 Enjoy your enhanced Jellyfin experience with AI upscaling!**
