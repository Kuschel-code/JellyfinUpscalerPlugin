# 🔗 Git Installation Guide

## 🚀 **ADVANCED INSTALLATION METHOD FOR NAS & DEVELOPERS**

### **Why Choose Git Installation?**
- ✅ **Automatic Updates** with `git pull`
- ✅ **Always Latest Version** directly from repository
- ✅ **Easy Rollback** to previous versions
- ✅ **Developer Features** and beta functionality
- ✅ **NAS-Optimized** for Synology, QNAP, Unraid, TrueNAS

---

## 🖥️ **SUPPORTED SYSTEMS**

### **✅ NAS Devices**
- **Synology DSM 7.0+** (DiskStation Manager)
- **QNAP QTS 5.0+** (QTS Operating System)
- **Unraid 6.10+** (Community Edition)
- **TrueNAS Scale** (Enterprise NAS)
- **OpenMediaVault** (Open Source NAS)

### **✅ Virtualization**
- **Docker Containers** (All platforms)
- **Proxmox VE** (Virtual Environment)
- **VMware vSphere** (Enterprise)
- **Hyper-V** (Windows Server)

### **✅ Operating Systems**
- **Linux** (Ubuntu, Debian, CentOS, Arch)
- **Windows** (with WSL2 or Git for Windows)
- **macOS** (with Homebrew)

---

## 🛠️ **AUTOMATED INSTALLATION**

### **One-Line Installation:**
```bash
curl -sSL https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/install-git.sh | bash
```

### **Manual Download & Execute:**
```bash
# Download installation script
wget https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/install-git.sh

# Make executable
chmod +x install-git.sh

# Run installation
sudo ./install-git.sh
```

---

## 📋 **MANUAL INSTALLATION STEPS**

### **Step 1: Prerequisites**
```bash
# Install Git (if not already installed)
# Synology: Install "Git Server" from Package Center
# QNAP: Install "Git" from App Center
# Unraid: Install "Git" from Community Applications
# Linux: sudo apt install git (Debian/Ubuntu) or sudo yum install git (CentOS)
```

### **Step 2: Locate Jellyfin Plugin Directory**
```bash
# Common plugin directories:
# Synology: /volume1/@appstore/jellyfin/plugins
# QNAP: /share/CACHEDEV1_DATA/.qpkg/jellyfin/plugins
# Unraid: /mnt/user/appdata/jellyfin/plugins
# TrueNAS: /mnt/tank/jellyfin/config/plugins
# Docker: /config/plugins
# Linux: /var/lib/jellyfin/plugins
```

### **Step 3: Clone Repository**
```bash
# Navigate to plugin directory
cd /path/to/jellyfin/plugins

# Clone the repository
git clone https://github.com/Kuschel-code/JellyfinUpscalerPlugin.git

# Enter plugin directory
cd JellyfinUpscalerPlugin
```

### **Step 4: Copy Plugin Files**
```bash
# Copy pre-built DLL (if available)
cp bin/Release/net8.0/JellyfinUpscalerPlugin.dll ./

# Copy configuration files
cp meta.json ./
cp manifest.json ./

# Copy web resources
cp -r Configuration ./
cp -r web ./
cp -r shaders ./
```

### **Step 5: Set Permissions**
```bash
# Set proper ownership
chown -R jellyfin:jellyfin .

# Set execute permissions
chmod +x JellyfinUpscalerPlugin.dll
```

### **Step 6: Restart Jellyfin**
```bash
# Synology
sudo synopkg restart jellyfin

# QNAP
/etc/init.d/jellyfin.sh restart

# Unraid
/etc/rc.d/rc.jellyfin restart

# Docker
docker restart jellyfin

# Linux
sudo systemctl restart jellyfin
```

---

## 🔄 **AUTOMATIC UPDATES**

### **Create Update Script:**
```bash
#!/bin/bash
# save as: update-upscaler.sh

echo "🔄 Updating AI Upscaler Plugin..."

# Stop Jellyfin
sudo systemctl stop jellyfin

# Navigate to plugin directory
cd /var/lib/jellyfin/plugins/JellyfinUpscalerPlugin

# Pull latest changes
git pull origin main

# Copy new DLL if available
if [ -f "bin/Release/net8.0/JellyfinUpscalerPlugin.dll" ]; then
    cp bin/Release/net8.0/JellyfinUpscalerPlugin.dll ./
    echo "✅ DLL updated"
fi

# Fix permissions
chown -R jellyfin:jellyfin .
chmod +x JellyfinUpscalerPlugin.dll

# Start Jellyfin
sudo systemctl start jellyfin

echo "🎉 Update completed!"
```

### **Make Script Executable:**
```bash
chmod +x update-upscaler.sh
```

### **Run Updates:**
```bash
./update-upscaler.sh
```

---

## 🔧 **ADVANCED CONFIGURATION**

### **Switch to Development Branch:**
```bash
cd /var/lib/jellyfin/plugins/JellyfinUpscalerPlugin
git checkout develop
git pull origin develop
```

### **Use Specific Version:**
```bash
# List available versions
git tag -l

# Switch to specific version
git checkout v1.3.6-ultimate
```

### **View Commit History:**
```bash
git log --oneline -10
```

### **Rollback to Previous Version:**
```bash
# View available versions
git log --oneline

# Rollback to specific commit
git checkout <commit-hash>
```

---

## 🚨 **TROUBLESHOOTING**

### **Problem: Git Not Found**
```bash
# Synology: Install Git Server from Package Center
# QNAP: Install Git from App Center
# Unraid: Install Git from Community Applications

# Or install manually:
# Debian/Ubuntu: sudo apt install git
# CentOS/RHEL: sudo yum install git
# macOS: brew install git
```

### **Problem: Permission Denied**
```bash
# Fix ownership
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/JellyfinUpscalerPlugin

# Fix permissions
sudo chmod -R 755 /var/lib/jellyfin/plugins/JellyfinUpscalerPlugin
sudo chmod +x /var/lib/jellyfin/plugins/JellyfinUpscalerPlugin/*.dll
```

### **Problem: Jellyfin Not Starting**
```bash
# Check Jellyfin logs
journalctl -u jellyfin -f

# Or check log files
tail -f /var/log/jellyfin/jellyfin.log
```

### **Problem: Plugin Not Loading**
```bash
# Verify plugin structure
ls -la /var/lib/jellyfin/plugins/JellyfinUpscalerPlugin/

# Required files:
# - JellyfinUpscalerPlugin.dll
# - meta.json
# - Configuration/ directory
# - web/ directory
```

---

## 📞 **SUPPORT & COMMUNITY**

### **Get Help:**
- 📚 **Documentation:** [Plugin Wiki](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/wiki)
- 🐛 **Bug Reports:** [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
- 💬 **Discussions:** [GitHub Discussions](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions)
- 🗨️ **Community:** [Jellyfin Forum](https://forum.jellyfin.org/)

### **Contribute:**
- 🔀 **Pull Requests:** [Contribute Code](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/pulls)
- 🌟 **Star Project:** [GitHub Repository](https://github.com/Kuschel-code/JellyfinUpscalerPlugin)
- 📢 **Share:** Tell others about the plugin!

---

**💡 Pro Tip:** Git installation is perfect for users who want the latest features and don't mind occasional updates. For production environments, consider the ZIP installation method for maximum stability.