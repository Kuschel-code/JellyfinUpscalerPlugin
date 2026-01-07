# ⚙️ Quick Settings UI Guide

## 🎯 **Accessing Quick Settings**

### **In-Player Controls**
1. **Play any video** in Jellyfin web client
2. **Look for Quick Settings button** (⚙️ icon) in top-right area
3. **Click to open** the AI Upscaler panel
4. **Touch users**: Tap and swipe for mobile controls

### **Button Location**
- **Desktop**: Top-right corner of video player
- **Mobile**: Swipe from right edge or tap ⚙️ icon
- **TV/Console**: Navigate with remote/controller

---

## 🎮 **Smart Presets**

### **🎮 Gaming Preset**
**Perfect for**: Game recordings, streaming, high-action content

**Settings:**
- **Resolution**: 4K (3840x2160)
- **Codec**: AV1 (if supported) / HEVC fallback
- **Quality**: CRF 20-23 (high quality)
- **Sharpness**: 75% (crisp details)
- **HDR**: HDR10 passthrough
- **Audio**: 7.1 Surround passthrough
- **Frame Rate**: Up to 120fps support

**Best for Hardware:**
- NVIDIA RTX 4070+
- Intel Arc A750+
- AMD RX 7800 XT+ (HEVC mode)

### **🍎 Apple TV Preset**  
**Perfect for**: Movies, TV shows, cinematic content

**Settings:**
- **Resolution**: 4K (3840x2160)
- **Codec**: AV1 with Dolby Vision support
- **Quality**: CRF 23-25 (cinematic quality)
- **Sharpness**: 60% (natural look)
- **HDR**: Dolby Vision / HDR10+ 
- **Audio**: 5.1 Dolby Atmos passthrough
- **Film Grain**: 10-15 (adds texture)

**Best for Hardware:**
- NVIDIA RTX 4080+
- Intel Arc A770
- High-end systems with excellent cooling

### **📱 Mobile Preset**
**Perfect for**: Phones, tablets, battery-powered devices

**Settings:**  
- **Resolution**: 1080p (1920x1080)
- **Codec**: H.264 (compatibility)
- **Quality**: CRF 26-28 (balanced)
- **Sharpness**: 40% (mobile screens)
- **HDR**: SDR conversion
- **Audio**: Stereo / Headphone optimization
- **Battery Mode**: Enabled

**Best for Hardware:**
- Any modern mobile device
- Integrated graphics
- Battery-powered laptops

### **🖥️ Server Preset**
**Perfect for**: Remote streaming, multiple users, server efficiency

**Settings:**
- **Resolution**: 1440p (2560x1440) 
- **Codec**: HEVC (compatibility)
- **Quality**: CRF 25-27 (balanced)
- **Sharpness**: 50% (neutral)
- **HDR**: Auto-detection
- **Audio**: Passthrough (preserve original)
- **Efficiency**: Maximum concurrent streams

**Best for Hardware:**
- Server GPUs
- Multi-user environments
- Remote streaming scenarios

---

## 🎛️ **Manual Controls**

### **Video Quality Settings**

#### **Resolution Selector**
- **Auto**: Intelligent upscaling based on source
- **720p**: Basic enhancement
- **1080p**: Standard quality  
- **1440p**: High quality
- **4K**: Maximum quality (GPU dependent)

#### **Sharpness Control** (0-100%)
- **0-30%**: Soft, natural look
- **40-60%**: Balanced (recommended)  
- **70-80%**: Crisp, enhanced details
- **90-100%**: Maximum sharpness (may introduce artifacts)

#### **Quality/Speed Balance**
- **Ultrafast**: Fastest processing, lower quality
- **Fast**: Quick processing, good quality
- **Medium**: Balanced (recommended)  
- **Slow**: Better quality, slower processing
- **Veryslow**: Maximum quality, very slow

### **Audio Controls**

#### **Audio Mode**
- **Passthrough**: Preserve original audio
- **Stereo**: Convert to 2-channel
- **5.1**: Surround sound
- **7.1**: Extended surround  
- **Auto**: Based on device capabilities

#### **Audio Quality**
- **Copy**: Bit-for-bit copy (lossless)
- **High**: Minimal compression
- **Medium**: Balanced compression
- **Low**: Maximum compression

### **Advanced Settings**

#### **HDR Processing**
- **Auto**: Detect and preserve HDR
- **HDR10**: Force HDR10 output
- **Dolby Vision**: Process DV content
- **SDR**: Convert to standard dynamic range

#### **Frame Rate Options**
- **Match Source**: Keep original frame rate
- **24fps**: Cinema standard
- **30fps**: TV standard
- **60fps**: Smooth motion
- **120fps**: High refresh rate (gaming)

---

## 📱 **Touch Controls**

### **Mobile Gestures**
- **Tap**: Select preset or toggle setting
- **Swipe Left/Right**: Adjust sliders (sharpness, quality)
- **Long Press**: Access advanced options
- **Pinch**: Zoom preview (if available)
- **Double Tap**: Toggle AI processing on/off

