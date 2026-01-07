<<<<<<< HEAD
# 🔍 Troubleshooting

Common issues and solutions for JellyfinUpscalerPlugin.

---

## ❌ **Common Issues**

### 🚫 **Plugin Not Working**
**Symptoms:** No enhancement, button missing
**Solutions:**
1. Restart Jellyfin server
2. Check plugin is enabled in Dashboard
3. Verify hardware compatibility
4. Update graphics drivers

### 🐌 **Poor Performance**
**Symptoms:** Lag, stuttering, high CPU usage
**Solutions:**
1. Lower quality preset (High → Medium)
2. Enable Light Mode for weak hardware
3. Check thermal throttling
4. Close unnecessary applications

### 🎨 **Visual Artifacts**
**Symptoms:** Blurring, oversharping, color issues
**Solutions:**
1. Reduce enhancement strength
2. Disable aggressive sharpening
3. Check AI model integrity
4. Update plugin to latest version

---

## 🛠️ **Advanced Troubleshooting**

### 📊 **Performance Diagnostics**
```powershell
# Check system resources
Get-Process | Where-Object {$_.Name -like "*jellyfin*"}
Get-WmiObject -Class Win32_VideoController | Select-Object Name, DriverVersion
```

### 🔧 **Reset Configuration**
1. Stop Jellyfin service
2. Delete plugin config file
3. Restart Jellyfin
4. Reconfigure settings

---

*For installation help, see [Installation Guide](Installation)*
=======
# 🛠️ Complete Troubleshooting Guide

> **Professional solutions for every possible issue you might encounter**

---

## 🚨 **Emergency Quick Fixes**

### **Plugin Not Working At All**
```bash
# 1. Emergency Reset
rm -rf /var/lib/jellyfin/plugins/JellyfinUpscaler*
curl -O https://github.com/Kuschel-code/JellyfinUpscalerPlugin/raw/main/INSTALL-FAILSAFE.cmd
./INSTALL-FAILSAFE.cmd

# 2. Check Jellyfin Logs
sudo journalctl -u jellyfin -f | grep -i upscaler

# 3. Verify Installation
ls -la /var/lib/jellyfin/plugins/JellyfinUpscaler*/
```

### **No AI Enhancement Button**
```javascript
// Browser Console Quick Fix
(function() {
    const button = document.createElement('button');
    button.innerHTML = '🔥 AI Pro';
    button.onclick = () => window.location.reload();
    document.body.appendChild(button);
})();
```

### **Severe Performance Issues**
```json
{
  "emergency_fallback_settings": {
    "ai_method": "traditional",
    "scale_factor": 1.5,
    "disable_all_enhancements": true,
    "cpu_only_mode": true
  }
}
```

---

## 🔧 **Installation Issues**

### **Plugin Not Appearing in Jellyfin**

#### **Symptom**: Plugin installed but not visible in dashboard

**Diagnosis Steps:**
```bash
# 1. Check plugin directory structure
ls -la /var/lib/jellyfin/plugins/
# Should show: JellyfinUpscaler_Advanced_1.3.0/

# 2. Verify plugin files
ls -la /var/lib/jellyfin/plugins/JellyfinUpscaler_Advanced_1.3.0/
# Required files: meta.json, main.js, icon.png

# 3. Check file permissions
stat /var/lib/jellyfin/plugins/JellyfinUpscaler_Advanced_1.3.0/meta.json
# Should be readable by jellyfin user
```

**Solutions:**
```bash
# Fix 1: Correct permissions
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/
sudo chmod -R 755 /var/lib/jellyfin/plugins/

# Fix 2: Verify GUID
cat /var/lib/jellyfin/plugins/JellyfinUpscaler_Advanced_1.3.0/meta.json
# GUID should be: "12345678-1234-1234-1234-123456789abc"

# Fix 3: Restart Jellyfin completely
sudo systemctl stop jellyfin
sudo systemctl start jellyfin
```

