# ⚡ Quick Start

Get up and running in under 5 minutes.

---

## 1. Start Docker (30 seconds)

```bash
docker run -d --name jellyfin-ai-upscaler \
  -p 5000:5000 -p 2222:22 \
  kuscheltier/jellyfin-ai-upscaler:1.5.1-cpu
```

> Use `:1.5.1` for NVIDIA, `:1.5.1-amd` for AMD, `:1.5.1-intel` for Intel

## 2. Install Plugin (1 minute)

1. Jellyfin Dashboard → **Plugins** → **Repositories** → **Add**
2. URL: `https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/manifest.json`
3. **Catalog** → Install **AI Upscaler Plugin** → **Restart Jellyfin**

## 3. Configure (30 seconds)

1. **Dashboard → Plugins → AI Upscaler → Settings**
2. Set **AI Service URL**: `http://YOUR_DOCKER_IP:5000`
3. Click **Test Connection** → ✅
4. **Save**

## 4. Enjoy! 🎬

Start playing any video and use the **AI** button in the player controls to upscale.

---

## Want More?

- [📥 Full Installation Guide](Installation)
- [🐳 Docker Setup](Docker-Setup)
- [🔐 SSH Remote Transcoding](SSH-Remote-Transcoding)
- [⚙️ Configuration Reference](Configuration)
