# 🚀 Installation Guide - AI Upscaler Plugin v1.3.6 ULTIMATE

Complete step-by-step installation guide for all platforms and use cases.

---

## 🎯 **ONE-CLICK JELLYFIN INSTALLATION (RECOMMENDED)**

### **📋 Method 1: Jellyfin Plugin Catalog**
**🔥 Easiest method - works in 99% of cases**

1. **Open Jellyfin Dashboard** → **Plugins** → **Repositories**
2. **Click "Add Repository"**
3. **Paste this URL:**
   ```
   https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository-jellyfin.json
   ```
4. **Save** → Go to **Catalog** → Find **"AI Upscaler Plugin - Ultimate v1.3.6"**
5. **Install** → **Restart Jellyfin Server** → **Done!** 🎉

---

## 🔧 **MANUAL INSTALLATION METHODS**

### **📥 Method 2: Direct ZIP Download**
If catalog installation fails:

1. **Download Latest Release:**
   ```
   https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v1.3.6-ultimate/JellyfinUpscalerPlugin-v1.3.6-Ultimate.zip
   ```

2. **Extract to Plugins Folder:**
   ```
   Windows: C:\ProgramData\Jellyfin\Server\plugins\
   Linux: /var/lib/jellyfin/plugins/
   macOS: /var/lib/jellyfin/plugins/
   Docker: ./jellyfin/plugins/
   ```

3. **Restart Jellyfin Server**

---

## 🖥️ **PLATFORM-SPECIFIC INSTRUCTIONS**

### 🪟 **Windows Installation**
```powershell
# PowerShell (Run as Administrator)
$pluginPath = "C:\ProgramData\Jellyfin\Server\plugins\"
$downloadUrl = "https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v1.3.6-ultimate/JellyfinUpscalerPlugin-v1.3.6-Ultimate.zip"

# Create directory and download
New-Item -ItemType Directory -Path $pluginPath -Force
Invoke-WebRequest -Uri $downloadUrl -OutFile "$env:TEMP\upscaler.zip"
Expand-Archive -Path "$env:TEMP\upscaler.zip" -DestinationPath $pluginPath -Force

# Restart Jellyfin
Restart-Service -Name "Jellyfin"
```

### 🐧 **Linux Installation (Auto-Script)**
```bash
#!/bin/bash
# One-command installation for Linux

curl -sSL https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/install-git.sh | bash
```

**Or manual steps:**
```bash
# Set variables
PLUGIN_DIR="/var/lib/jellyfin/plugins"
DOWNLOAD_URL="https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v1.3.6-ultimate/JellyfinUpscalerPlugin-v1.3.6-Ultimate.zip"

# Create directory and download
sudo mkdir -p "$PLUGIN_DIR"
cd /tmp
wget "$DOWNLOAD_URL" -O upscaler.zip
sudo unzip -o upscaler.zip -d "$PLUGIN_DIR"

# Set permissions
sudo chown -R jellyfin:jellyfin "$PLUGIN_DIR"
sudo chmod -R 755 "$PLUGIN_DIR"

# Restart Jellyfin
sudo systemctl restart jellyfin
```

### 🍎 **macOS Installation**
```bash
#!/bin/bash
# macOS Installation

PLUGIN_DIR="/var/lib/jellyfin/plugins"
DOWNLOAD_URL="https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v1.3.6-ultimate/JellyfinUpscalerPlugin-v1.3.6-Ultimate.zip"

# Create directory and download
sudo mkdir -p "$PLUGIN_DIR"
curl -L "$DOWNLOAD_URL" -o /tmp/upscaler.zip
sudo unzip -o /tmp/upscaler.zip -d "$PLUGIN_DIR"

# Restart Jellyfin (if using Homebrew)
brew services restart jellyfin
```

---

## 🐳 **DOCKER INSTALLATION**

