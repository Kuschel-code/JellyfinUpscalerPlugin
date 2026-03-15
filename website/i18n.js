/* ========================================
   Translations for all 6 languages
======================================== */
const i18n = {
    en: {
        nav: { home: "Home", installation: "Installation", sshSetup: "SSH Setup", configuration: "Configuration", features: "Features", troubleshooting: "Troubleshooting", dockerTags: "Docker Tags", changelog: "Changelog" },
        hero: {
            badge: "v1.5.2.3 — Security & Bug Fix",
            title1: "Transform your media",
            title2: "with AI.",
            subtitle: "Upscale SD to 4K using neural networks. GPU-accelerated Docker microservice for Jellyfin with support for NVIDIA, AMD, Intel & Apple Silicon.",
            getStarted: "Get Started",
            viewGithub: "View on GitHub",
            stats: { gpus: "GPU Architectures", size: "Plugin Size", upscale: "Upscaling", license: "Open Source" }
        },
        features: {
            tag: "Features",
            title1: "Everything you need.", title2: "Nothing you don't.",
            docker: { title: "Docker Microservice", desc: "AI processing runs in an isolated container — no DLL conflicts, no crashes. Only 1.6 MB plugin vs. 417 MB before." },
            ssh: { title: "SSH Remote Transcoding", desc: "Offload FFmpeg to GPU containers via SSH. Your NAS delegates transcoding to powerful hardware." },
            gpu: { title: "5 GPU Architectures", desc: "Native NVIDIA CUDA, AMD ROCm, Intel OpenVINO, Apple Silicon ARM64, and multi-threaded CPU." },
            ai: { title: "Neural Network Models", desc: "FSRCNN, ESPCN, LapSRN, EDSR, Real-ESRGAN — from lightning-fast to maximum detail." },
            ui: { title: "Seamless Integration", desc: "Player button, side-by-side preview, real-time benchmarking, and Web UI for model management." }
        },
        installation: {
            tag: "Getting Started",
            title1: "Up and running", title2: "in minutes.",
            warning: "Important Notice",
            warningText: "This plugin requires a Docker container running alongside Jellyfin. The plugin itself is only ~1.6 MB — all AI heavy lifting happens in Docker.",
            step1: "Start Docker Container",
            step1desc: "Pull and run the image that matches your GPU.",
            recommended: "Recommended",
            optionA: "Docker Hub (Pull)",
            optionB: "Build Locally",
            withGpu: "With NVIDIA GPU",
            withIntel: "With Intel GPU (Arc / Iris)",
            withAmd: "With AMD GPU (ROCm)",
            step2: "Install Plugin",
            step2desc: "Add the plugin repository to Jellyfin.",
            addRepo: "Add Repository URL",
            addRepoPath: "Dashboard → Plugins → Repositories → Add",
            installPlugin: "Install from Catalog",
            installPluginPath: "Catalog → General → AI Upscaler → Install",
            restartJellyfin: "Restart Jellyfin",
            restartText: "After installation, restart your server to activate the plugin.",
            configureUrl: "Configure AI Service URL",
            configureUrlText: "Set the Docker container URL:",
            done: "You're all set!",
            doneText: "The plugin is installed and ready. Start playing content and use the AI button in the player.",
            tip: "💡 Tip:",
            tipText: "Replace YOUR_SERVER_IP with your Docker host IP:"
        },
        configuration: {
            tag: "Settings",
            title1: "Complete control", title2: "at your fingertips.",
            basic: "Basic Settings", hardware: "Hardware", remote: "Remote Transcoding (SSH)", ui: "UI Settings", advanced: "Advanced",
            fields: {
                enable: "Enable Plugin", serviceUrl: "AI Service URL", model: "AI Model", scale: "Scale Factor", quality: "Quality Level",
                hwAccel: "Hardware Acceleration", maxVram: "Max VRAM (MB)", cpuThreads: "CPU Threads",
                enableRemote: "Enable Remote Transcoding", remoteHost: "Remote Host", sshPort: "SSH Port", sshUser: "SSH User", sshKey: "SSH Key File", localPath: "Local Media Path", remotePath: "Remote Media Path",
                showButton: "Show Player Button", buttonPos: "Button Position", notifications: "Notifications",
                comparison: "Comparison View", metrics: "Performance Metrics", cache: "Pre-Processing Cache", cacheSize: "Cache Size (MB)"
            }
        },
        troubleshooting: {
            tag: "Help",
            title1: "Common issues.", title2: "Quick fixes.",
            problems: [
                { title: "Plugin shows 'Not Supported'", desc: "The plugin fails to load in Jellyfin.", solutions: ["Uninstall old versions (v1.4.x)", "Delete old plugin folder", "Restart Jellyfin", "Install fresh from repository"] },
                { title: "Container won't start", desc: "Docker container exits immediately or keeps restarting.", solutions: ["Check logs: docker logs jellyfin-ai-upscaler", "Verify GPU drivers are installed", "Check port conflicts (5000, 2222)", "Ensure correct Docker image tag"], commands: [{ label: "Check logs", code: "docker logs jellyfin-ai-upscaler --tail 50" }, { label: "Health check", code: "curl http://localhost:5000/health" }] },
                { title: "Upscaling not working", desc: "AI button appears but upscaling fails.", solutions: ["Verify Docker container is running", "Test connection in plugin settings", "Check AI Service URL is correct", "Verify media paths are accessible"], commands: [{ label: "Test connectivity", code: "curl http://YOUR_SERVER:5000/health" }] },
                { title: "Upscale button not showing", desc: "Button missing in Jellyfin player.", solutions: ["The button only works in web browsers (Chrome, Edge, Firefox)", "It does NOT work in native apps (Windows app, mobile, TV)", "Open Jellyfin via http://YOUR_IP:8096 in a browser", "Go to Dashboard → Plugins → AI Upscaler → enable Player Button", "Hard refresh the browser: Ctrl+Shift+R"] },
                { title: "BadImageFormatException", desc: "Assembly load error with native DLLs.", solutions: ["This is the old v1.4.x issue", "Upgrade to v1.5.0+ (Docker)", "Remove ALL old DLLs from plugin folder"] },
                { title: "GPU Not Detected", desc: "Container runs in CPU mode despite GPU available.", solutions: ["NVIDIA: Install nvidia-container-toolkit, use --gpus all", "Intel: Use :1.5.4-intel tag with --device=/dev/dri", "AMD: Use :1.5.4-amd tag with --device=/dev/kfd --device=/dev/dri", "Windows: Use :1.5.4-cpu (GPU passthrough not supported in Docker Desktop)"], commands: [{ label: "Test NVIDIA", code: "docker run --rm --gpus all nvidia/cuda:12.2.2-base-ubuntu22.04 nvidia-smi" }, { label: "Test Intel", code: "docker exec jellyfin-ai-upscaler ls -la /dev/dri/" }] },
                { title: "Windows + Intel/AMD GPU", desc: "GPU images don't work on Windows Docker Desktop.", solutions: ["--device=/dev/dri is Linux-only (not available in WSL2)", "Use the CPU image: :1.5.4-cpu", "For GPU acceleration, run Docker on a Linux host", "CPU mode still produces excellent results, just slower"], commands: [{ label: "Windows CPU command", code: "docker run -d --name jellyfin-ai-upscaler -p 5000:5000 -p 2222:22 kuscheltier/jellyfin-ai-upscaler:1.5.4-cpu" }] },
                { title: "Proxmox LXC GPU Passthrough", desc: "Intel/AMD GPU not detected in Proxmox LXC container.", solutions: ["Add to LXC config: lxc.cgroup2.devices.allow: c 226:* rwm", "Add: lxc.mount.entry: /dev/dri dev/dri none bind,optional,create=dir", "Use --device=/dev/dri --group-add=render in Docker", "Verify: ls -la /dev/dri/ inside the LXC"], commands: [{ label: "Verify GPU in LXC", code: "ls -la /dev/dri/ && docker exec jellyfin-ai-upscaler python3 -c 'import onnxruntime; print(onnxruntime.get_available_providers())'" }] },
                { title: "SSH Connection Failed", desc: "Cannot connect to Docker via SSH.", solutions: ["Verify SSHD is running in container", "Check authorized_keys permissions", "Confirm port 2222 is mapped", "Remove old host key: ssh-keygen -R [localhost]:2222"], commands: [{ label: "Check SSHD", code: "docker exec jellyfin-ai-upscaler ps aux | grep sshd" }] }
            ],
            solution: "Solution",
            commands: "Useful Commands",
            needHelp: "Still need help?",
            githubIssues: "GitHub Issues",
            githubWiki: "GitHub Wiki"
        },
        dockerTags: {
            tag: "Docker",
            title1: "Choose your", title2: "image.",
            cards: [
                { brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.4", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" },
                { brand: "AMD", tech: "ROCm", tag: ":1.5.4-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" },
                { brand: "Intel", tech: "OpenVINO", tag: ":1.5.4-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" },
                { brand: "Apple", tech: "ARM64 Optimized", tag: ":1.5.4-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" },
                { brand: "CPU", tech: "Multi-Thread", tag: ":1.5.4-cpu", models: "Any x86 / ARM64", rating: 2, color: "#6366f1" }
            ]
        },
        changelog: {
            tag: "History",
            title1: "What's", title2: "new.",
            versions: [
                { ver: "1.5.2.3", date: "Mar 2026", type: "Fix", items: ["Fixed: Player upscale button not showing (Issue #45)", "Global script injection into index.html (like Intro Skipper)", "Rewritten player-integration.js for Jellyfin 10.11+ SPA", "Fixed: Intel OpenVINO GPU falling back to CPU", "Docker 1.5.4: Updated Intel compute-runtime + GPU_FP32 targeting"] },
                { ver: "1.5.2.1", date: "Mar 2026", type: "Security", items: ["Security: SSH command injection prevention (regex + ArgumentList)", "Security: Path traversal protection for media paths", "Security: SSH hardening — key-only auth, conditional sshd start", "Fixed: Progress tracking (was stuck at 0%/50%)", "Fixed: Pause/resume job ID resolution", "Synced: Model list aligned (14 models) between plugin & backend", "Docker 1.5.3: Scale validation, async I/O, pinned AMD base image"] },
                { ver: "1.5.2.0", date: "Feb 2026", type: "Fix", items: ["Fixed: GPU acceleration not working (Issue #44)", "Upgraded Docker base to cuDNN 9 (CUDA 12.6)", "Intelligent ONNX provider detection", "NVIDIA GPUs now correctly use CUDA/TensorRT"] },
                { ver: "1.5.1.1", date: "Feb 2026", type: "Hotfix", items: ["Fixed: SSH config not saving/loading correctly", "Added: Test SSH Connection button functional", "Added: Backend API /api/upscaler/ssh/test"] },
                { ver: "1.5.1.0", date: "Jan 2026", type: "Feature", items: ["SSH Remote Transcoding via Docker", "Multi-Architecture Docker images", "Path Mapping (local ↔ remote)", "SSH Key & Password auth", "Enhanced settings UI"] },
                { ver: "1.5.0.0", date: "Jan 2026", type: "Major", items: ["Docker Microservice Architecture", "Plugin size: 417 MB → 1.6 MB", "OpenCV DNN Models (FSRCNN, ESPCN, etc.)", "Web UI for model management", "Fixed version format for Jellyfin"] },
                { ver: "1.4.1", date: "Dec 2025", type: "Stable", items: ["Improved hardware detection", "UI refinements", "Bug fixes"] },
                { ver: "1.4.0", date: "Nov 2025", type: "Major", items: ["Redesigned UI for Jellyfin 10.10+", "Real hardware detection", "Side-by-side comparison preview", "14 AI model support"] }
            ]
        },
        sshSetup: {
            tag: "SSH Guide",
            title1: "Set up SSH", title2: "Remote Transcoding.",
            intro: "SSH Remote Transcoding lets your Jellyfin server offload video transcoding to a powerful GPU machine via SSH. This guide walks you through the complete setup process.",
            prereqTitle: "Prerequisites",
            prereqText: "You need Docker installed, the AI Upscaler container running with port 22 mapped, and SSH tools available on your Jellyfin host.",
            step1: { title: "Start Container with SSH Port", desc: "Make sure port 22 inside the container is mapped to a host port (e.g. 2222). This enables SSH access to the container.", cmdLabel: "Docker Run", tip: "💡 Important:", tipText: "The -p 2222:22 flag maps container SSH (port 22) to host port 2222. Adjust if 2222 is already in use." },
            step2: { title: "Generate SSH Key Pair", desc: "Create an ed25519 SSH key pair on your Jellyfin server. This key will authenticate the connection without a password.", cmdLabel: "Generate Key", tip: "💡 Tip:", tipText: "Press Enter when prompted for a passphrase to create a key without one (recommended for automated transcoding)." },
            step3: { title: "Copy Public Key to Container", desc: "Copy your public key (.pub) into the container's authorized_keys file so SSH accepts the connection.", cmdLabel: "Copy Key", fixPerms: "Then fix the file permissions (required by SSH):", fixPermsLabel: "Fix Permissions" },
            step4: { title: "Test SSH Connection", desc: "Verify the SSH connection works before configuring the plugin.", tip: "💡 First connection:", tipText: "Type 'yes' when asked about the host fingerprint. You should see a root shell inside the container." },
            step5: {
                title: "Configure Plugin Settings", desc: "Open Jellyfin → Dashboard → Plugins → AI Upscaler → Settings and enter the SSH details.", settingsTitle: "Plugin SSH Settings", settings: [
                    { label: "Enable Remote Transcoding", value: "✅ Enabled" },
                    { label: "Remote Host", value: "YOUR_SERVER_IP" },
                    { label: "SSH Port", value: "2222" },
                    { label: "SSH User", value: "root" },
                    { label: "SSH Key Path", value: "~/.ssh/jellyfin_upscaler" }
                ]
            },
            step6: {
                title: "Configure Path Mapping", desc: "If your media files are in different paths on the Jellyfin server vs. the Docker container, configure path mapping.", mappingTitle: "Path Mapping Example", mappings: [
                    { label: "Local Media Path (Jellyfin)", value: "/mnt/media/movies" },
                    { label: "Remote Media Path (Docker)", value: "/media/movies" }
                ], tip: "💡 Docker volumes:", tipText: "Make sure your Docker container has the media mounted with -v /mnt/media:/media so both paths point to the same files."
            },
            troubleshoot: {
                title: "SSH Troubleshooting", items: [
                    { q: "Permission denied (publickey)", a: "Your authorized_keys file permissions may be wrong. SSH requires 600 permissions on authorized_keys and 700 on the .ssh directory.", cmd: "docker exec jellyfin-ai-upscaler chmod 700 /root/.ssh && docker exec jellyfin-ai-upscaler chmod 600 /root/.ssh/authorized_keys", cmdLabel: "Fix permissions" },
                    { q: "Connection refused on port 2222", a: "The SSHD service may not be running inside the container, or port mapping is incorrect. Check if SSHD is running.", cmd: "docker exec jellyfin-ai-upscaler ps aux | grep sshd", cmdLabel: "Check SSHD" },
                    { q: "Host key verification failed", a: "If you recreated the container, the host key changed. Remove the old key entry.", cmd: "ssh-keygen -R \"[localhost]:2222\"", cmdLabel: "Remove old host key" },
                    { q: "Transcoding starts but files not found", a: "Your path mapping is incorrect. The remote path must match the mount point inside the Docker container. Check your -v volume mount matches the Remote Media Path setting." }
                ]
            },
            done: "SSH Setup Complete!",
            doneText: "Your Jellyfin server will now offload transcoding to the Docker GPU container via SSH. Test by playing a video and checking the AI upscaler button."
        },
        footer: { copyright: "© 2026 Kuschel-code. MIT License." }
    },
    de: {
        nav: { home: "Startseite", installation: "Installation", sshSetup: "SSH Einrichtung", configuration: "Konfiguration", features: "Funktionen", troubleshooting: "Fehlerbehebung", dockerTags: "Docker Tags", changelog: "Änderungen" },
        hero: {
            badge: "v1.5.2.3 — Sicherheit & Bugfixes",
            title1: "Transformiere deine Medien",
            title2: "mit KI.",
            subtitle: "Skaliere SD auf 4K mit neuronalen Netzwerken. GPU-beschleunigter Docker-Microservice für Jellyfin mit Unterstützung für NVIDIA, AMD, Intel & Apple Silicon.",
            getStarted: "Jetzt starten",
            viewGithub: "Auf GitHub ansehen",
            stats: { gpus: "GPU-Architekturen", size: "Plugin-Größe", upscale: "Hochskalierung", license: "Open Source" }
        },
        features: {
            tag: "Funktionen",
            title1: "Alles was du brauchst.", title2: "Nichts was du nicht brauchst.",
            docker: { title: "Docker-Microservice", desc: "KI-Verarbeitung läuft in einem isolierten Container — keine DLL-Konflikte, keine Abstürze. Nur 1,6 MB Plugin statt 417 MB." },
            ssh: { title: "SSH Remote Transcoding", desc: "Lagere FFmpeg an GPU-Container via SSH aus. Dein NAS delegiert die Transcodierung an leistungsstarke Hardware." },
            gpu: { title: "5 GPU-Architekturen", desc: "Native Unterstützung für NVIDIA CUDA, AMD ROCm, Intel OpenVINO, Apple Silicon ARM64 und CPU." },
            ai: { title: "Neuronale Netzwerk-Modelle", desc: "FSRCNN, ESPCN, LapSRN, EDSR, Real-ESRGAN — von blitzschnell bis maximale Details." },
            ui: { title: "Nahtlose Integration", desc: "Player-Taste, Vergleichsvorschau, Echtzeit-Benchmark und Web-UI zur Modellverwaltung." }
        },
        installation: {
            tag: "Erste Schritte",
            title1: "In Minuten", title2: "einsatzbereit.",
            warning: "Wichtiger Hinweis",
            warningText: "Dieses Plugin benötigt einen Docker-Container neben Jellyfin. Das Plugin selbst ist nur ~1,6 MB — die gesamte KI-Arbeit passiert in Docker.",
            step1: "Docker-Container starten",
            step1desc: "Image passend zu deiner GPU herunterladen und starten.",
            recommended: "Empfohlen",
            optionA: "Docker Hub (Pull)",
            optionB: "Lokal bauen",
            withGpu: "Mit NVIDIA GPU",
            withIntel: "Mit Intel GPU (Arc / Iris)",
            withAmd: "Mit AMD GPU (ROCm)",
            step2: "Plugin installieren",
            step2desc: "Plugin-Repository zu Jellyfin hinzufügen.",
            addRepo: "Repository-URL hinzufügen",
            addRepoPath: "Dashboard → Plugins → Repositories → Hinzufügen",
            installPlugin: "Aus Katalog installieren",
            installPluginPath: "Katalog → Allgemein → AI Upscaler → Installieren",
            restartJellyfin: "Jellyfin neustarten",
            restartText: "Nach der Installation Server neustarten, um das Plugin zu aktivieren.",
            configureUrl: "KI-Service URL konfigurieren",
            configureUrlText: "Docker-Container URL setzen:",
            done: "Fertig!",
            doneText: "Das Plugin ist installiert und bereit. Starte Inhalte und nutze den KI-Button im Player.",
            tip: "💡 Tipp:",
            tipText: "Ersetze YOUR_SERVER_IP mit deiner Docker-Host-IP:"
        },
        configuration: {
            tag: "Einstellungen",
            title1: "Volle Kontrolle", title2: "auf einen Blick.",
            basic: "Grundeinstellungen", hardware: "Hardware", remote: "Remote Transcoding (SSH)", ui: "Oberfläche", advanced: "Erweitert",
            fields: {
                enable: "Plugin aktivieren", serviceUrl: "KI-Service URL", model: "KI-Modell", scale: "Skalierungsfaktor", quality: "Qualitätsstufe",
                hwAccel: "Hardwarebeschleunigung", maxVram: "Max VRAM (MB)", cpuThreads: "CPU-Threads",
                enableRemote: "Remote Transcoding", remoteHost: "Remote Host", sshPort: "SSH Port", sshUser: "SSH Benutzer", sshKey: "SSH Key Datei", localPath: "Lokaler Medienpfad", remotePath: "Remote Medienpfad",
                showButton: "Player-Button anzeigen", buttonPos: "Button-Position", notifications: "Benachrichtigungen",
                comparison: "Vergleichsansicht", metrics: "Leistungsmetriken", cache: "Vorab-Cache", cacheSize: "Cache-Größe (MB)"
            }
        },
        troubleshooting: {
            tag: "Hilfe",
            title1: "Häufige Probleme.", title2: "Schnelle Lösungen.",
            problems: [
                { title: "Plugin zeigt 'Nicht unterstützt'", desc: "Das Plugin kann in Jellyfin nicht geladen werden.", solutions: ["Alte Versionen (v1.4.x) deinstallieren", "Alten Plugin-Ordner löschen", "Jellyfin neustarten", "Neu aus Repository installieren"] },
                { title: "Container startet nicht", desc: "Docker-Container stoppt sofort oder startet ständig neu.", solutions: ["Logs prüfen: docker logs jellyfin-ai-upscaler", "GPU-Treiber überprüfen", "Port-Konflikte prüfen (5000, 2222)", "Docker-Image-Tag überprüfen"], commands: [{ label: "Logs prüfen", code: "docker logs jellyfin-ai-upscaler --tail 50" }, { label: "Health Check", code: "curl http://localhost:5000/health" }] },
                { title: "Upscaling funktioniert nicht", desc: "KI-Button erscheint, aber Upscaling schlägt fehl.", solutions: ["Docker-Container läuft?", "Verbindung in Einstellungen testen", "KI-Service URL prüfen", "Medienpfade überprüfen"], commands: [{ label: "Verbindung testen", code: "curl http://DEIN_SERVER:5000/health" }] },
                { title: "Upscale-Button nicht sichtbar", desc: "Button fehlt im Jellyfin-Player.", solutions: ["Der Button funktioniert nur in Webbrowsern (Chrome, Edge, Firefox)", "Er funktioniert NICHT in nativen Apps (Windows-App, Mobil, TV)", "Öffne Jellyfin über http://DEINE_IP:8096 im Browser", "Dashboard → Plugins → AI Upscaler → Player-Button aktivieren", "Browser hart neu laden: Strg+Umschalt+R"] },
                { title: "BadImageFormatException", desc: "Assembly-Ladefehler mit nativen DLLs.", solutions: ["Das ist das alte v1.4.x Problem", "Auf v1.5.0+ upgraden (Docker)", "Alle alten DLLs aus Plugin-Ordner entfernen"] },
                { title: "GPU nicht erkannt", desc: "Container läuft im CPU-Modus trotz GPU.", solutions: ["NVIDIA: nvidia-container-toolkit installieren, --gpus all nutzen", "Intel: :1.5.4-intel Tag mit --device=/dev/dri nutzen", "AMD: :1.5.4-amd Tag mit --device=/dev/kfd --device=/dev/dri", "Windows: :1.5.4-cpu nutzen (GPU-Passthrough nicht in Docker Desktop)"], commands: [{ label: "NVIDIA testen", code: "docker run --rm --gpus all nvidia/cuda:12.2.2-base-ubuntu22.04 nvidia-smi" }, { label: "Intel testen", code: "docker exec jellyfin-ai-upscaler ls -la /dev/dri/" }] },
                { title: "Windows + Intel/AMD GPU", desc: "GPU-Images funktionieren nicht mit Docker Desktop.", solutions: ["--device=/dev/dri ist nur unter Linux verfügbar", "CPU-Image nutzen: :1.5.4-cpu", "Für GPU-Beschleunigung Docker auf Linux-Host verwenden", "CPU-Modus liefert trotzdem exzellente Ergebnisse"], commands: [{ label: "Windows CPU Befehl", code: "docker run -d --name jellyfin-ai-upscaler -p 5000:5000 -p 2222:22 kuscheltier/jellyfin-ai-upscaler:1.5.4-cpu" }] },
                { title: "Proxmox LXC GPU-Passthrough", desc: "Intel/AMD GPU wird in Proxmox LXC nicht erkannt.", solutions: ["LXC-Config: lxc.cgroup2.devices.allow: c 226:* rwm", "LXC-Config: lxc.mount.entry: /dev/dri dev/dri none bind,optional,create=dir", "Docker: --device=/dev/dri --group-add=render", "Prüfen: ls -la /dev/dri/ in der LXC"] },
                { title: "SSH-Verbindung fehlgeschlagen", desc: "Keine Verbindung zum Docker über SSH.", solutions: ["SSHD im Container prüfen", "authorized_keys Berechtigungen prüfen", "Port 2222 gemappt?", "Alten Host-Key entfernen: ssh-keygen -R [localhost]:2222"], commands: [{ label: "SSHD prüfen", code: "docker exec jellyfin-ai-upscaler ps aux | grep sshd" }] }
            ],
            solution: "Lösung",
            commands: "Nützliche Befehle",
            needHelp: "Noch Hilfe nötig?",
            githubIssues: "GitHub Issues",
            githubWiki: "GitHub Wiki"
        },
        dockerTags: {
            tag: "Docker",
            title1: "Wähle dein", title2: "Image.",
            cards: [
                { brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.4", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" },
                { brand: "AMD", tech: "ROCm", tag: ":1.5.4-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" },
                { brand: "Intel", tech: "OpenVINO", tag: ":1.5.4-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" },
                { brand: "Apple", tech: "ARM64 Optimiert", tag: ":1.5.4-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" },
                { brand: "CPU", tech: "Multi-Thread", tag: ":1.5.4-cpu", models: "Beliebig x86 / ARM64", rating: 2, color: "#6366f1" }
            ]
        },
        changelog: {
            tag: "Verlauf",
            title1: "Was gibt's", title2: "Neues.",
            versions: [
                { ver: "1.5.2.3", date: "Mär 2026", type: "Fix", items: ["Behoben: Player Upscale-Button wurde nicht angezeigt (Issue #45)", "Globale Script-Injection in index.html (wie Intro Skipper)", "player-integration.js für Jellyfin 10.11+ SPA neu geschrieben", "Behoben: Intel OpenVINO GPU fiel auf CPU zurück", "Docker 1.5.4: Intel Compute-Runtime aktualisiert + GPU_FP32 Targeting"] },
                { ver: "1.5.2.1", date: "Mär 2026", type: "Sicherheit", items: ["Sicherheit: SSH Command Injection verhindert (Regex + ArgumentList)", "Sicherheit: Path Traversal Schutz für Medienpfade", "Sicherheit: SSH-Härtung — nur Key-Auth, bedingter sshd-Start", "Behoben: Fortschrittsanzeige (hing bei 0%/50%)", "Behoben: Pause/Resume Job-ID Auflösung", "Synchronisiert: Modellliste (14 Modelle) zwischen Plugin & Backend", "Docker 1.5.3: Scale-Validierung, async I/O, fixiertes AMD Base-Image"] },
                { ver: "1.5.2.0", date: "Feb 2026", type: "Fix", items: ["Behoben: GPU-Beschleunigung funktionierte nicht (Issue #44)", "Docker-Basis auf cuDNN 9 (CUDA 12.6) aktualisiert", "Intelligente ONNX Provider-Erkennung", "NVIDIA GPUs nutzen jetzt korrekt CUDA/TensorRT"] },
                { ver: "1.5.1.1", date: "Feb 2026", type: "Hotfix", items: ["Behoben: SSH-Konfiguration wurde nicht gespeichert", "Hinzugefügt: SSH-Verbindungstest Button", "Hinzugefügt: API /api/upscaler/ssh/test"] },
                { ver: "1.5.1.0", date: "Jan 2026", type: "Feature", items: ["SSH Remote Transcoding via Docker", "Multi-Architektur Docker Images", "Pfad-Mapping (lokal ↔ remote)", "SSH Key & Passwort Auth", "Erweiterte Einstellungs-UI"] },
                { ver: "1.5.0.0", date: "Jan 2026", type: "Major", items: ["Docker Microservice Architektur", "Plugin-Größe: 417 MB → 1,6 MB", "OpenCV DNN Modelle", "Web UI für Modellverwaltung", "Versionsformat für Jellyfin korrigiert"] },
                { ver: "1.4.1", date: "Dez 2025", type: "Stabil", items: ["Verbesserte Hardwareerkennung", "UI-Verbesserungen", "Fehlerbehebungen"] },
                { ver: "1.4.0", date: "Nov 2025", type: "Major", items: ["Redesigntes UI für Jellyfin 10.10+", "Echte Hardwareerkennung", "Vergleichsvorschau", "14 KI-Modelle"] }
            ]
        },
        sshSetup: {
            tag: "SSH Anleitung",
            title1: "SSH Remote", title2: "Transcoding einrichten.",
            intro: "SSH Remote Transcoding ermöglicht es deinem Jellyfin-Server, Video-Transcoding an einen leistungsstarken GPU-Rechner via SSH auszulagern. Diese Anleitung führt dich durch den gesamten Setup-Prozess.",
            prereqTitle: "Voraussetzungen",
            prereqText: "Docker muss installiert sein, der AI Upscaler Container muss mit Port 22 gemappt laufen, und SSH-Tools müssen auf deinem Jellyfin-Host verfügbar sein.",
            step1: { title: "Container mit SSH-Port starten", desc: "Stelle sicher, dass Port 22 im Container auf einen Host-Port (z.B. 2222) gemappt ist.", cmdLabel: "Docker Run", tip: "💡 Wichtig:", tipText: "Das -p 2222:22 Flag mappt SSH (Port 22) auf Host-Port 2222. Passe an, falls 2222 belegt ist." },
            step2: { title: "SSH-Schlüsselpaar generieren", desc: "Erstelle ein ed25519 SSH-Schlüsselpaar auf deinem Jellyfin-Server. Dieser Schlüssel authentifiziert die Verbindung ohne Passwort.", cmdLabel: "Schlüssel generieren", tip: "💡 Tipp:", tipText: "Drücke Enter bei der Passphrase-Abfrage für einen Schlüssel ohne Passwort (empfohlen für automatisches Transcoding)." },
            step3: { title: "Public Key in Container kopieren", desc: "Kopiere deinen öffentlichen Schlüssel (.pub) in die authorized_keys Datei des Containers.", cmdLabel: "Schlüssel kopieren", fixPerms: "Dann Berechtigungen korrigieren (SSH erfordert dies):", fixPermsLabel: "Berechtigungen setzen" },
            step4: { title: "SSH-Verbindung testen", desc: "Überprüfe die SSH-Verbindung bevor du das Plugin konfigurierst.", tip: "💡 Erste Verbindung:", tipText: "Tippe 'yes' wenn nach dem Host-Fingerprint gefragt wird. Du solltest eine Root-Shell im Container sehen." },
            step5: {
                title: "Plugin-Einstellungen konfigurieren", desc: "Öffne Jellyfin → Dashboard → Plugins → AI Upscaler → Einstellungen und gib die SSH-Details ein.", settingsTitle: "Plugin SSH-Einstellungen", settings: [
                    { label: "Remote Transcoding aktivieren", value: "✅ Aktiviert" },
                    { label: "Remote Host", value: "DEINE_SERVER_IP" },
                    { label: "SSH Port", value: "2222" },
                    { label: "SSH Benutzer", value: "root" },
                    { label: "SSH Key Pfad", value: "~/.ssh/jellyfin_upscaler" }
                ]
            },
            step6: {
                title: "Pfad-Mapping konfigurieren", desc: "Falls deine Mediendateien unterschiedliche Pfade auf dem Jellyfin-Server und im Docker-Container haben, konfiguriere das Pfad-Mapping.", mappingTitle: "Pfad-Mapping Beispiel", mappings: [
                    { label: "Lokaler Medienpfad (Jellyfin)", value: "/mnt/media/movies" },
                    { label: "Remote Medienpfad (Docker)", value: "/media/movies" }
                ], tip: "💡 Docker Volumes:", tipText: "Stelle sicher, dass dein Docker-Container die Medien mit -v /mnt/media:/media gemountet hat."
            },
            troubleshoot: {
                title: "SSH Fehlerbehebung", items: [
                    { q: "Permission denied (publickey)", a: "Die Berechtigungen der authorized_keys Datei könnten falsch sein. SSH erfordert 600 für authorized_keys und 700 für das .ssh Verzeichnis.", cmd: "docker exec jellyfin-ai-upscaler chmod 700 /root/.ssh && docker exec jellyfin-ai-upscaler chmod 600 /root/.ssh/authorized_keys", cmdLabel: "Berechtigungen reparieren" },
                    { q: "Connection refused auf Port 2222", a: "Der SSHD-Dienst läuft möglicherweise nicht im Container oder das Port-Mapping ist falsch.", cmd: "docker exec jellyfin-ai-upscaler ps aux | grep sshd", cmdLabel: "SSHD prüfen" },
                    { q: "Host key verification failed", a: "Wenn du den Container neu erstellt hast, hat sich der Host-Key geändert. Entferne den alten Eintrag.", cmd: "ssh-keygen -R \"[localhost]:2222\"", cmdLabel: "Alten Host-Key entfernen" },
                    { q: "Transcoding startet aber Dateien nicht gefunden", a: "Dein Pfad-Mapping ist falsch. Der Remote-Pfad muss dem Mount-Punkt im Docker-Container entsprechen." }
                ]
            },
            done: "SSH Einrichtung abgeschlossen!",
            doneText: "Dein Jellyfin-Server wird jetzt Transcoding an den Docker GPU-Container via SSH auslagern. Teste es, indem du ein Video abspielst."
        },
        footer: { copyright: "© 2026 Kuschel-code. MIT-Lizenz." }
    },
    fr: {
        nav: { home: "Accueil", installation: "Installation", sshSetup: "Config SSH", configuration: "Configuration", features: "Fonctionnalités", troubleshooting: "Dépannage", dockerTags: "Docker Tags", changelog: "Historique" },
        hero: {
            badge: "v1.5.2.3 — Sécurité & Corrections",
            title1: "Transformez vos médias",
            title2: "avec l'IA.",
            subtitle: "Améliorez SD en 4K avec des réseaux neuronaux. Microservice Docker accéléré GPU pour Jellyfin avec NVIDIA, AMD, Intel et Apple Silicon.",
            getStarted: "Commencer",
            viewGithub: "Voir sur GitHub",
            stats: { gpus: "Architectures GPU", size: "Taille du plugin", upscale: "Mise à l'échelle", license: "Open Source" }
        },
        features: {
            tag: "Fonctionnalités",
            title1: "Tout ce qu'il faut.", title2: "Rien de plus.",
            docker: { title: "Microservice Docker", desc: "Le traitement IA dans un conteneur isolé — pas de conflits DLL. Seulement 1,6 Mo." },
            ssh: { title: "SSH Remote Transcoding", desc: "Déportez FFmpeg vers des conteneurs GPU via SSH." },
            gpu: { title: "5 architectures GPU", desc: "NVIDIA CUDA, AMD ROCm, Intel OpenVINO, Apple Silicon ARM64 et CPU." },
            ai: { title: "Modèles de réseaux neuronaux", desc: "FSRCNN, ESPCN, LapSRN, EDSR, Real-ESRGAN." },
            ui: { title: "Intégration transparente", desc: "Bouton lecteur, aperçu comparatif, benchmark en temps réel et interface Web." }
        },
        installation: {
            tag: "Démarrage",
            title1: "Opérationnel", title2: "en minutes.",
            warning: "Avis important",
            warningText: "Ce plugin nécessite un conteneur Docker à côté de Jellyfin.",
            step1: "Démarrer le conteneur Docker",
            step1desc: "Téléchargez et lancez l'image correspondant à votre GPU.",
            recommended: "Recommandé", optionA: "Docker Hub (Pull)", optionB: "Build local", withGpu: "Avec GPU NVIDIA", withIntel: "Avec GPU Intel (Arc / Iris)", withAmd: "Avec GPU AMD (ROCm)",
            step2: "Installer le plugin",
            step2desc: "Ajoutez le dépôt du plugin à Jellyfin.",
            addRepo: "Ajouter l'URL du dépôt", addRepoPath: "Dashboard → Plugins → Dépôts → Ajouter",
            installPlugin: "Installer depuis le catalogue", installPluginPath: "Catalogue → Général → AI Upscaler → Installer",
            restartJellyfin: "Redémarrer Jellyfin", restartText: "Redémarrez après l'installation.",
            configureUrl: "Configurer l'URL du service IA", configureUrlText: "URL du conteneur Docker :",
            done: "C'est prêt !", doneText: "Le plugin est installé et prêt à l'emploi.",
            tip: "💡 Astuce :", tipText: "Remplacez YOUR_SERVER_IP par l'IP de votre hôte Docker :"
        },
        configuration: {
            tag: "Paramètres", title1: "Contrôle total", title2: "à portée de main.",
            basic: "Paramètres de base", hardware: "Matériel", remote: "Transcoding distant (SSH)", ui: "Interface", advanced: "Avancé",
            fields: { enable: "Activer le plugin", serviceUrl: "URL du service IA", model: "Modèle IA", scale: "Facteur d'échelle", quality: "Niveau de qualité", hwAccel: "Accélération matérielle", maxVram: "VRAM max (Mo)", cpuThreads: "Threads CPU", enableRemote: "Transcoding distant", remoteHost: "Hôte distant", sshPort: "Port SSH", sshUser: "Utilisateur SSH", sshKey: "Fichier clé SSH", localPath: "Chemin média local", remotePath: "Chemin média distant", showButton: "Bouton lecteur", buttonPos: "Position du bouton", notifications: "Notifications", comparison: "Vue comparaison", metrics: "Métriques", cache: "Cache pré-traitement", cacheSize: "Taille cache (Mo)" }
        },
        troubleshooting: {
            tag: "Aide", title1: "Problèmes courants.", title2: "Solutions rapides.",
            problems: [
                { title: "Plugin 'Non supporté'", desc: "Le plugin ne charge pas.", solutions: ["Désinstaller les anciennes versions", "Supprimer l'ancien dossier", "Redémarrer Jellyfin", "Réinstaller"] },
                { title: "Conteneur ne démarre pas", desc: "Le conteneur s'arrête immédiatement.", solutions: ["Vérifier les logs", "Vérifier les pilotes GPU", "Vérifier les ports"], commands: [{ label: "Logs", code: "docker logs jellyfin-ai-upscaler --tail 50" }] }
            ],
            solution: "Solution", commands: "Commandes utiles", needHelp: "Encore besoin d'aide ?", githubIssues: "GitHub Issues", githubWiki: "GitHub Wiki"
        },
        dockerTags: {
            tag: "Docker", title1: "Choisissez votre", title2: "image.",
            cards: [
                { brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.4", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" },
                { brand: "AMD", tech: "ROCm", tag: ":1.5.4-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" },
                { brand: "Intel", tech: "OpenVINO", tag: ":1.5.4-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" },
                { brand: "Apple", tech: "ARM64 Optimisé", tag: ":1.5.4-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" },
                { brand: "CPU", tech: "Multi-Thread", tag: ":1.5.4-cpu", models: "x86 / ARM64", rating: 2, color: "#6366f1" }
            ]
        },
        changelog: {
            tag: "Historique", title1: "Quoi de", title2: "neuf.",
            versions: [
                { ver: "1.5.2.3", date: "Mar 2026", type: "Correction", items: ["Corrigé : Bouton upscale non affiché (Issue #45)", "Injection script globale dans index.html (comme Intro Skipper)", "player-integration.js réécrit pour Jellyfin 10.11+ SPA", "Corrigé : Intel OpenVINO GPU retombait sur CPU", "Docker 1.5.4 : Runtime Intel mis à jour + ciblage GPU_FP32"] },
                { ver: "1.5.2.1", date: "Mar 2026", type: "Sécurité", items: ["Sécurité : Prévention injection SSH (regex + ArgumentList)", "Sécurité : Protection contre path traversal", "Sécurité : Durcissement SSH — auth par clé uniquement", "Corrigé : Suivi de progression (bloqué à 0%/50%)", "Corrigé : Résolution ID job pause/reprise", "Synchronisé : Liste des modèles (14) entre plugin et backend", "Docker 1.5.3 : Validation scale, I/O async, image AMD fixée"] },
                { ver: "1.5.2.0", date: "Fév 2026", type: "Correction", items: ["Corrigé : Accélération GPU ne fonctionnait pas (Issue #44)", "Base Docker mise à jour vers cuDNN 9", "Détection intelligente des providers ONNX", "NVIDIA utilise maintenant CUDA/TensorRT correctement"] },
                { ver: "1.5.1.1", date: "Fév 2026", type: "Correctif", items: ["Corrigé : Config SSH non sauvegardée", "Ajouté : Bouton test SSH", "Ajouté : API /api/upscaler/ssh/test"] },
                { ver: "1.5.1.0", date: "Jan 2026", type: "Feature", items: ["SSH Remote Transcoding", "Images Docker multi-arch", "Mapping de chemins", "Auth SSH clé & mot de passe"] },
                { ver: "1.5.0.0", date: "Jan 2026", type: "Majeur", items: ["Architecture Microservice Docker", "Taille : 417 Mo → 1,6 Mo", "Modèles OpenCV DNN", "Interface Web"] },
                { ver: "1.4.0", date: "Nov 2025", type: "Majeur", items: ["Interface redessinée", "Détection matérielle", "Aperçu comparatif"] }
            ]
        },
        sshSetup: {
            tag: "Guide SSH", title1: "Configurer SSH", title2: "Remote Transcoding.",
            intro: "Le transcodage distant SSH permet à votre serveur Jellyfin de déléguer le transcodage vidéo à une machine GPU via SSH.",
            prereqTitle: "Prérequis", prereqText: "Docker installé, conteneur AI Upscaler avec port 22 mappé, outils SSH disponibles.",
            step1: { title: "Démarrer le conteneur avec SSH", desc: "Mappez le port 22 du conteneur vers un port hôte (ex: 2222).", cmdLabel: "Docker Run", tip: "💡 Important :", tipText: "Le flag -p 2222:22 mappe le SSH du conteneur sur le port 2222." },
            step2: { title: "Générer une paire de clés SSH", desc: "Créez une clé ed25519 sur votre serveur Jellyfin.", cmdLabel: "Générer la clé", tip: "💡 Astuce :", tipText: "Appuyez sur Entrée pour créer une clé sans phrase de passe." },
            step3: { title: "Copier la clé dans le conteneur", desc: "Copiez votre clé publique dans le fichier authorized_keys du conteneur.", cmdLabel: "Copier la clé", fixPerms: "Puis corrigez les permissions :", fixPermsLabel: "Permissions" },
            step4: { title: "Tester la connexion SSH", desc: "Vérifiez que la connexion SSH fonctionne.", tip: "💡 Première connexion :", tipText: "Tapez 'yes' pour accepter l'empreinte de l'hôte." },
            step5: { title: "Configurer le plugin", desc: "Ouvrez Jellyfin → Dashboard → Plugins → AI Upscaler → Paramètres.", settingsTitle: "Paramètres SSH", settings: [{ label: "Transcodage distant", value: "✅ Activé" }, { label: "Hôte distant", value: "VOTRE_IP" }, { label: "Port SSH", value: "2222" }, { label: "Utilisateur SSH", value: "root" }, { label: "Chemin clé SSH", value: "~/.ssh/jellyfin_upscaler" }] },
            step6: { title: "Mapping des chemins", desc: "Configurez le mapping si les chemins diffèrent entre Jellyfin et Docker.", mappingTitle: "Exemple de mapping", mappings: [{ label: "Chemin local (Jellyfin)", value: "/mnt/media/movies" }, { label: "Chemin distant (Docker)", value: "/media/movies" }], tip: "💡 Volumes Docker :", tipText: "Assurez-vous que le conteneur monte les médias avec -v /mnt/media:/media." },
            troubleshoot: { title: "Dépannage SSH", items: [{ q: "Permission denied (publickey)", a: "Vérifiez les permissions : 600 pour authorized_keys, 700 pour .ssh.", cmd: "docker exec jellyfin-ai-upscaler chmod 700 /root/.ssh && docker exec jellyfin-ai-upscaler chmod 600 /root/.ssh/authorized_keys", cmdLabel: "Corriger" }, { q: "Connection refused", a: "SSHD pourrait ne pas fonctionner.", cmd: "docker exec jellyfin-ai-upscaler ps aux | grep sshd", cmdLabel: "Vérifier SSHD" }] },
            done: "Configuration SSH terminée !", doneText: "Votre serveur Jellyfin déléguera le transcodage au conteneur GPU via SSH."
        },
        footer: { copyright: "© 2026 Kuschel-code. Licence MIT." }
    },
    zh: {
        nav: { home: "首页", installation: "安装", sshSetup: "SSH设置", configuration: "配置", features: "功能", troubleshooting: "故障排除", dockerTags: "Docker 标签", changelog: "更新日志" },
        hero: { badge: "v1.5.2.3 — 安全与修复", title1: "用人工智能", title2: "转换您的媒体。", subtitle: "使用神经网络将SD升级到4K。支持NVIDIA、AMD、Intel和Apple Silicon的GPU加速Docker微服务。", getStarted: "开始使用", viewGithub: "在GitHub上查看", stats: { gpus: "GPU架构", size: "插件大小", upscale: "升级", license: "开源" } },
        features: { tag: "功能", title1: "你需要的一切。", title2: "没有多余的。", docker: { title: "Docker微服务", desc: "AI处理在隔离容器中运行——无DLL冲突。仅1.6 MB。" }, ssh: { title: "SSH远程转码", desc: "通过SSH将FFmpeg卸载到GPU容器。" }, gpu: { title: "5种GPU架构", desc: "NVIDIA CUDA、AMD ROCm、Intel OpenVINO、Apple Silicon、CPU。" }, ai: { title: "神经网络模型", desc: "FSRCNN、ESPCN、LapSRN、EDSR、Real-ESRGAN。" }, ui: { title: "无缝集成", desc: "播放器按钮、对比预览、实时基准测试和Web UI。" } },
        installation: { tag: "入门", title1: "几分钟", title2: "即可启动。", warning: "重要提示", warningText: "此插件需要Docker容器。插件仅~1.6 MB，所有AI计算在Docker中完成。", step1: "启动Docker容器", step1desc: "拉取并运行匹配GPU的镜像。", recommended: "推荐", optionA: "Docker Hub", optionB: "本地构建", withGpu: "NVIDIA GPU", step2: "安装插件", step2desc: "将插件仓库添加到Jellyfin。", addRepo: "添加仓库URL", addRepoPath: "仪表板 → 插件 → 仓库 → 添加", installPlugin: "从目录安装", installPluginPath: "目录 → 常规 → AI Upscaler → 安装", restartJellyfin: "重启Jellyfin", restartText: "安装后重启服务器。", configureUrl: "配置AI服务URL", configureUrlText: "设置Docker容器URL：", done: "完成！", doneText: "插件已安装就绪。", tip: "💡 提示：", tipText: "将YOUR_SERVER_IP替换为Docker主机IP：" },
        configuration: { tag: "设置", title1: "完全控制", title2: "触手可及。", basic: "基本设置", hardware: "硬件", remote: "远程转码(SSH)", ui: "界面", advanced: "高级", fields: { enable: "启用插件", serviceUrl: "AI服务URL", model: "AI模型", scale: "缩放倍数", quality: "质量级别", hwAccel: "硬件加速", maxVram: "最大显存(MB)", cpuThreads: "CPU线程", enableRemote: "远程转码", remoteHost: "远程主机", sshPort: "SSH端口", sshUser: "SSH用户", sshKey: "SSH密钥文件", localPath: "本地媒体路径", remotePath: "远程媒体路径", showButton: "显示播放器按钮", buttonPos: "按钮位置", notifications: "通知", comparison: "对比视图", metrics: "性能指标", cache: "预处理缓存", cacheSize: "缓存大小(MB)" } },
        troubleshooting: { tag: "帮助", title1: "常见问题。", title2: "快速修复。", problems: [{ title: "插件显示'不支持'", desc: "插件无法加载。", solutions: ["卸载旧版本", "删除旧插件文件夹", "重启Jellyfin", "重新安装"] }, { title: "容器无法启动", desc: "Docker容器立即退出。", solutions: ["检查日志", "验证GPU驱动", "检查端口冲突"], commands: [{ label: "查看日志", code: "docker logs jellyfin-ai-upscaler --tail 50" }] }], solution: "解决方案", commands: "常用命令", needHelp: "还需要帮助？", githubIssues: "GitHub Issues", githubWiki: "GitHub Wiki" },
        dockerTags: { tag: "Docker", title1: "选择你的", title2: "镜像。", cards: [{ brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.4", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" }, { brand: "AMD", tech: "ROCm", tag: ":1.5.4-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" }, { brand: "Intel", tech: "OpenVINO", tag: ":1.5.4-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" }, { brand: "Apple", tech: "ARM64优化", tag: ":1.5.4-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" }, { brand: "CPU", tech: "多线程", tag: ":1.5.4-cpu", models: "任意x86/ARM64", rating: 2, color: "#6366f1" }] },
        changelog: { tag: "日志", title1: "最新", title2: "更新。", versions: [{ ver: "1.5.2.3", date: "2026年3月", type: "修复", items: ["修复：播放器升级按钮不显示（Issue #45）", "全局脚本注入index.html（类似Intro Skipper）", "player-integration.js为Jellyfin 10.11+ SPA重写", "修复：Intel OpenVINO GPU回退到CPU", "Docker 1.5.4：更新Intel计算运行时 + GPU_FP32目标"] }, { ver: "1.5.2.1", date: "2026年3月", type: "安全", items: ["安全：防止SSH命令注入（正则+ArgumentList）", "安全：媒体路径穿越防护", "安全：SSH加固——仅密钥认证", "修复：进度跟踪（卡在0%/50%）", "修复：暂停/恢复作业ID", "同步：插件与后端模型列表（14个模型）", "Docker 1.5.3：Scale验证、异步I/O、固定AMD基础镜像"] }, { ver: "1.5.2.0", date: "2026年2月", type: "修复", items: ["修复：GPU加速不工作（Issue #44）", "Docker基础镜像升级至cuDNN 9", "智能ONNX Provider检测", "NVIDIA GPU现在正确使用CUDA/TensorRT"] }, { ver: "1.5.1.1", date: "2026年2月", type: "修复", items: ["修复：SSH配置未保存", "新增：SSH连接测试按钮", "新增：API端点"] }, { ver: "1.5.0.0", date: "2026年1月", type: "重大", items: ["Docker微服务架构", "插件大小：417MB→1.6MB", "Web UI管理界面"] }] },
        sshSetup: {
            tag: "SSH指南", title1: "设置SSH", title2: "远程转码。",
            intro: "SSH远程转码允许Jellyfin服务器通过SSH将视频转码卸载到GPU服务器。",
            prereqTitle: "前提条件", prereqText: "需要已安装Docker，AI Upscaler容器映射端口22，SSH工具可用。",
            step1: { title: "启动带SSH端口的容器", desc: "确保容器的22端口映射到主机端口（如2222）。", cmdLabel: "Docker Run", tip: "💡 重要：", tipText: "-p 2222:22 将容器SSH映射到主机端口2222。" },
            step2: { title: "生成SSH密钥对", desc: "在Jellyfin服务器上创建ed25519 SSH密钥对。", cmdLabel: "生成密钥", tip: "💡 提示：", tipText: "按回车跳过密码短语（建议用于自动转码）。" },
            step3: { title: "将公钥复制到容器", desc: "将公钥(.pub)复制到容器的authorized_keys文件中。", cmdLabel: "复制密钥", fixPerms: "然后修复文件权限：", fixPermsLabel: "修复权限" },
            step4: { title: "测试SSH连接", desc: "在配置插件前验证SSH连接。", tip: "💡 首次连接：", tipText: "输入'yes'接受主机指纹。" },
            step5: { title: "配置插件设置", desc: "打开Jellyfin → 仪表板 → 插件 → AI Upscaler → 设置。", settingsTitle: "SSH设置", settings: [{ label: "启用远程转码", value: "✅ 已启用" }, { label: "远程主机", value: "服务器IP" }, { label: "SSH端口", value: "2222" }, { label: "SSH用户", value: "root" }, { label: "SSH密钥路径", value: "~/.ssh/jellyfin_upscaler" }] },
            step6: { title: "配置路径映射", desc: "如果Jellyfin和Docker的媒体路径不同，需要配置路径映射。", mappingTitle: "路径映射示例", mappings: [{ label: "本地媒体路径", value: "/mnt/media/movies" }, { label: "远程媒体路径", value: "/media/movies" }], tip: "💡 Docker卷：", tipText: "确保容器使用 -v /mnt/media:/media 挂载媒体。" },
            troubleshoot: { title: "SSH故障排除", items: [{ q: "Permission denied (publickey)", a: "检查authorized_keys权限（需要600）和.ssh目录权限（需要700）。", cmd: "docker exec jellyfin-ai-upscaler chmod 700 /root/.ssh && docker exec jellyfin-ai-upscaler chmod 600 /root/.ssh/authorized_keys", cmdLabel: "修复权限" }, { q: "端口2222连接被拒绝", a: "SSHD可能未运行。", cmd: "docker exec jellyfin-ai-upscaler ps aux | grep sshd", cmdLabel: "检查SSHD" }] },
            done: "SSH设置完成！", doneText: "Jellyfin将通过SSH将转码卸载到Docker GPU容器。"
        },
        footer: { copyright: "© 2026 Kuschel-code。MIT许可证。" }
    },
    ru: {
        nav: { home: "Главная", installation: "Установка", sshSetup: "Настройка SSH", configuration: "Настройки", features: "Возможности", troubleshooting: "Устранение неполадок", dockerTags: "Docker Теги", changelog: "История изменений" },
        hero: { badge: "v1.5.2.3 — Безопасность и исправления", title1: "Преобразуйте медиа", title2: "с помощью ИИ.", subtitle: "Масштабируйте SD до 4K с помощью нейросетей. GPU-ускоренный Docker-микросервис для Jellyfin.", getStarted: "Начать", viewGithub: "GitHub", stats: { gpus: "Архитектур GPU", size: "Размер плагина", upscale: "Масштабирование", license: "Open Source" } },
        features: { tag: "Возможности", title1: "Всё что нужно.", title2: "Ничего лишнего.", docker: { title: "Docker Микросервис", desc: "ИИ работает в изолированном контейнере — без конфликтов DLL. Всего 1,6 МБ." }, ssh: { title: "SSH Remote Transcoding", desc: "Перенаправьте FFmpeg на GPU-контейнеры через SSH." }, gpu: { title: "5 архитектур GPU", desc: "NVIDIA CUDA, AMD ROCm, Intel OpenVINO, Apple Silicon, CPU." }, ai: { title: "Модели нейросетей", desc: "FSRCNN, ESPCN, LapSRN, EDSR, Real-ESRGAN." }, ui: { title: "Бесшовная интеграция", desc: "Кнопка плеера, предпросмотр, бенчмарк и Web UI." } },
        installation: { tag: "Начало", title1: "Запуск", title2: "за минуты.", warning: "Важно", warningText: "Плагин требует Docker-контейнер.", step1: "Запустить Docker", step1desc: "Скачайте образ для вашей GPU.", recommended: "Рекомендуется", optionA: "Docker Hub", optionB: "Сборка", withGpu: "NVIDIA GPU", step2: "Установить плагин", step2desc: "Добавьте репозиторий.", addRepo: "URL репозитория", addRepoPath: "Панель → Плагины → Репозитории → Добавить", installPlugin: "Установить из каталога", installPluginPath: "Каталог → AI Upscaler → Установить", restartJellyfin: "Перезапустить Jellyfin", restartText: "Перезапустите сервер.", configureUrl: "Настроить URL", configureUrlText: "URL контейнера Docker:", done: "Готово!", doneText: "Плагин установлен.", tip: "💡 Совет:", tipText: "Замените YOUR_SERVER_IP:" },
        configuration: { tag: "Настройки", title1: "Полный контроль", title2: "в ваших руках.", basic: "Основные", hardware: "Аппаратное обеспечение", remote: "Удалённое транскодирование", ui: "Интерфейс", advanced: "Продвинутые", fields: { enable: "Включить плагин", serviceUrl: "URL ИИ-сервиса", model: "Модель ИИ", scale: "Масштаб", quality: "Качество", hwAccel: "Аппаратное ускорение", maxVram: "Макс VRAM (МБ)", cpuThreads: "Потоки CPU", enableRemote: "Удалённый транскодинг", remoteHost: "Хост", sshPort: "SSH порт", sshUser: "SSH пользователь", sshKey: "SSH ключ", localPath: "Локальный путь", remotePath: "Удалённый путь", showButton: "Кнопка плеера", buttonPos: "Позиция", notifications: "Уведомления", comparison: "Сравнение", metrics: "Метрики", cache: "Кэш", cacheSize: "Размер кэша (МБ)" } },
        troubleshooting: { tag: "Помощь", title1: "Частые проблемы.", title2: "Быстрые решения.", problems: [{ title: "Плагин 'Не поддерживается'", desc: "Плагин не загружается.", solutions: ["Удалить старые версии", "Очистить папку плагинов", "Перезапустить Jellyfin", "Переустановить"] }], solution: "Решение", commands: "Команды", needHelp: "Нужна помощь?", githubIssues: "GitHub Issues", githubWiki: "GitHub Wiki" },
        dockerTags: { tag: "Docker", title1: "Выберите", title2: "образ.", cards: [{ brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.4", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" }, { brand: "AMD", tech: "ROCm", tag: ":1.5.4-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" }, { brand: "Intel", tech: "OpenVINO", tag: ":1.5.4-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" }, { brand: "Apple", tech: "ARM64", tag: ":1.5.4-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" }, { brand: "CPU", tech: "Multi-Thread", tag: ":1.5.4-cpu", models: "x86 / ARM64", rating: 2, color: "#6366f1" }] },
        changelog: { tag: "Журнал", title1: "Что", title2: "нового.", versions: [{ ver: "1.5.2.3", date: "Мар 2026", type: "Исправ.", items: ["Исправлено: Кнопка апскейла не отображалась (Issue #45)", "Глобальная инъекция скрипта в index.html (как Intro Skipper)", "player-integration.js переписан для Jellyfin 10.11+ SPA", "Исправлено: Intel OpenVINO GPU откатывался на CPU", "Docker 1.5.4: Обновлён Intel compute-runtime + таргетинг GPU_FP32"] }, { ver: "1.5.2.1", date: "Мар 2026", type: "Безопасность", items: ["Безопасность: Предотвращение SSH-инъекций (regex + ArgumentList)", "Безопасность: Защита от path traversal", "Безопасность: Усиление SSH — только ключи, условный запуск sshd", "Исправлено: Отслеживание прогресса (зависало на 0%/50%)", "Исправлено: ID задачи pause/resume", "Синхронизировано: Список моделей (14) между плагином и бэкендом", "Docker 1.5.3: Валидация scale, async I/O, закреплённый образ AMD"] }, { ver: "1.5.2.0", date: "Фев 2026", type: "Исправ.", items: ["GPU ускорение исправлено (Issue #44)", "Docker база обновлена до cuDNN 9", "Интеллектуальное определение ONNX провайдеров", "NVIDIA GPU теперь корректно используют CUDA/TensorRT"] }, { ver: "1.5.1.1", date: "Фев 2026", type: "Исправ.", items: ["SSH конфигурация исправлена", "Кнопка теста SSH", "API эндпоинт"] }, { ver: "1.5.0.0", date: "Янв 2026", type: "Мажорный", items: ["Docker микросервис", "1,6 МБ вместо 417 МБ", "Web UI"] }] },
        sshSetup: {
            tag: "SSH Руководство", title1: "Настройка SSH", title2: "удалённого транскодирования.",
            intro: "SSH позволяет серверу Jellyfin делегировать транскодирование на GPU-машину через SSH.",
            prereqTitle: "Требования", prereqText: "Docker установлен, контейнер AI Upscaler с маппингом порта 22, SSH-инструменты.",
            step1: { title: "Запуск контейнера с SSH", desc: "Порт 22 контейнера маппится на хост (напр. 2222).", cmdLabel: "Docker Run", tip: "💡 Важно:", tipText: "Флаг -p 2222:22 маппит SSH контейнера на порт 2222." },
            step2: { title: "Генерация SSH-ключей", desc: "Создайте ed25519 SSH-ключ на сервере Jellyfin.", cmdLabel: "Сгенерировать ключ", tip: "💡 Совет:", tipText: "Нажмите Enter, чтобы создать ключ без пароля." },
            step3: { title: "Копирование ключа в контейнер", desc: "Скопируйте публичный ключ в authorized_keys контейнера.", cmdLabel: "Копировать ключ", fixPerms: "Затем исправьте права:", fixPermsLabel: "Права доступа" },
            step4: { title: "Тест SSH-соединения", desc: "Проверьте SSH-соединение перед настройкой плагина.", tip: "💡 Первое подключение:", tipText: "Введите 'yes' для подтверждения отпечатка хоста." },
            step5: { title: "Настройка плагина", desc: "Откройте Jellyfin → Панель → Плагины → AI Upscaler → Настройки.", settingsTitle: "SSH настройки", settings: [{ label: "Удалённый транскодинг", value: "✅ Включен" }, { label: "Хост", value: "IP_СЕРВЕРА" }, { label: "SSH порт", value: "2222" }, { label: "Пользователь", value: "root" }, { label: "Путь к ключу", value: "~/.ssh/jellyfin_upscaler" }] },
            step6: { title: "Маппинг путей", desc: "Если пути медиафайлов различаются между Jellyfin и Docker.", mappingTitle: "Пример маппинга", mappings: [{ label: "Локальный путь", value: "/mnt/media/movies" }, { label: "Удалённый путь", value: "/media/movies" }], tip: "💡 Docker тома:", tipText: "Убедитесь, что контейнер монтирует медиа: -v /mnt/media:/media." },
            troubleshoot: { title: "Устранение проблем SSH", items: [{ q: "Permission denied (publickey)", a: "Проверьте права: 600 для authorized_keys, 700 для .ssh.", cmd: "docker exec jellyfin-ai-upscaler chmod 700 /root/.ssh && docker exec jellyfin-ai-upscaler chmod 600 /root/.ssh/authorized_keys", cmdLabel: "Исправить права" }, { q: "Отказ соединения на порту 2222", a: "SSHD может не работать в контейнере.", cmd: "docker exec jellyfin-ai-upscaler ps aux | grep sshd", cmdLabel: "Проверить SSHD" }] },
            done: "SSH настроен!", doneText: "Jellyfin будет делегировать транскодирование GPU-контейнеру через SSH."
        },
        footer: { copyright: "© 2026 Kuschel-code. Лицензия MIT." }
    },
    ja: {
        nav: { home: "ホーム", installation: "インストール", sshSetup: "SSH設定", configuration: "設定", features: "機能", troubleshooting: "トラブルシューティング", dockerTags: "Docker タグ", changelog: "変更履歴" },
        hero: { badge: "v1.5.2.3 — セキュリティとバグ修正", title1: "AIでメディアを", title2: "変換する。", subtitle: "ニューラルネットワークでSDを4Kにアップスケール。NVIDIA、AMD、Intel、Apple Silicon対応のGPU対応Dockerマイクロサービス。", getStarted: "始める", viewGithub: "GitHub", stats: { gpus: "GPUアーキテクチャ", size: "プラグインサイズ", upscale: "アップスケール", license: "オープンソース" } },
        features: { tag: "機能", title1: "必要なものすべて。", title2: "余計なものなし。", docker: { title: "Dockerマイクロサービス", desc: "AI処理は隔離されたコンテナで実行。わずか1.6MBのプラグイン。" }, ssh: { title: "SSHリモートトランスコーディング", desc: "SSH経由でFFmpegをGPUコンテナに委託。" }, gpu: { title: "5つのGPUアーキテクチャ", desc: "NVIDIA CUDA、AMD ROCm、Intel OpenVINO、Apple Silicon、CPU。" }, ai: { title: "ニューラルネットワークモデル", desc: "FSRCNN、ESPCN、LapSRN、EDSR、Real-ESRGAN。" }, ui: { title: "シームレスな統合", desc: "プレーヤーボタン、プレビュー比較、ベンチマーク、Web UI。" } },
        installation: { tag: "はじめに", title1: "数分で", title2: "起動。", warning: "重要", warningText: "このプラグインにはDockerコンテナが必要です。", step1: "Dockerコンテナを起動", step1desc: "GPUに合うイメージを取得して実行。", recommended: "推奨", optionA: "Docker Hub", optionB: "ローカルビルド", withGpu: "NVIDIA GPU", step2: "プラグインをインストール", step2desc: "Jellyfinにプラグインリポジトリを追加。", addRepo: "リポジトリURLを追加", addRepoPath: "ダッシュボード → プラグイン → リポジトリ → 追加", installPlugin: "カタログからインストール", installPluginPath: "カタログ → AI Upscaler → インストール", restartJellyfin: "Jellyfinを再起動", restartText: "インストール後にサーバーを再起動。", configureUrl: "AIサービスURLを設定", configureUrlText: "DockerコンテナのURL：", done: "完了！", doneText: "プラグインの準備完了。", tip: "💡 ヒント：", tipText: "YOUR_SERVER_IPをDockerホストIPに置き換え：" },
        configuration: { tag: "設定", title1: "完全な制御を", title2: "手の中に。", basic: "基本設定", hardware: "ハードウェア", remote: "リモートトランスコーディング", ui: "UI設定", advanced: "詳細", fields: { enable: "プラグイン有効", serviceUrl: "AIサービスURL", model: "AIモデル", scale: "スケール倍率", quality: "品質レベル", hwAccel: "ハードウェアアクセラレーション", maxVram: "最大VRAM(MB)", cpuThreads: "CPUスレッド", enableRemote: "リモートトランスコーディング", remoteHost: "リモートホスト", sshPort: "SSHポート", sshUser: "SSHユーザー", sshKey: "SSH鍵ファイル", localPath: "ローカルメディアパス", remotePath: "リモートメディアパス", showButton: "プレーヤーボタン", buttonPos: "ボタン位置", notifications: "通知", comparison: "比較ビュー", metrics: "パフォーマンス", cache: "プリキャッシュ", cacheSize: "キャッシュサイズ(MB)" } },
        troubleshooting: { tag: "ヘルプ", title1: "よくある問題。", title2: "素早い解決。", problems: [{ title: "プラグインが「サポートされていない」", desc: "プラグインが読み込めない。", solutions: ["古いバージョンをアンインストール", "古いフォルダを削除", "Jellyfinを再起動", "再インストール"] }], solution: "解決策", commands: "コマンド", needHelp: "まだ助けが必要？", githubIssues: "GitHub Issues", githubWiki: "GitHub Wiki" },
        dockerTags: { tag: "Docker", title1: "イメージを", title2: "選択。", cards: [{ brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.4", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" }, { brand: "AMD", tech: "ROCm", tag: ":1.5.4-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" }, { brand: "Intel", tech: "OpenVINO", tag: ":1.5.4-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" }, { brand: "Apple", tech: "ARM64最適化", tag: ":1.5.4-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" }, { brand: "CPU", tech: "マルチスレッド", tag: ":1.5.4-cpu", models: "x86 / ARM64", rating: 2, color: "#6366f1" }] },
        changelog: { tag: "履歴", title1: "新機能", title2: "のご紹介。", versions: [{ ver: "1.5.2.3", date: "2026年3月", type: "修正", items: ["修正：プレーヤーのアップスケールボタンが表示されない（Issue #45）", "index.htmlへのグローバルスクリプトインジェクション（Intro Skipperと同様）", "Jellyfin 10.11+ SPA用にplayer-integration.jsを書き直し", "修正：Intel OpenVINO GPUがCPUにフォールバック", "Docker 1.5.4：Intelコンピュートランタイム更新 + GPU_FP32ターゲティング"] }, { ver: "1.5.2.1", date: "2026年3月", type: "セキュリティ", items: ["セキュリティ：SSHコマンドインジェクション防止（正規表現+ArgumentList）", "セキュリティ：パストラバーサル防止", "セキュリティ：SSH強化——鍵認証のみ、条件付きsshd起動", "修正：進捗追跡（0%/50%で停止）", "修正：一時停止/再開ジョブID解決", "同期：プラグインとバックエンド間のモデルリスト（14モデル）", "Docker 1.5.3：スケール検証、非同期I/O、AMDベースイメージ固定"] }, { ver: "1.5.2.0", date: "2026年2月", type: "修正", items: ["GPUアクセラレーション修正 (Issue #44)", "Docker基盤をcuDNN 9に更新", "ONNX Provider自動検出", "NVIDIA GPUがCUDA/TensorRTを正しく使用"] }, { ver: "1.5.1.1", date: "2026年2月", type: "修正", items: ["SSH設定の保存修正", "SSH接続テストボタン追加", "APIエンドポイント追加"] }, { ver: "1.5.0.0", date: "2026年1月", type: "メジャー", items: ["Dockerマイクロサービスアーキテクチャ", "プラグインサイズ削減", "Web UI"] }] },
        sshSetup: {
            tag: "SSHガイド", title1: "SSHリモート", title2: "トランスコーディング設定。",
            intro: "SSHリモートトランスコーディングにより、JellyfinサーバーからGPUマシンにトランスコーディングを委託できます。",
            prereqTitle: "前提条件", prereqText: "Dockerインストール済み、ポート22マッピング済みのAI Upscalerコンテナ、SSHツールが必要です。",
            step1: { title: "SSHポート付きコンテナを起動", desc: "コンテナのポート22をホストポート（例：2222）にマッピング。", cmdLabel: "Docker Run", tip: "💡 重要：", tipText: "-p 2222:22 でコンテナSSHをホストポート2222にマッピング。" },
            step2: { title: "SSH鍵ペアを生成", desc: "JellyfinサーバーでSSH鍵を作成。", cmdLabel: "鍵を生成", tip: "💡 ヒント：", tipText: "パスフレーズなしで作成するにはEnterを押してください。" },
            step3: { title: "公開鍵をコンテナにコピー", desc: "公開鍵(.pub)をコンテナのauthorized_keysにコピー。", cmdLabel: "鍵をコピー", fixPerms: "次にパーミッションを修正：", fixPermsLabel: "パーミッション修正" },
            step4: { title: "SSH接続テスト", desc: "プラグイン設定前にSSH接続を確認。", tip: "💡 初回接続：", tipText: "'yes'と入力してホストフィンガープリントを受け入れてください。" },
            step5: { title: "プラグイン設定", desc: "Jellyfin → ダッシュボード → プラグイン → AI Upscaler → 設定を開きます。", settingsTitle: "SSH設定", settings: [{ label: "リモートトランスコーディング", value: "✅ 有効" }, { label: "リモートホスト", value: "サーバーIP" }, { label: "SSHポート", value: "2222" }, { label: "SSHユーザー", value: "root" }, { label: "SSH鍵パス", value: "~/.ssh/jellyfin_upscaler" }] },
            step6: { title: "パスマッピング設定", desc: "JellyfinとDockerのメディアパスが異なる場合に設定。", mappingTitle: "パスマッピング例", mappings: [{ label: "ローカルメディアパス", value: "/mnt/media/movies" }, { label: "リモートメディアパス", value: "/media/movies" }], tip: "💡 Dockerボリューム：", tipText: "コンテナが -v /mnt/media:/media でメディアをマウントしていることを確認。" },
            troubleshoot: { title: "SSHトラブルシューティング", items: [{ q: "Permission denied (publickey)", a: "authorized_keysの権限（600）と.sshディレクトリの権限（700）を確認。", cmd: "docker exec jellyfin-ai-upscaler chmod 700 /root/.ssh && docker exec jellyfin-ai-upscaler chmod 600 /root/.ssh/authorized_keys", cmdLabel: "権限修正" }, { q: "ポート2222接続拒否", a: "コンテナ内でSSHDが実行されていない可能性があります。", cmd: "docker exec jellyfin-ai-upscaler ps aux | grep sshd", cmdLabel: "SSHD確認" }] },
            done: "SSH設定完了！", doneText: "JellyfinはSSH経由でDockerのGPUコンテナにトランスコーディングを委託します。"
        },
        footer: { copyright: "© 2026 Kuschel-code。MITライセンス。" }
    }
};
