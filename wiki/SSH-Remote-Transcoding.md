# 🔐 SSH Remote Transcoding

Remote Transcoding via SSH allows Jellyfin to offload FFmpeg processing to the Docker container. This enables GPU-accelerated transcoding on a separate machine.

---

## How It Works

```
┌─────────────────┐         SSH (Port 2222)         ┌──────────────────────┐
│   Jellyfin      │ ───────────────────────────────►│  Docker Container    │
│   (Host)        │                                  │  (AI Upscaler)       │
│                 │◄───────────────────────────────│                      │
│   Wrapper.ps1   │    FFmpeg Output + Stderr        │   /usr/bin/ffmpeg    │
└─────────────────┘                                  └──────────────────────┘
```

1. Jellyfin calls the FFmpeg wrapper script (installed by plugin)
2. The wrapper translates local paths to remote container paths
3. SSH executes FFmpeg inside the Docker container
4. Output streams back to Jellyfin

---

## Step 1: Ensure SSH Port is Exposed

Your Docker container must expose port 22 (mapped to host port 2222):

```bash
docker run -d \
  --name jellyfin-ai-upscaler \
  -p 5000:5000 \
  -p 2222:22 \      # ← This enables SSH
  -v /media:/media:ro \
  -v /transcode:/transcode \
  kuscheltier/jellyfin-ai-upscaler:1.5.1
```

---

## Step 2: Setup SSH Keys

### Generate SSH Key (on Jellyfin host)

**Windows (PowerShell):**
```powershell
ssh-keygen -t rsa -b 4096 -f "$env:USERPROFILE\.ssh\jellyfin_upscaler_key" -N '""'
```

**Linux / macOS:**
```bash
ssh-keygen -t rsa -b 4096 -f ~/.ssh/jellyfin_upscaler_key -N ""
```

### Copy Public Key to Container

```bash
# Copy the public key into the container
docker exec -i jellyfin-ai-upscaler bash -c \
  'mkdir -p /root/.ssh && cat >> /root/.ssh/authorized_keys' \
  < ~/.ssh/jellyfin_upscaler_key.pub

# Set correct permissions
docker exec jellyfin-ai-upscaler chmod 600 /root/.ssh/authorized_keys
docker exec jellyfin-ai-upscaler chmod 700 /root/.ssh
```

### Test SSH Connection

```bash
ssh -i ~/.ssh/jellyfin_upscaler_key \
  -p 2222 \
  -o StrictHostKeyChecking=no \
  root@localhost "echo 'SSH works!'"
```

Expected output: `SSH works!`

---

## Step 3: Configure Plugin

Open **Jellyfin Dashboard → Plugins → AI Upscaler → Settings** and fill in:

| Setting | Value | Example |
|---------|-------|---------|
| **Enable Remote Transcoding** | ☑ Checked | ☑ |
| **Remote Host** | Docker host IP | `192.168.1.100` or `localhost` |
| **SSH Port** | Mapped SSH port | `2222` |
| **SSH User** | Container user | `root` |
| **SSH Key File** | Path to private key | `C:\Users\Admin\.ssh\jellyfin_upscaler_key` |
| **Local Media Path** | Jellyfin media path | `C:\Media` or `/media` |
| **Remote Media Path** | Container media mount | `/media` |
| **Transcode Path** | Container transcode dir | `/transcode` |

### Click "Test SSH Connection"

The button sends a test command via SSH. You should see:
- ✅ **SSH Connection successful!** – Ready to use
- ❌ **SSH Connection failed** – Check settings below

---

## Step 4: Path Mapping

Path mapping is **critical**. It translates file paths between your Jellyfin server and the Docker container.

### Example: Windows Jellyfin → Linux Docker

```
Jellyfin Server (Windows):                Docker Container (Linux):
C:\Media\Movies\Movie.mkv          →      /media/Movies/Movie.mkv
C:\Media\TV Shows\Show\S01E01.mkv  →      /media/TV Shows/Show/S01E01.mkv
```

**Settings:**
- Local Media Path: `C:\Media`
- Remote Media Path: `/media`

### Example: Linux Jellyfin → Linux Docker

```
Jellyfin Server:                    Docker Container:
/srv/media/Movies/Movie.mkv   →    /media/Movies/Movie.mkv
```

**Settings:**
- Local Media Path: `/srv/media`
- Remote Media Path: `/media`

---

## Persistent SSH Keys with Docker Compose

To keep SSH keys across container restarts:

```yaml
services:
  ai-upscaler:
    image: kuscheltier/jellyfin-ai-upscaler:1.5.1
    volumes:
      - ./ssh-keys:/root/.ssh          # Persistent SSH keys
      - /path/to/media:/media:ro
      - /path/to/transcode:/transcode
    ports:
      - "5000:5000"
      - "2222:22"
```

Then copy your public key to `./ssh-keys/authorized_keys`.

---

## Troubleshooting

### "Connection refused"
```bash
# Check if SSH is running inside container
docker exec jellyfin-ai-upscaler ps aux | grep sshd

# Restart SSHD
docker exec jellyfin-ai-upscaler /usr/sbin/sshd
```

### "Permission denied (publickey)"
```bash
# Verify authorized_keys exists
docker exec jellyfin-ai-upscaler cat /root/.ssh/authorized_keys

# Check file permissions
docker exec jellyfin-ai-upscaler ls -la /root/.ssh/
# Should show: drwx------ .ssh, -rw------- authorized_keys
```

### "Host key verification failed"
```bash
# Remove old host key
ssh-keygen -R "[localhost]:2222"

# Connect with strict checking disabled
ssh -o StrictHostKeyChecking=no -i ~/.ssh/jellyfin_upscaler_key -p 2222 root@localhost
```

### FFmpeg not found on remote
```bash
# Check ffmpeg location in container
docker exec jellyfin-ai-upscaler which ffmpeg
docker exec jellyfin-ai-upscaler ffmpeg -version
```

---

## Security Recommendations

| Setting | Development | Production |
|---------|------------|------------|
| Root Login | ✅ OK for testing | ❌ Create dedicated user |
| Password Auth | Disabled | Disabled |
| Key-based Auth | ✅ Recommended | ✅ Required |
| StrictHostKeyChecking | `no` | `yes` |
| Firewall SSH Port | Open | Restrict to Jellyfin IP |
