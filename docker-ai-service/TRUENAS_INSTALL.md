# Docker Hub & TrueNAS Installation Guide

## 🐳 Step 1: Push to Docker Hub

### On Windows (where you built the image)

```powershell
# 1. Login to Docker Hub
docker login

# 2. Tag the image with your Docker Hub username
docker tag jellyfin-ai-upscaler:latest YOUR_DOCKERHUB_USERNAME/jellyfin-ai-upscaler:latest
docker tag jellyfin-ai-upscaler:latest YOUR_DOCKERHUB_USERNAME/jellyfin-ai-upscaler:1.1.0

# 3. Push to Docker Hub
docker push YOUR_DOCKERHUB_USERNAME/jellyfin-ai-upscaler:latest
docker push YOUR_DOCKERHUB_USERNAME/jellyfin-ai-upscaler:1.1.0
```

Replace `YOUR_DOCKERHUB_USERNAME` with your actual Docker Hub username.

---

## 📦 Step 2: TrueNAS Installation

### Option A: TrueNAS SCALE (Recommended - with Apps)

1. Go to **Apps** → **Discover Apps** → **Custom App**
2. Fill in:
   - **Application Name**: `jellyfin-ai-upscaler`
   - **Image Repository**: `YOUR_DOCKERHUB_USERNAME/jellyfin-ai-upscaler`
   - **Image Tag**: `latest`
3. Port Configuration:
   - **Container Port**: `5000`
   - **Node Port**: `5000`
4. Storage (Optional but recommended):
   - Mount `/app/models` to a dataset for persistent model storage
5. Click **Install**

### Option B: Docker Compose via SSH

1. SSH into TrueNAS:
   ```bash
   ssh root@YOUR_TRUENAS_IP
   ```

2. Create directory and docker-compose.yml:
   ```bash
   mkdir -p /mnt/pool/docker/jellyfin-ai-upscaler
   cd /mnt/pool/docker/jellyfin-ai-upscaler
   ```

3. Create docker-compose.yml:
   ```yaml
   version: "3.8"
   services:
     ai-upscaler:
       image: YOUR_DOCKERHUB_USERNAME/jellyfin-ai-upscaler:latest
       container_name: jellyfin-ai-upscaler
       ports:
         - "5000:5000"
       volumes:
         - ./models:/app/models
       environment:
         - USE_GPU=false  # Set to true if you have NVIDIA GPU passthrough
         - MAX_CONCURRENT_REQUESTS=4
       restart: unless-stopped
   ```

4. Start the container:
   ```bash
   docker-compose up -d
   ```

### Option C: Direct Docker Run

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -v /mnt/pool/docker/ai-upscaler-models:/app/models \
  -e USE_GPU=false \
  -e MAX_CONCURRENT_REQUESTS=4 \
  --restart unless-stopped \
  YOUR_DOCKERHUB_USERNAME/jellyfin-ai-upscaler:latest
```

---

## 🔧 Step 3: Configure Jellyfin Plugin

1. In Jellyfin, go to **Dashboard** → **Plugins** → **AI Upscaler Plugin**
2. Set **AI Service URL** to: `http://YOUR_TRUENAS_IP:5000`
3. Save and restart Jellyfin

---

## ✅ Step 4: Verify Installation

1. Open in browser: `http://YOUR_TRUENAS_IP:5000`
2. You should see the AI Upscaler Web UI
3. Download a model (e.g., FSRCNN x2)
4. Load the model
5. Test with an image upload

---

## 🔄 Updating

```bash
# Pull latest image
docker pull YOUR_DOCKERHUB_USERNAME/jellyfin-ai-upscaler:latest

# Restart container
docker-compose down
docker-compose up -d

# Or if using docker run:
docker stop jellyfin-ai-upscaler
docker rm jellyfin-ai-upscaler
# Run the docker run command again
```