### **GUID Conflict Error**

#### **Symptom**: "Plugin GUID already exists" error

**Diagnosis:**
```bash
# Find conflicting plugins
find /var/lib/jellyfin/plugins/ -name "meta.json" -exec grep -l "12345678-1234-1234-1234-123456789abc" {} \;
```

**Solution:**
```bash
# Remove old/conflicting versions
sudo rm -rf /var/lib/jellyfin/plugins/JellyfinUpscalerPlugin_*
sudo rm -rf /var/lib/jellyfin/plugins/*upscaler*

# Clean installation
curl -O https://github.com/Kuschel-code/JellyfinUpscalerPlugin/raw/main/INSTALL-ADVANCED.cmd
./INSTALL-ADVANCED.cmd
```

### **Network/Download Issues**

#### **Symptom**: Can't download plugin files

**Diagnosis:**
```bash
# Test connectivity
curl -I https://github.com/Kuschel-code/JellyfinUpscalerPlugin/raw/main/dist/JellyfinUpscaler-Advanced.zip
# Should return: HTTP/2 200

# Check DNS resolution
nslookup github.com
```

**Solutions:**
```bash
# Solution 1: Use failsafe installer (multiple mirrors)
curl -O https://github.com/Kuschel-code/JellyfinUpscalerPlugin/raw/main/INSTALL-FAILSAFE.cmd
./INSTALL-FAILSAFE.cmd

# Solution 2: Manual download with different tool
wget --no-check-certificate https://github.com/Kuschel-code/JellyfinUpscalerPlugin/raw/main/dist/JellyfinUpscaler-Advanced.zip

# Solution 3: Use proxy/VPN if blocked
export https_proxy=your-proxy:port
curl -O https://github.com/Kuschel-code/JellyfinUpscalerPlugin/raw/main/dist/JellyfinUpscaler-Advanced.zip
```

---

## 🎮 **Hardware Detection Issues**

### **GPU Not Detected**

#### **Symptom**: Plugin shows "No compatible GPU found"

**Diagnosis Steps:**
```bash
# 1. Check GPU visibility
lspci | grep -E "(VGA|3D|Display)"
# Should show your GPU

# 2. Check GPU drivers
nvidia-smi          # For NVIDIA
rocm-smi           # For AMD  
intel_gpu_top      # For Intel

# 3. Check GPU accessibility
ls -la /dev/dri/
# Should show render nodes
```

**Solutions by GPU Type:**

#### **NVIDIA Solutions:**
```bash
# Install/Update NVIDIA drivers
sudo apt update
sudo apt install nvidia-driver-535  # or latest

# Install CUDA toolkit
sudo apt install nvidia-cuda-toolkit

# Check NVENC/NVDEC support
ffmpeg -hwaccels
# Should list: cuda, nvdec
```

#### **AMD Solutions:**
```bash
# Install AMDGPU drivers
sudo apt update
sudo apt install amdgpu-dkms

# Install ROCm (for compute)
sudo apt install rocm-opencl-runtime

# Check VAAPI support
vainfo
# Should list hardware acceleration capabilities
```

#### **Intel Solutions:**
```bash
# Install Intel GPU drivers
sudo apt update
sudo apt install intel-media-va-driver

# For Intel Arc
sudo apt install intel-opencl-icd

# Check QSV support
ffmpeg -hwaccels
# Should list: qsv, vaapi
```

### **Wrong AI Method Selected**

#### **Symptom**: Plugin uses suboptimal AI method for GPU

**Diagnosis:**
```javascript
// Check detected capabilities
console.log(window.upscalerSettings?.detectedGPU);
// Should show: {vendor, name, supportedMethods}
```

**Manual Override:**
```json
{
  "force_gpu_detection": {
    "vendor": "nvidia",
    "model": "RTX 4080",
    "supported_methods": ["dlss30", "dlss24", "fsr30", "real_esrgan"],
    "vram": 16384,
    "compute_capability": 8.9
  }
}
```

