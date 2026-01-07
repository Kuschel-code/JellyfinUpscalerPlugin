# 📥 Installation Guide - AI Upscaler Plugin

## 🚀 **Method 1: GitHub Repository (EASIEST FOR SERVERS)**

### **Why this is the BEST method:**
- ✅ **Automatic updates** - Always latest version
- ✅ **Version management** - Rollback capability
- ✅ **Server-friendly** - One-time setup
- ✅ **No manual downloads** - Everything automatic

### **Installation Steps:**

#### **Step 1: Add Repository**
1. Open **Jellyfin Admin Dashboard**
2. Navigate to **Plugins** → **Repositories**
3. Click **Add Repository**
4. Enter:
   ```
   Repository Name: AI Upscaler Plugin
   Repository URL: https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/manifest.json
   ```
5. Click **Save**

#### **Step 2: Install Plugin**
1. Go to **Plugins** → **Catalog**
2. Find **"AI Upscaler Plugin"** (should appear automatically)
3. Click **Install**
4. **Restart Jellyfin** when prompted

#### **Step 3: Verify Installation**
1. Check **Plugins** → **My Plugins**
2. Confirm **"AI Upscaler Plugin v1.3.5"** is listed as **Active**
3. Play any video and look for **Quick Settings** button

---

## 📦 **Method 2: Direct ZIP Upload**

If repository method doesn't work or you prefer manual control:

#### **Download & Upload**
1. **Download**: [JellyfinUpscalerPlugin-v1.3.5-RealFeatures.zip](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v1.3.5/JellyfinUpscalerPlugin-v1.3.5-RealFeatures.zip)
2. **Admin Dashboard** → **Plugins** → **Upload Plugin**  
3. **Select** the downloaded ZIP file
4. **Upload** and **Restart** Jellyfin

---

## ✅ **Post-Installation Verification**

### **Check Plugin Status**
- Plugin shows **"Active"** in My Plugins
- Version displays **"1.3.5"**
- No error messages in Jellyfin logs

### **Check UI Integration**
1. **Play any video** in Jellyfin web client
2. Look for **Quick Settings** button (⚙️ icon, top-right area)
3. Click to open - should show AV1 options
4. **Touch test**: Works on mobile/tablet

### **Check API Functionality**
Open in browser (replace with your server):
```
http://your-jellyfin-server:8096/api/upscaler/hardware
```
Should return JSON with GPU information.

---

## 🐛 **Troubleshooting**

### **Repository URL Issues**

#### **Exact URL Required**
Make sure you use the **exact URL** (copy-paste):
```
https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/manifest.json
```

#### **Check Network**
- Firewall blocking GitHub access?
- Corporate proxy issues?
- Try direct ZIP method instead

### **Plugin Not Appearing**

#### **Version Check**
- **Minimum**: Jellyfin 10.10.0
- **Recommended**: Jellyfin 10.10.3+
- Update Jellyfin if too old

#### **Complete Restart**
1. **Stop** Jellyfin service/container
2. **Wait** 10 seconds
3. **Start** Jellyfin again
4. **Clear browser cache** (Ctrl+F5)

### **Quick Settings Missing**

#### **JavaScript Issues**
1. **Open Developer Tools** (F12)
2. **Check Console** for errors
3. **Disable ad blockers** on Jellyfin domain
4. **Enable JavaScript** if disabled

#### **Browser Compatibility**
- **Chrome/Edge**: Full support
- **Firefox**: Full support  
- **Safari**: Limited support
- **Mobile browsers**: Touch-optimized

### **Performance Issues**

#### **Enable Light Mode**
For weaker hardware:
1. **Plugin Settings** → **Performance**
2. **Enable "Light Mode"**
3. **Reduce "Max Concurrent Streams"** to 1

#### **Check GPU Drivers**
- **NVIDIA**: 522.25+ for RTX 4000 AV1 support
- **Intel**: 31.0.101.4146+ for Arc AV1 support  
- **AMD**: Latest Adrenalin drivers

---

## 🔄 **Updating the Plugin**

### **Repository Method (Automatic)**
1. **Plugins** → **Catalog**
2. **Check for updates** (refresh if needed)
3. **Update** button appears when available
4. **Restart** after update

### **Manual Method**
1. **Download** latest ZIP from GitHub releases
2. **Remove** old plugin files
3. **Upload** new ZIP package
4. **Restart** Jellyfin

---

## 🗑️ **Uninstalling**

### **Via Dashboard**
1. **Plugins** → **My Plugins**  
2. Find **AI Upscaler Plugin**
3. Click **Uninstall**
4. **Restart** Jellyfin

### **Manual Removal (if needed)**
**Linux:**
```bash
sudo rm /var/lib/jellyfin/plugins/JellyfinUpscalerPlugin.dll
sudo rm -rf /var/lib/jellyfin/plugins/JellyfinUpscalerPlugin/
```

**Windows:**
Delete from: `C:\ProgramData\Jellyfin\Server\plugins\`

---

## 📞 **Need Help?**

### **Support Channels**
- 🐛 **GitHub Issues**: [Report problems](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
- 💬 **Discord**: [Community support](https://discord.gg/jellyfinupscaler)
- 📧 **Email**: support@jellyfinupscaler.com
- 📖 **Wiki**: [Full documentation](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)

### **When Reporting Issues**
Please include:
- **Jellyfin version**
- **Operating system** 
- **Hardware specs** (GPU model)
- **Error messages** from logs
- **Steps to reproduce** the problem

---

## 🔗 **Quick Links**

- 📋 **[System Requirements](System-Requirements)** - Check compatibility
- 🎮 **[Hardware Support](Hardware-Compatibility)** - GPU compatibility
- ⚙️ **[Quick Settings Guide](Quick-Settings-UI)** - Learn the UI
- 🔧 **[Advanced Configuration](Advanced-Configuration)** - All settings
- 📱 **[Mobile Optimization](Mobile-Optimization)** - Touch features