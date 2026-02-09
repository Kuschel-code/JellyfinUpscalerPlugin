# 🎨 AI Models

The AI Upscaler Plugin supports multiple neural network models for different content types and performance levels.

---

## Available Models

### FSRCNN (Fast Super-Resolution CNN)
- **Best for:** General content, quick processing
- **Speed:** ⚡⚡⚡⚡⚡ Fastest
- **Quality:** ⭐⭐⭐ Good
- **VRAM:** ~200 MB
- **Scales:** 2x, 3x, 4x

### ESPCN (Efficient Sub-Pixel CNN)
- **Best for:** Real-time upscaling, low-power devices
- **Speed:** ⚡⚡⚡⚡⚡ Very Fast
- **Quality:** ⭐⭐⭐ Good
- **VRAM:** ~150 MB
- **Scales:** 2x, 3x, 4x

### LapSRN (Laplacian Pyramid SR Network)
- **Best for:** Gradual quality improvement, balanced performance
- **Speed:** ⚡⚡⚡⚡ Fast
- **Quality:** ⭐⭐⭐⭐ Very Good
- **VRAM:** ~500 MB
- **Scales:** 2x, 4x, 8x

### EDSR (Enhanced Deep SR)
- **Best for:** Maximum detail, high-end systems
- **Speed:** ⚡⚡ Slower
- **Quality:** ⭐⭐⭐⭐⭐ Excellent
- **VRAM:** ~1 GB
- **Scales:** 2x, 3x, 4x

### Real-ESRGAN
- **Best for:** Live-action movies, nature, photos
- **Speed:** ⚡⚡ Slower
- **Quality:** ⭐⭐⭐⭐⭐ Excellent
- **VRAM:** ~1.5 GB
- **Scales:** 2x, 4x

---

## Model Comparison

| Model | Speed | Quality | VRAM | Best Use |
|-------|-------|---------|------|----------|
| FSRCNN | ⚡⚡⚡⚡⚡ | ⭐⭐⭐ | 200 MB | General, real-time |
| ESPCN | ⚡⚡⚡⚡⚡ | ⭐⭐⭐ | 150 MB | Low-power devices |
| LapSRN | ⚡⚡⚡⚡ | ⭐⭐⭐⭐ | 500 MB | Balanced |
| EDSR | ⚡⚡ | ⭐⭐⭐⭐⭐ | 1 GB | High quality |
| Real-ESRGAN | ⚡⚡ | ⭐⭐⭐⭐⭐ | 1.5 GB | Movies, photos |

---

## Model Selection Guide

```
What's your priority?
├── Speed → FSRCNN or ESPCN
├── Quality → EDSR or Real-ESRGAN
└── Balanced → LapSRN
```

```
What hardware do you have?
├── High-end GPU (RTX 3060+) → Real-ESRGAN or EDSR
├── Mid-range GPU → LapSRN
├── Low-end GPU / iGPU → FSRCNN
└── CPU only → ESPCN or FSRCNN
```

---

## Managing Models

### Via Web UI
1. Open `http://YOUR_SERVER:5000` in your browser
2. Navigate to the Models section
3. Upload, enable, or disable models

### Via Docker Volume
Models are stored in `/app/models` inside the container. Mount a persistent volume:

```bash
docker run -v ai-models:/app/models kuscheltier/jellyfin-ai-upscaler:1.5.1
```

### Via Plugin Settings
1. Go to **Dashboard → Plugins → AI Upscaler → Settings**
2. Select your desired model from the **AI Model** dropdown
3. The plugin will communicate with Docker to use the selected model