---

## 🌐 **Language & Localization Issues**

### **Language Not Changing**

#### **Symptom**: Interface stays in English despite language change

**Diagnosis:**
```javascript
// Check language detection
console.log('Jellyfin Lang:', document.documentElement.lang);
console.log('Browser Lang:', navigator.language);
console.log('Plugin Lang:', window.upscalerSettings?.currentLanguage);
```

**Solutions:**
```javascript
// Solution 1: Force language reload
window.upscalerSettings?.changeLanguage('de');  // For German

// Solution 2: Clear cache and reload
localStorage.removeItem('jellyfin-upscaler-settings');
location.reload();

// Solution 3: Manual language override
localStorage.setItem('jellyfin-upscaler-lang', 'de');
location.reload();
```

### **Missing Translations**

#### **Symptom**: Some text appears in English instead of selected language

**Diagnosis:**
```bash
# Check translation file
curl -s https://github.com/Kuschel-code/JellyfinUpscalerPlugin/raw/main/src/localization/languages.json | jq '.de.translations' | head -10
```

**Solution:**
```javascript
// Fallback translation function
function t(key, fallback) {
    const translations = window.upscalerSettings?.translations || {};
    return translations[key] || fallback || key;
}
```

---

## ⚡ **Performance Issues**

### **Low FPS / Stuttering**

#### **Symptom**: Video playback becomes choppy with AI enhancement

**Diagnosis Tools:**
```javascript
// Performance monitoring
const performanceMonitor = {
    checkGPUUsage: () => {
        // GPU usage should be 70-85% for optimal performance
        return navigator.gpu?.getGPUUsage?.() || 'unknown';
    },
    
    checkMemoryUsage: () => {
        if (performance.memory) {
            const used = performance.memory.usedJSHeapSize;
            const total = performance.memory.totalJSHeapSize;
            return `${(used/total*100).toFixed(1)}% memory used`;
        }
    },
    
    checkFrameRate: () => {
        const video = document.querySelector('video');
        return video ? `${video.getVideoPlaybackQuality?.().totalVideoFrames || 'unknown'} frames` : 'no video';
    }
};
```

**Systematic Solutions:**

#### **Step 1: Reduce Quality Settings**
```json
{
  "performance_mode": {
    "ai_method": "fsr21",
    "scale_factor": 1.5,
    "disable_hdr": true,
    "disable_frame_interpolation": true,
    "reduce_sharpness": 0.2
  }
}
```

#### **Step 2: Hardware Optimization**
```bash
# Check thermals
sensors | grep -E "(GPU|Core)"
# GPU should be < 80°C, CPU < 75°C

# Check power limits
nvidia-smi -q -d POWER
# Power draw should be stable

# Check memory
free -h
# Available memory should be > 4GB
```

#### **Step 3: Driver Optimization**
```bash
# NVIDIA: Force performance mode
nvidia-settings -a [gpu:0]/GPUPowerMizerMode=1

# AMD: Set performance profile
echo performance | sudo tee /sys/class/drm/card0/device/power_dpm_force_performance_level

# Intel: Enable hardware scheduling
echo 1 | sudo tee /sys/module/i915/parameters/enable_guc
```

### **High GPU Temperature**

#### **Symptom**: GPU overheating during AI upscaling

**Emergency Actions:**
```json
{
  "thermal_emergency": {
    "immediate": [
      "Reduce scale factor to 1.5x",
      "Disable all enhancements",
      "Switch to CPU-only mode",
      "Pause video playback"
    ],
    "monitoring": {
      "check_temperature_every": "5 seconds",
      "emergency_shutdown_at": "90°C",
      "throttle_at": "85°C"
    }
  }
}
```