### **Docker Compose (Recommended)**
```yaml
# docker-compose.yml
version: '3.8'
services:
  jellyfin:
    image: jellyfin/jellyfin:latest
    container_name: jellyfin-upscaler
    volumes:
      - ./config:/config
      - ./cache:/cache
      - ./media:/media
      - ./plugins:/config/plugins  # Plugin directory
    environment:
      - JELLYFIN_PublishedServerUrl=http://localhost:8096
      - NVIDIA_VISIBLE_DEVICES=all  # For NVIDIA GPU
    ports:
      - "8096:8096"
    restart: unless-stopped
    
    # For NVIDIA GPU support
    runtime: nvidia
    environment:
      - NVIDIA_DRIVER_CAPABILITIES=all
```

**Installation Commands:**
```bash
# 1. Create directories
mkdir -p jellyfin/{config,cache,plugins}

# 2. Download plugin
cd jellyfin/plugins
curl -L https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v1.3.6-ultimate/JellyfinUpscalerPlugin-v1.3.6-Ultimate.zip -o upscaler.zip
unzip upscaler.zip

# 3. Set permissions
chown -R 1000:1000 jellyfin/
chmod -R 755 jellyfin/plugins/

# 4. Start container
docker-compose up -d
```

---

## 🏥 **NAS SYSTEMS**

### **🔧 Synology NAS**
1. **SSH into your Synology** (enable SSH in Control Panel)
2. **Find Jellyfin plugin directory:**
   ```bash
   find /volume* -name "plugins" -path "*/jellyfin/*" 2>/dev/null
   ```
3. **Download and extract:**
   ```bash
   cd [JELLYFIN_PLUGIN_DIRECTORY]
   sudo wget https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/download/v1.3.6-ultimate/JellyfinUpscalerPlugin-v1.3.6-Ultimate.zip
   sudo unzip JellyfinUpscalerPlugin-v1.3.6-Ultimate.zip
   ```
4. **Restart Jellyfin** via Package Center

### **📦 QNAP NAS**
1. **Container Station** → Open Jellyfin container
2. **Mount plugin directory** to host
3. **Download plugin** to mounted directory
4. **Restart container**

### **🎯 Unraid**
1. **Docker** → Jellyfin template
2. **Add volume mapping:** `/config/plugins` → `/mnt/user/appdata/jellyfin/plugins`
3. **Download plugin** to mapped directory
4. **Restart Jellyfin docker**

---

## ✅ **VERIFICATION & FIRST SETUP**

### **🔍 Verify Installation**
1. **Check Jellyfin Dashboard** → **Plugins**
2. **Look for:** "🚀 AI Upscaler Plugin v1.3.6 ULTIMATE"
3. **Status should be:** "Active" ✅

### **🎯 First Time Configuration**
1. **Go to:** **Dashboard** → **Plugins** → **AI Upscaler Configuration**
2. **Choose Preset:**
   - **🎮 Gaming:** High performance, low latency
   - **🏠 Home Theater:** Balanced quality/performance
   - **📱 Mobile:** Battery optimized
   - **🔧 Custom:** Manual configuration

3. **Hardware Detection:**
   - Plugin automatically detects your GPU
   - Optimizes settings for your hardware
   - Shows available AI models

### **🎬 Test with Video**
1. **Play any video** in Jellyfin
2. **Look for "🚀 AI Pro"** button in player
3. **Click to enable** upscaling
4. **Enjoy enhanced quality!** 🎉

---

## 🚨 **TROUBLESHOOTING**

### **❌ Plugin Not Appearing**
```bash
# Check Jellyfin logs
sudo journalctl -u jellyfin -f

# Check plugin directory permissions
ls -la /var/lib/jellyfin/plugins/
```

### **⚠️ Installation Failed**
1. **Clear browser cache** and refresh
2. **Restart Jellyfin service** completely
3. **Check available disk space** (>500MB needed)
4. **Verify network connectivity** to GitHub

### **🔧 Performance Issues**
1. **Check GPU drivers** are installed
2. **Verify VRAM** (minimum 2GB recommended)
3. **Monitor CPU usage** during upscaling
4. **Adjust AI model** if needed

---

## 📞 **SUPPORT**

- **📖 Wiki:** [Complete Documentation](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)
- **🐛 Issues:** [Report Bugs](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
- **💬 Discussions:** [Community Help](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions)
- **⚡ Quick Help:** Check [FAQ](FAQ) first

**Installation successful?** ⭐ **Star the repository** to show support!