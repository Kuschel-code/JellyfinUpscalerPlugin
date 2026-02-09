# 🔍 Troubleshooting

Solutions for common problems with the AI Upscaler Plugin v1.5.1.

---

## 🐳 Docker Issues

### Container won't start
```bash
# Check container status
docker ps -a --filter name=jellyfin-ai-upscaler

# View error logs
docker logs jellyfin-ai-upscaler --tail 50
```

**Common fixes:**
- **Port conflict:** Change `-p 5000:5000` to `-p 5001:5000`
- **GPU not found:** Ensure `nvidia-container-toolkit` is installed (NVIDIA) or `/dev/dri` is accessible (Intel/AMD)
- **Image not found:** `docker pull kuscheltier/jellyfin-ai-upscaler:1.5.1`

### Health check failing
```bash
# Manual health check
curl http://localhost:5000/health

# Check if the Python app started
docker exec jellyfin-ai-upscaler ps aux | grep uvicorn
```

---

## 🔌 Plugin Issues

### Plugin shows "Not Supported"
1. **Uninstall** old versions (especially v1.4.x)
2. Delete all files in `plugins/AIUpscaler/` folder
3. **Restart Jellyfin**
4. Install fresh from repository

### Settings not saving
- **v1.5.1.0 bug:** SSH fields were not included in the save function. Update to **v1.5.1.1+**
- Check browser console (F12) for JavaScript errors
- Try a different browser or incognito mode

### "Connection refused (localhost:5000)"
1. **Docker running?** `docker ps | grep upscaler`
2. **Correct URL?** Should be `http://YOUR_DOCKER_IP:5000` (not `https://`)
3. **Network mode?** If Jellyfin runs in Docker too, use the Docker network name instead of `localhost`
4. **Firewall?** Ensure port 5000 is open

---

## 🔐 SSH Issues

### "Connection refused" on SSH
```bash
# Check SSHD is running in container
docker exec jellyfin-ai-upscaler ps aux | grep sshd

# Manually start SSHD
docker exec jellyfin-ai-upscaler /usr/sbin/sshd

# Verify port 22 is listening
docker exec jellyfin-ai-upscaler ss -tlnp | grep 22
```

### "Permission denied (publickey)"
```bash
# Check authorized_keys
docker exec jellyfin-ai-upscaler cat /root/.ssh/authorized_keys

# Fix permissions
docker exec jellyfin-ai-upscaler chmod 700 /root/.ssh
docker exec jellyfin-ai-upscaler chmod 600 /root/.ssh/authorized_keys

# Re-copy your public key
docker exec -i jellyfin-ai-upscaler bash -c \
  'cat > /root/.ssh/authorized_keys' < ~/.ssh/your_key.pub
```

### "Host key verification failed"
```bash
# Remove old host entry
ssh-keygen -R "[localhost]:2222"

# Reconnect
ssh -o StrictHostKeyChecking=no -p 2222 root@localhost
```

### Path mapping not working
- **Windows → Linux:** `Local: C:\Media` → `Remote: /media`
- **Paths are case-sensitive** on Linux!
- Verify volume mount: `docker exec jellyfin-ai-upscaler ls /media/`

---

## 🎮 Playback Issues

### AI button missing in player
1. Enable **"Show Player Button"** in settings
2. Clear browser cache: **Ctrl+Shift+Delete** → Clear
3. Hard refresh: **Ctrl+F5**
4. Check plugin is "Active" in dashboard

### Upscaling too slow / stuttering
1. **Lower Scale Factor:** 4x → 2x
2. **Lower Quality:** High → Medium or Low
3. **Check GPU usage:** `nvidia-smi` (NVIDIA) or `rocm-smi` (AMD)
4. **Enable Pre-Processing Cache** for frequently watched content

### Image artifacts (blur, ghosting)
1. Try a different AI model
2. Verify ONNX model files aren't corrupted
3. Reduce scale factor from 4x to 2x

---

## 🔧 Advanced Debugging

### Enable verbose logging
Check Jellyfin logs at **Dashboard → Logs** for entries containing `AI Upscaler`.

### Reset plugin configuration
1. Stop Jellyfin
2. Delete `JellyfinUpscalerPlugin.xml` in config folder:
   - **Windows:** `C:\ProgramData\Jellyfin\Server\config\`
   - **Linux:** `/var/lib/jellyfin/config/`
3. Start Jellyfin and reconfigure

### Check GPU inside container
```bash
# NVIDIA
docker exec jellyfin-ai-upscaler nvidia-smi

# AMD
docker exec jellyfin-ai-upscaler rocm-smi

# Intel
docker exec jellyfin-ai-upscaler python3 -c "from openvino.runtime import Core; print(Core().available_devices)"
```

---

## 📞 Still stuck?

- [GitHub Issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues) – Bug reports
- [GitHub Discussions](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/discussions) – Questions