**Long-term Solutions:**
```bash
# Improve case cooling
# - Add intake/exhaust fans
# - Clean dust from GPU heatsink
# - Repaste GPU thermal compound

# Adjust fan curves
nvidia-settings -a [gpu:0]/GPUFanControlState=1
nvidia-settings -a [fan:0]/GPUTargetFanSpeed=80

# Undervolt GPU (advanced)
# Use MSI Afterburner or similar tool
```

---

## 🎬 **Video Playback Issues**

### **No Video Enhancement Visible**

#### **Symptom**: Video plays but no quality improvement seen

**Diagnosis Checklist:**
```bash
# 1. Check if enhancement is actually enabled
# Look for AI method indicator in player

# 2. Check input resolution
# Enhancement is most visible on lower-resolution sources

# 3. Check display scaling
# Make sure browser zoom is 100%

# 4. Test with known low-quality video
# Use 480p or 720p source for obvious improvement
```

**Solutions:**
```javascript
// Force enable enhancement
window.upscalerSettings?.applyProfile('maximum_quality');

// Test with extreme settings for visibility
const testSettings = {
    ai_method: 'real_esrgan',
    scale_factor: 4.0,
    sharpness: 1.0,
    saturation: 2.0,
    contrast: 2.0
};
```

### **Video Artifacts/Distortion**

#### **Symptom**: Weird visual artifacts, halos, or distortion

**Common Causes & Fixes:**

#### **Over-Sharpening Artifacts:**
```json
{
  "problem": "Halos around edges, unnatural sharpness",
  "solution": {
    "reduce_sharpness": 0.3,
    "enable_artifact_reduction": true,
    "use_conservative_preset": true
  }
}
```

#### **AI Model Artifacts:**
```json
{
  "problem": "Strange textures, unrealistic details",
  "solution": {
    "switch_ai_method": "Use FSR instead of aggressive AI",
    "reduce_scale_factor": "Lower from 4x to 2x",
    "enable_temporal_consistency": true
  }
}
```

#### **Color Artifacts:**
```json
{
  "problem": "Weird colors, oversaturation",
  "solution": {
    "reduce_saturation": 1.0,
    "disable_hdr_enhancement": true,
    "check_color_space": "Ensure proper sRGB/Rec.709"
  }
}
```

### **Audio/Video Sync Issues**

#### **Symptom**: Audio and video become out of sync

**Quick Fixes:**
```javascript
// Resync audio/video
const video = document.querySelector('video');
video.currentTime = video.currentTime;

// Reset player
video.load();

// Disable frame interpolation (common cause)
window.upscalerSettings?.updateSetting('frame_interpolation', false);
```

---

## 🌊 **Browser & Compatibility Issues**

### **Browser-Specific Problems**

#### **Chrome/Edge Issues:**
```javascript
// Common Chrome problems
const chromeFixes = {
    hardware_acceleration: {
        check: "chrome://settings/system",
        fix: "Enable 'Use hardware acceleration when available'"
    },
    
    memory_issues: {
        check: "chrome://settings/system", 
        fix: "Increase memory allocation or close other tabs"
    },
    
    webgl_issues: {
        check: "chrome://gpu/",
        fix: "Ensure WebGL is enabled and hardware accelerated"
    }
};
```

#### **Firefox Issues:**
```javascript
// Firefox-specific fixes
const firefoxFixes = {
    webgl_disabled: {
        setting: "webgl.disabled",
        value: false,
        location: "about:config"
    },
    
    hardware_acceleration: {
        setting: "layers.acceleration.force-enabled", 
        value: true,
        location: "about:config"
    }
};
```

#### **Safari Issues:**
```javascript
// Safari limitations and workarounds
const safariLimitations = {
    webgl_support: "Limited WebGL 2.0 support",
    workaround: "Use basic upscaling methods only",
    
    memory_limits: "Stricter memory management",
    workaround: "Reduce scale factor and disable advanced features"
};
```

### **Mobile Browser Issues**

