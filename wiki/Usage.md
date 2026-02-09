# 🎮 Usage Guide

How to use the AI Upscaler with Jellyfin for daily viewing.

---

## Automatic Upscaling

Once configured, the plugin automatically upscales video during playback:

1. **Start any video** in Jellyfin
2. The AI Upscaler processes frames via the Docker container
3. Enhanced video is displayed with improved resolution and detail

---

## Player Controls

### AI Button
When enabled, an **AI** button appears in the Jellyfin video player controls:

- **Click to toggle** AI upscaling on/off during playback
- **Quick menu** allows switching models and scale factors without leaving the player
- Works with keyboard, mouse, and TV remotes

### Enabling the AI Button
1. **Dashboard → Plugins → AI Upscaler → Settings**
2. Enable **"Show Player Button"**
3. Choose **Button Position** (left or right)
4. Refresh your browser (Ctrl+F5)

---

## Side-by-Side Preview

Test AI models before enabling them for playback:

1. Go to **Plugin Settings → AI Comparison Preview**
2. Select a movie or episode from the dropdown
3. Click **"✨ Generate Preview"**
4. Compare original (left) vs. enhanced (right) side by side
5. Try different models and scale factors to find the best fit

---

## Pre-Processing (Recommended for NAS)

For systems without a powerful GPU, pre-process your library:

1. Enable **Pre-Processing Cache** in settings
2. The plugin will process videos in the background
3. Pre-cached content plays instantly with AI enhancement

---

## Dashboard Monitoring

Access the AI Upscaler Dashboard from the Jellyfin sidebar:

- **Active Jobs** – Currently processing upscale tasks
- **Hardware Status** – CPU/GPU utilization and temperature
- **Connection Status** – Docker and SSH connection health
- **Statistics** – Total frames processed, average FPS

---

## Web UI (Docker)

The Docker container includes a Web UI at `http://YOUR_SERVER:5000`:

- **Model Management** – Upload, enable, and configure AI models
- **Health Status** – Container health and GPU detection
- **API Docs** – FastAPI auto-generated documentation at `/docs`