### **Touch-Optimized Elements**
- **Large buttons**: Finger-friendly sizes
- **Haptic feedback**: Tactile responses
- **Swipe gestures**: Easy navigation
- **Auto-hide**: UI disappears during playback

---

## 🔋 **Battery & Performance**

### **Battery Mode** (Mobile Devices)
**Auto-activates when:**
- Battery level < 50%
- Device temperature > 40°C
- Mobile device detected

**Changes applied:**
- Switch to H.264 codec
- Reduce to 1080p max resolution  
- Lower quality settings (faster processing)
- Limit concurrent processing
- Enable thermal throttling

### **Performance Monitoring**
- **Real-time FPS**: Current processing speed
- **Temperature**: GPU/CPU thermal status
- **Progress**: Completion percentage
- **Quality metrics**: PSNR, SSIM values

---

## 🎨 **Visual Indicators**

### **Status Icons**
- 🔥 **Red**: High performance mode active
- ⚡ **Yellow**: Balanced processing
- 🟢 **Green**: Power-saving mode
- 🔋 **Battery**: Mobile optimization active
- 🌡️ **Thermometer**: Thermal throttling

### **Quality Indicators**
- **★★★★★**: Maximum quality (slow)
- **★★★★☆**: High quality (recommended)
- **★★★☆☆**: Balanced quality/speed
- **★★☆☆☆**: Fast processing (lower quality)
- **★☆☆☆☆**: Fastest (basic enhancement)

---

## ⚡ **Quick Actions**

### **One-Touch Toggles**
- **🔘 AI Enhancement**: Enable/disable upscaling
- **🎮 Gaming Mode**: Switch to gaming preset
- **🔋 Battery Mode**: Enable power saving
- **🌙 Night Mode**: Dark UI for evening use
- **📱 Mobile Mode**: Touch optimization

### **Keyboard Shortcuts** (Desktop)
- **Spacebar**: Toggle AI processing
- **1-4**: Switch presets (Gaming, Apple TV, Mobile, Server)  
- **↑/↓**: Adjust sharpness
- **←/→**: Adjust quality
- **Esc**: Close Quick Settings

---

## 🎯 **Content-Aware Features**

### **Automatic Detection**
The plugin automatically detects and optimizes for:

#### **Content Types**
- **🎬 Movies**: Cinematic quality, film grain preservation
- **📺 TV Shows**: Balanced settings for series content
- **🎮 Games**: High sharpness, low latency
- **🎭 Anime**: Enhanced for animated content

#### **Source Quality**  
- **4K → 4K**: Enhancement and codec conversion
- **1080p → 4K**: AI upscaling with smart interpolation
- **720p → 1080p/4K**: Aggressive upscaling
- **480p → 1080p**: Maximum enhancement

### **Smart Adjustments**
- **Low resolution source**: Increased sharpness
- **High motion content**: Motion compensation
- **Dark scenes**: Shadow enhancement
- **Bright scenes**: Highlight preservation

---

## 📊 **Real-Time Statistics**

### **Performance Metrics**
- **Processing Speed**: X.Xx realtime
- **Quality Score**: PSNR/SSIM values
- **File Size**: Original vs processed
- **Time Remaining**: ETA for completion

### **Hardware Status**
- **GPU Usage**: Utilization percentage  
- **Temperature**: Current thermal status
- **VRAM**: Memory usage and availability
- **Power**: Consumption estimates

---

## 🛠️ **Troubleshooting Quick Settings**

### **UI Not Appearing**
1. **Check browser console** (F12) for JavaScript errors
2. **Disable ad blockers** on Jellyfin domain
3. **Clear browser cache** and reload
4. **Update browser** to latest version

### **Touch Not Working**
1. **Enable touch support** in browser
2. **Check touch events** in developer tools
3. **Try different gestures** (tap vs swipe)
4. **Restart Jellyfin** if persistent

### **Settings Not Saving**
1. **Check plugin permissions** in Jellyfin
2. **Verify API connectivity** to `/api/upscaler/`
3. **Clear browser storage** for Jellyfin domain
4. **Check Jellyfin logs** for errors

---

## 🎓 **Pro Tips**

### **Best Practices**
- **Start with presets** before manual tuning
- **Monitor temperature** during heavy processing
- **Use battery mode** on mobile devices
- **Test different content types** to find optimal settings

### **Performance Optimization**
- **Gaming**: Prioritize speed over quality for live content
- **Movies**: Use slower, higher quality settings
- **Mobile**: Always enable battery optimizations
- **Server**: Balance quality with concurrent user capacity

### **Quality vs Speed**
- **Fast processing needed**: Use Gaming or Mobile preset
- **Best quality wanted**: Use Apple TV preset with slow encoder
- **Balanced approach**: Server preset with medium encoder
- **Testing/demo**: Start with auto settings and adjust