#### **iOS Safari:**
```json
{
  "common_issues": [
    "WebGL context limits",
    "Memory constraints",
    "iOS power management"
  ],
  "solutions": [
    "Use mobile-optimized profile",
    "Reduce scale factor to 1.5x",
    "Disable advanced AI methods"
  ]
}
```

#### **Android Chrome:**
```json
{
  "optimizations": [
    "Enable hardware acceleration in Chrome flags",
    "Close background apps to free memory",
    "Use FSR instead of intensive AI methods"
  ]
}
```

---

## 🔒 **Security & Privacy Issues**

### **Blocked by Browser Security**

#### **Symptom**: Features blocked by CORS, CSP, or other security policies

**Solutions:**
```javascript
// Check for security blocks
console.log('Security errors:');
console.log(document.querySelectorAll('[csp-violation]'));

// Workarounds (if needed)
const securityWorkarounds = {
    cors_issues: "Use local processing instead of remote APIs",
    csp_violations: "Request administrator to update CSP policy",
    mixed_content: "Ensure all resources use HTTPS"
};
```

### **Privacy Concerns**

#### **Data Collection Audit:**
```json
{
  "data_privacy_status": {
    "telemetry": "Disabled by default",
    "usage_analytics": "Optional, user-controlled",
    "cloud_processing": "Never used - all local",
    "user_data": "Stored locally only",
    "third_party_sharing": "None"
  }
}
```

---

## 📱 **Platform-Specific Issues**

### **Windows Issues**

#### **Windows Defender Blocking:**
```powershell
# Add exclusions for Jellyfin and plugin
Add-MpPreference -ExclusionPath "C:\ProgramData\Jellyfin"
Add-MpPreference -ExclusionPath "C:\Program Files\Jellyfin"

# Check Windows Event Log
Get-EventLog -LogName Application -Source "Jellyfin*" -Newest 10
```

#### **Windows GPU Scheduling:**
```powershell
# Enable Hardware-accelerated GPU scheduling
# Settings > Display > Graphics > Change default graphics settings
# Turn ON "Hardware-accelerated GPU scheduling"
```

### **Linux Issues**

#### **Permission Problems:**
```bash
# Fix SELinux issues (if applicable)
sudo setsebool -P httpd_can_network_connect 1

# Check systemd service status
sudo systemctl status jellyfin
sudo journalctl -u jellyfin --no-pager -l

# Fix plugin permissions
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/
sudo chmod -R 755 /var/lib/jellyfin/plugins/
```

#### **Missing Dependencies:**
```bash
# Install required libraries
sudo apt update
sudo apt install libgl1-mesa-glx libglu1-mesa libnvidia-gl-* libnvidia-compute-*

# For hardware acceleration
sudo apt install vainfo intel-media-va-driver mesa-va-drivers
```

### **macOS Issues**

#### **Gatekeeper Blocking:**
```bash
# Allow plugin to run
sudo spctl --master-disable
# Or specifically allow the plugin
xattr -rd com.apple.quarantine /path/to/plugin/
```

#### **Metal Performance:**
```bash
# Check Metal support
system_profiler SPDisplaysDataType | grep Metal
# Should show Metal support: Yes
```

---

## 🐳 **Docker Issues**

### **GPU Passthrough Problems**

#### **NVIDIA Docker Issues:**
```bash
# Check NVIDIA Docker runtime
docker run --rm --gpus all nvidia/cuda:11.0-base nvidia-smi

# Fix runtime configuration
# Add to /etc/docker/daemon.json:
{
  "default-runtime": "nvidia",
  "runtimes": {
    "nvidia": {
      "path": "nvidia-container-runtime",
      "runtimeArgs": []
    }
  }
}

# Restart Docker
sudo systemctl restart docker
```

#### **AMD GPU Docker:**
```bash
# Mount GPU devices
docker run -it --device=/dev/dri --device=/dev/kfd your-jellyfin-container

# Check ROCm support
docker run --rm -it --device=/dev/dri rocm/rocm-terminal rocm-smi
```

### **Container Permission Issues**

```bash
# Fix UID/GID mapping
docker run -e PUID=1000 -e PGID=1000 your-jellyfin-container

# Mount plugin directory with correct permissions
docker run -v ./plugins:/config/plugins:rw,Z your-jellyfin-container
```

---

## 🔍 **Advanced Diagnostics**

### **Comprehensive System Check**

```bash
#!/bin/bash
# Advanced diagnostic script

echo "=== Jellyfin AI Upscaler Diagnostics ==="

# System Info
echo "## System Information"
uname -a
lsb_release -a 2>/dev/null || cat /etc/os-release

# GPU Info
echo "## GPU Information"
lspci | grep -E "(VGA|3D|Display)"
nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv 2>/dev/null
rocm-smi --showproductname --showmeminfo 2>/dev/null

# Jellyfin Status
echo "## Jellyfin Status"
systemctl status jellyfin --no-pager
ls -la /var/lib/jellyfin/plugins/

# Plugin Status
echo "## Plugin Status"
find /var/lib/jellyfin/plugins/ -name "*Upscaler*" -type d
cat /var/lib/jellyfin/plugins/JellyfinUpscaler*/meta.json 2>/dev/null

# Browser Checks
echo "## Browser Compatibility"
echo "User Agent: $(curl -s 'http://httpbin.org/user-agent' | jq -r .user-agent)"

# Network Tests
echo "## Network Connectivity"
curl -I https://github.com/Kuschel-code/JellyfinUpscalerPlugin/raw/main/dist/JellyfinUpscaler-Advanced.zip

echo "=== Diagnostics Complete ==="
```

### **Performance Profiling**

```javascript
// Browser performance profiling
const profiler = {
    startProfiling: () => {
        console.time('Upscaler Performance');
        performance.mark('upscaler-start');
    },
    
    measureStep: (stepName) => {
        performance.mark(`upscaler-${stepName}`);
        performance.measure(stepName, 'upscaler-start', `upscaler-${stepName}`);
    },
    
    endProfiling: () => {
        console.timeEnd('Upscaler Performance');
        const entries = performance.getEntriesByType('measure');
        console.table(entries);
    }
};
```

---

## 🆘 **Getting Help**

### **Before Asking for Help**

Collect this information:
```bash
# System Information
uname -a
lsb_release -a || cat /etc/os-release

# GPU Information  
nvidia-smi || rocm-smi || intel_gpu_top
lspci | grep -E "(VGA|3D)"

# Jellyfin Information
sudo journalctl -u jellyfin --since "1 hour ago" | grep -i error

# Plugin Information
ls -la /var/lib/jellyfin/plugins/JellyfinUpscaler*/
cat /var/lib/jellyfin/plugins/JellyfinUpscaler*/meta.json

# Browser Information
# Include browser version, enabled extensions, console errors
```

### **How to Report Issues**

1. **GitHub Issues**: [Report bugs and request features](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues)
2. **Include System Info**: Use the diagnostic script above
3. **Describe Steps**: Exact steps to reproduce the problem
4. **Screenshots**: Visual problems need visual evidence
5. **Log Files**: Always include relevant log entries

---

## 🎯 **Issue Resolution Priority**

### **Critical Issues (Fix Immediately)**
- Plugin completely non-functional
- Security vulnerabilities
- Data corruption/loss
- System crashes/freezes

### **High Priority Issues**
- Major performance problems
- Hardware compatibility issues
- Core features not working
- Installation failures

### **Medium Priority Issues**
- UI/UX problems
- Minor performance issues
- Feature requests
- Localization problems

### **Low Priority Issues**
- Cosmetic issues
- Enhancement requests
- Documentation updates
- Code optimizations

---

**🛠️ Remember: Most issues have simple solutions. Start with the basics (restart, reinstall, update) before diving into complex troubleshooting!**
>>>>>>> fb710c41083708d3f59b200a8aea080fe8d2abcb
