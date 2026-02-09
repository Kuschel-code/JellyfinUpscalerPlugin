/* ========================================
   Translations for all 6 languages
======================================== */
const i18n = {
    en: {
        nav: { home: "Home", installation: "Installation", configuration: "Configuration", features: "Features", troubleshooting: "Troubleshooting", dockerTags: "Docker Tags", changelog: "Changelog" },
        hero: {
            badge: "v1.5.1 — SSH Remote Transcoding Edition",
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
                { title: "BadImageFormatException", desc: "Assembly load error with native DLLs.", solutions: ["This is the old v1.4.x issue", "Upgrade to v1.5.0+ (Docker)", "Remove ALL old DLLs from plugin folder"] },
                { title: "GPU Not Detected", desc: "Container runs in CPU mode despite GPU available.", solutions: ["Install nvidia-container-toolkit", "Verify docker --gpus all works", "Check /dev/dri permissions (Intel/AMD)"], commands: [{ label: "Test GPU access", code: "docker run --rm --gpus all nvidia/cuda:12.2.2-base-ubuntu22.04 nvidia-smi" }] },
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
                { brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.1", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" },
                { brand: "AMD", tech: "ROCm", tag: ":1.5.1-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" },
                { brand: "Intel", tech: "OpenVINO", tag: ":1.5.1-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" },
                { brand: "Apple", tech: "ARM64 Optimized", tag: ":1.5.1-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" },
                { brand: "CPU", tech: "Multi-Thread", tag: ":1.5.1-cpu", models: "Any x86 / ARM64", rating: 2, color: "#6366f1" }
            ]
        },
        changelog: {
            tag: "History",
            title1: "What's", title2: "new.",
            versions: [
                { ver: "1.5.1.1", date: "Feb 2026", type: "Hotfix", items: ["Fixed: SSH config not saving/loading correctly", "Added: Test SSH Connection button functional", "Added: Backend API /api/upscaler/ssh/test"] },
                { ver: "1.5.1.0", date: "Jan 2026", type: "Feature", items: ["SSH Remote Transcoding via Docker", "Multi-Architecture Docker images", "Path Mapping (local ↔ remote)", "SSH Key & Password auth", "Enhanced settings UI"] },
                { ver: "1.5.0.0", date: "Jan 2026", type: "Major", items: ["Docker Microservice Architecture", "Plugin size: 417 MB → 1.6 MB", "OpenCV DNN Models (FSRCNN, ESPCN, etc.)", "Web UI for model management", "Fixed version format for Jellyfin"] },
                { ver: "1.4.1", date: "Dec 2025", type: "Stable", items: ["Improved hardware detection", "UI refinements", "Bug fixes"] },
                { ver: "1.4.0", date: "Nov 2025", type: "Major", items: ["Redesigned UI for Jellyfin 10.10+", "Real hardware detection", "Side-by-side comparison preview", "14 AI model support"] }
            ]
        },
        footer: { copyright: "© 2026 Kuschel-code. MIT License." }
    },
    de: {
        nav: { home: "Startseite", installation: "Installation", configuration: "Konfiguration", features: "Funktionen", troubleshooting: "Fehlerbehebung", dockerTags: "Docker Tags", changelog: "Änderungen" },
        hero: {
            badge: "v1.5.1 — SSH Remote Transcoding Edition",
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
                { title: "BadImageFormatException", desc: "Assembly-Ladefehler mit nativen DLLs.", solutions: ["Das ist das alte v1.4.x Problem", "Auf v1.5.0+ upgraden (Docker)", "Alle alten DLLs aus Plugin-Ordner entfernen"] },
                { title: "GPU nicht erkannt", desc: "Container läuft im CPU-Modus trotz GPU.", solutions: ["nvidia-container-toolkit installieren", "docker --gpus all testen", "/dev/dri Berechtigungen prüfen (Intel/AMD)"], commands: [{ label: "GPU-Zugriff testen", code: "docker run --rm --gpus all nvidia/cuda:12.2.2-base-ubuntu22.04 nvidia-smi" }] },
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
                { brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.1", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" },
                { brand: "AMD", tech: "ROCm", tag: ":1.5.1-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" },
                { brand: "Intel", tech: "OpenVINO", tag: ":1.5.1-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" },
                { brand: "Apple", tech: "ARM64 Optimiert", tag: ":1.5.1-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" },
                { brand: "CPU", tech: "Multi-Thread", tag: ":1.5.1-cpu", models: "Beliebig x86 / ARM64", rating: 2, color: "#6366f1" }
            ]
        },
        changelog: {
            tag: "Verlauf",
            title1: "Was gibt's", title2: "Neues.",
            versions: [
                { ver: "1.5.1.1", date: "Feb 2026", type: "Hotfix", items: ["Behoben: SSH-Konfiguration wurde nicht gespeichert", "Hinzugefügt: SSH-Verbindungstest Button", "Hinzugefügt: API /api/upscaler/ssh/test"] },
                { ver: "1.5.1.0", date: "Jan 2026", type: "Feature", items: ["SSH Remote Transcoding via Docker", "Multi-Architektur Docker Images", "Pfad-Mapping (lokal ↔ remote)", "SSH Key & Passwort Auth", "Erweiterte Einstellungs-UI"] },
                { ver: "1.5.0.0", date: "Jan 2026", type: "Major", items: ["Docker Microservice Architektur", "Plugin-Größe: 417 MB → 1,6 MB", "OpenCV DNN Modelle", "Web UI für Modellverwaltung", "Versionsformat für Jellyfin korrigiert"] },
                { ver: "1.4.1", date: "Dez 2025", type: "Stabil", items: ["Verbesserte Hardwareerkennung", "UI-Verbesserungen", "Fehlerbehebungen"] },
                { ver: "1.4.0", date: "Nov 2025", type: "Major", items: ["Redesigntes UI für Jellyfin 10.10+", "Echte Hardwareerkennung", "Vergleichsvorschau", "14 KI-Modelle"] }
            ]
        },
        footer: { copyright: "© 2026 Kuschel-code. MIT-Lizenz." }
    },
    fr: {
        nav: { home: "Accueil", installation: "Installation", configuration: "Configuration", features: "Fonctionnalités", troubleshooting: "Dépannage", dockerTags: "Docker Tags", changelog: "Historique" },
        hero: {
            badge: "v1.5.1 — Édition SSH Remote Transcoding",
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
            recommended: "Recommandé", optionA: "Docker Hub (Pull)", optionB: "Build local", withGpu: "Avec GPU NVIDIA",
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
                { brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.1", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" },
                { brand: "AMD", tech: "ROCm", tag: ":1.5.1-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" },
                { brand: "Intel", tech: "OpenVINO", tag: ":1.5.1-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" },
                { brand: "Apple", tech: "ARM64 Optimisé", tag: ":1.5.1-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" },
                { brand: "CPU", tech: "Multi-Thread", tag: ":1.5.1-cpu", models: "x86 / ARM64", rating: 2, color: "#6366f1" }
            ]
        },
        changelog: {
            tag: "Historique", title1: "Quoi de", title2: "neuf.",
            versions: [
                { ver: "1.5.1.1", date: "Fév 2026", type: "Correctif", items: ["Corrigé : Config SSH non sauvegardée", "Ajouté : Bouton test SSH", "Ajouté : API /api/upscaler/ssh/test"] },
                { ver: "1.5.1.0", date: "Jan 2026", type: "Feature", items: ["SSH Remote Transcoding", "Images Docker multi-arch", "Mapping de chemins", "Auth SSH clé & mot de passe"] },
                { ver: "1.5.0.0", date: "Jan 2026", type: "Majeur", items: ["Architecture Microservice Docker", "Taille : 417 Mo → 1,6 Mo", "Modèles OpenCV DNN", "Interface Web"] },
                { ver: "1.4.0", date: "Nov 2025", type: "Majeur", items: ["Interface redessinée", "Détection matérielle", "Aperçu comparatif"] }
            ]
        },
        footer: { copyright: "© 2026 Kuschel-code. Licence MIT." }
    },
    zh: {
        nav: { home: "首页", installation: "安装", configuration: "配置", features: "功能", troubleshooting: "故障排除", dockerTags: "Docker 标签", changelog: "更新日志" },
        hero: { badge: "v1.5.1 — SSH远程转码版", title1: "用人工智能", title2: "转换您的媒体。", subtitle: "使用神经网络将SD升级到4K。支持NVIDIA、AMD、Intel和Apple Silicon的GPU加速Docker微服务。", getStarted: "开始使用", viewGithub: "在GitHub上查看", stats: { gpus: "GPU架构", size: "插件大小", upscale: "升级", license: "开源" } },
        features: { tag: "功能", title1: "你需要的一切。", title2: "没有多余的。", docker: { title: "Docker微服务", desc: "AI处理在隔离容器中运行——无DLL冲突。仅1.6 MB。" }, ssh: { title: "SSH远程转码", desc: "通过SSH将FFmpeg卸载到GPU容器。" }, gpu: { title: "5种GPU架构", desc: "NVIDIA CUDA、AMD ROCm、Intel OpenVINO、Apple Silicon、CPU。" }, ai: { title: "神经网络模型", desc: "FSRCNN、ESPCN、LapSRN、EDSR、Real-ESRGAN。" }, ui: { title: "无缝集成", desc: "播放器按钮、对比预览、实时基准测试和Web UI。" } },
        installation: { tag: "入门", title1: "几分钟", title2: "即可启动。", warning: "重要提示", warningText: "此插件需要Docker容器。插件仅~1.6 MB，所有AI计算在Docker中完成。", step1: "启动Docker容器", step1desc: "拉取并运行匹配GPU的镜像。", recommended: "推荐", optionA: "Docker Hub", optionB: "本地构建", withGpu: "NVIDIA GPU", step2: "安装插件", step2desc: "将插件仓库添加到Jellyfin。", addRepo: "添加仓库URL", addRepoPath: "仪表板 → 插件 → 仓库 → 添加", installPlugin: "从目录安装", installPluginPath: "目录 → 常规 → AI Upscaler → 安装", restartJellyfin: "重启Jellyfin", restartText: "安装后重启服务器。", configureUrl: "配置AI服务URL", configureUrlText: "设置Docker容器URL：", done: "完成！", doneText: "插件已安装就绪。", tip: "💡 提示：", tipText: "将YOUR_SERVER_IP替换为Docker主机IP：" },
        configuration: { tag: "设置", title1: "完全控制", title2: "触手可及。", basic: "基本设置", hardware: "硬件", remote: "远程转码(SSH)", ui: "界面", advanced: "高级", fields: { enable: "启用插件", serviceUrl: "AI服务URL", model: "AI模型", scale: "缩放倍数", quality: "质量级别", hwAccel: "硬件加速", maxVram: "最大显存(MB)", cpuThreads: "CPU线程", enableRemote: "远程转码", remoteHost: "远程主机", sshPort: "SSH端口", sshUser: "SSH用户", sshKey: "SSH密钥文件", localPath: "本地媒体路径", remotePath: "远程媒体路径", showButton: "显示播放器按钮", buttonPos: "按钮位置", notifications: "通知", comparison: "对比视图", metrics: "性能指标", cache: "预处理缓存", cacheSize: "缓存大小(MB)" } },
        troubleshooting: { tag: "帮助", title1: "常见问题。", title2: "快速修复。", problems: [{ title: "插件显示'不支持'", desc: "插件无法加载。", solutions: ["卸载旧版本", "删除旧插件文件夹", "重启Jellyfin", "重新安装"] }, { title: "容器无法启动", desc: "Docker容器立即退出。", solutions: ["检查日志", "验证GPU驱动", "检查端口冲突"], commands: [{ label: "查看日志", code: "docker logs jellyfin-ai-upscaler --tail 50" }] }], solution: "解决方案", commands: "常用命令", needHelp: "还需要帮助？", githubIssues: "GitHub Issues", githubWiki: "GitHub Wiki" },
        dockerTags: { tag: "Docker", title1: "选择你的", title2: "镜像。", cards: [{ brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.1", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" }, { brand: "AMD", tech: "ROCm", tag: ":1.5.1-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" }, { brand: "Intel", tech: "OpenVINO", tag: ":1.5.1-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" }, { brand: "Apple", tech: "ARM64优化", tag: ":1.5.1-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" }, { brand: "CPU", tech: "多线程", tag: ":1.5.1-cpu", models: "任意x86/ARM64", rating: 2, color: "#6366f1" }] },
        changelog: { tag: "日志", title1: "最新", title2: "更新。", versions: [{ ver: "1.5.1.1", date: "2026年2月", type: "修复", items: ["修复：SSH配置未保存", "新增：SSH连接测试按钮", "新增：API端点"] }, { ver: "1.5.0.0", date: "2026年1月", type: "重大", items: ["Docker微服务架构", "插件大小：417MB→1.6MB", "Web UI管理界面"] }] },
        footer: { copyright: "© 2026 Kuschel-code。MIT许可证。" }
    },
    ru: {
        nav: { home: "Главная", installation: "Установка", configuration: "Настройки", features: "Возможности", troubleshooting: "Устранение неполадок", dockerTags: "Docker Теги", changelog: "История изменений" },
        hero: { badge: "v1.5.1 — SSH Remote Transcoding", title1: "Преобразуйте медиа", title2: "с помощью ИИ.", subtitle: "Масштабируйте SD до 4K с помощью нейросетей. GPU-ускоренный Docker-микросервис для Jellyfin.", getStarted: "Начать", viewGithub: "GitHub", stats: { gpus: "Архитектур GPU", size: "Размер плагина", upscale: "Масштабирование", license: "Open Source" } },
        features: { tag: "Возможности", title1: "Всё что нужно.", title2: "Ничего лишнего.", docker: { title: "Docker Микросервис", desc: "ИИ работает в изолированном контейнере — без конфликтов DLL. Всего 1,6 МБ." }, ssh: { title: "SSH Remote Transcoding", desc: "Перенаправьте FFmpeg на GPU-контейнеры через SSH." }, gpu: { title: "5 архитектур GPU", desc: "NVIDIA CUDA, AMD ROCm, Intel OpenVINO, Apple Silicon, CPU." }, ai: { title: "Модели нейросетей", desc: "FSRCNN, ESPCN, LapSRN, EDSR, Real-ESRGAN." }, ui: { title: "Бесшовная интеграция", desc: "Кнопка плеера, предпросмотр, бенчмарк и Web UI." } },
        installation: { tag: "Начало", title1: "Запуск", title2: "за минуты.", warning: "Важно", warningText: "Плагин требует Docker-контейнер.", step1: "Запустить Docker", step1desc: "Скачайте образ для вашей GPU.", recommended: "Рекомендуется", optionA: "Docker Hub", optionB: "Сборка", withGpu: "NVIDIA GPU", step2: "Установить плагин", step2desc: "Добавьте репозиторий.", addRepo: "URL репозитория", addRepoPath: "Панель → Плагины → Репозитории → Добавить", installPlugin: "Установить из каталога", installPluginPath: "Каталог → AI Upscaler → Установить", restartJellyfin: "Перезапустить Jellyfin", restartText: "Перезапустите сервер.", configureUrl: "Настроить URL", configureUrlText: "URL контейнера Docker:", done: "Готово!", doneText: "Плагин установлен.", tip: "💡 Совет:", tipText: "Замените YOUR_SERVER_IP:" },
        configuration: { tag: "Настройки", title1: "Полный контроль", title2: "в ваших руках.", basic: "Основные", hardware: "Аппаратное обеспечение", remote: "Удалённое транскодирование", ui: "Интерфейс", advanced: "Продвинутые", fields: { enable: "Включить плагин", serviceUrl: "URL ИИ-сервиса", model: "Модель ИИ", scale: "Масштаб", quality: "Качество", hwAccel: "Аппаратное ускорение", maxVram: "Макс VRAM (МБ)", cpuThreads: "Потоки CPU", enableRemote: "Удалённый транскодинг", remoteHost: "Хост", sshPort: "SSH порт", sshUser: "SSH пользователь", sshKey: "SSH ключ", localPath: "Локальный путь", remotePath: "Удалённый путь", showButton: "Кнопка плеера", buttonPos: "Позиция", notifications: "Уведомления", comparison: "Сравнение", metrics: "Метрики", cache: "Кэш", cacheSize: "Размер кэша (МБ)" } },
        troubleshooting: { tag: "Помощь", title1: "Частые проблемы.", title2: "Быстрые решения.", problems: [{ title: "Плагин 'Не поддерживается'", desc: "Плагин не загружается.", solutions: ["Удалить старые версии", "Очистить папку плагинов", "Перезапустить Jellyfin", "Переустановить"] }], solution: "Решение", commands: "Команды", needHelp: "Нужна помощь?", githubIssues: "GitHub Issues", githubWiki: "GitHub Wiki" },
        dockerTags: { tag: "Docker", title1: "Выберите", title2: "образ.", cards: [{ brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.1", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" }, { brand: "AMD", tech: "ROCm", tag: ":1.5.1-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" }, { brand: "Intel", tech: "OpenVINO", tag: ":1.5.1-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" }, { brand: "Apple", tech: "ARM64", tag: ":1.5.1-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" }, { brand: "CPU", tech: "Multi-Thread", tag: ":1.5.1-cpu", models: "x86 / ARM64", rating: 2, color: "#6366f1" }] },
        changelog: { tag: "Журнал", title1: "Что", title2: "нового.", versions: [{ ver: "1.5.1.1", date: "Фев 2026", type: "Исправ.", items: ["SSH конфигурация исправлена", "Кнопка теста SSH", "API эндпоинт"] }, { ver: "1.5.0.0", date: "Янв 2026", type: "Мажорный", items: ["Docker микросервис", "1,6 МБ вместо 417 МБ", "Web UI"] }] },
        footer: { copyright: "© 2026 Kuschel-code. Лицензия MIT." }
    },
    ja: {
        nav: { home: "ホーム", installation: "インストール", configuration: "設定", features: "機能", troubleshooting: "トラブルシューティング", dockerTags: "Docker タグ", changelog: "変更履歴" },
        hero: { badge: "v1.5.1 — SSHリモートトランスコーディング版", title1: "AIでメディアを", title2: "変換する。", subtitle: "ニューラルネットワークでSDを4Kにアップスケール。NVIDIA、AMD、Intel、Apple Silicon対応のGPU対応Dockerマイクロサービス。", getStarted: "始める", viewGithub: "GitHub", stats: { gpus: "GPUアーキテクチャ", size: "プラグインサイズ", upscale: "アップスケール", license: "オープンソース" } },
        features: { tag: "機能", title1: "必要なものすべて。", title2: "余計なものなし。", docker: { title: "Dockerマイクロサービス", desc: "AI処理は隔離されたコンテナで実行。わずか1.6MBのプラグイン。" }, ssh: { title: "SSHリモートトランスコーディング", desc: "SSH経由でFFmpegをGPUコンテナに委託。" }, gpu: { title: "5つのGPUアーキテクチャ", desc: "NVIDIA CUDA、AMD ROCm、Intel OpenVINO、Apple Silicon、CPU。" }, ai: { title: "ニューラルネットワークモデル", desc: "FSRCNN、ESPCN、LapSRN、EDSR、Real-ESRGAN。" }, ui: { title: "シームレスな統合", desc: "プレーヤーボタン、プレビュー比較、ベンチマーク、Web UI。" } },
        installation: { tag: "はじめに", title1: "数分で", title2: "起動。", warning: "重要", warningText: "このプラグインにはDockerコンテナが必要です。", step1: "Dockerコンテナを起動", step1desc: "GPUに合うイメージを取得して実行。", recommended: "推奨", optionA: "Docker Hub", optionB: "ローカルビルド", withGpu: "NVIDIA GPU", step2: "プラグインをインストール", step2desc: "Jellyfinにプラグインリポジトリを追加。", addRepo: "リポジトリURLを追加", addRepoPath: "ダッシュボード → プラグイン → リポジトリ → 追加", installPlugin: "カタログからインストール", installPluginPath: "カタログ → AI Upscaler → インストール", restartJellyfin: "Jellyfinを再起動", restartText: "インストール後にサーバーを再起動。", configureUrl: "AIサービスURLを設定", configureUrlText: "DockerコンテナのURL：", done: "完了！", doneText: "プラグインの準備完了。", tip: "💡 ヒント：", tipText: "YOUR_SERVER_IPをDockerホストIPに置き換え：" },
        configuration: { tag: "設定", title1: "完全な制御を", title2: "手の中に。", basic: "基本設定", hardware: "ハードウェア", remote: "リモートトランスコーディング", ui: "UI設定", advanced: "詳細", fields: { enable: "プラグイン有効", serviceUrl: "AIサービスURL", model: "AIモデル", scale: "スケール倍率", quality: "品質レベル", hwAccel: "ハードウェアアクセラレーション", maxVram: "最大VRAM(MB)", cpuThreads: "CPUスレッド", enableRemote: "リモートトランスコーディング", remoteHost: "リモートホスト", sshPort: "SSHポート", sshUser: "SSHユーザー", sshKey: "SSH鍵ファイル", localPath: "ローカルメディアパス", remotePath: "リモートメディアパス", showButton: "プレーヤーボタン", buttonPos: "ボタン位置", notifications: "通知", comparison: "比較ビュー", metrics: "パフォーマンス", cache: "プリキャッシュ", cacheSize: "キャッシュサイズ(MB)" } },
        troubleshooting: { tag: "ヘルプ", title1: "よくある問題。", title2: "素早い解決。", problems: [{ title: "プラグインが「サポートされていない」", desc: "プラグインが読み込めない。", solutions: ["古いバージョンをアンインストール", "古いフォルダを削除", "Jellyfinを再起動", "再インストール"] }], solution: "解決策", commands: "コマンド", needHelp: "まだ助けが必要？", githubIssues: "GitHub Issues", githubWiki: "GitHub Wiki" },
        dockerTags: { tag: "Docker", title1: "イメージを", title2: "選択。", cards: [{ brand: "NVIDIA", tech: "CUDA + TensorRT", tag: ":1.5.1", models: "RTX 40/30/20, GTX 16/10", rating: 5, color: "#76b900" }, { brand: "AMD", tech: "ROCm", tag: ":1.5.1-amd", models: "RX 7000, RX 6000", rating: 4, color: "#ed1c24" }, { brand: "Intel", tech: "OpenVINO", tag: ":1.5.1-intel", models: "Arc A-Series, Iris Xe", rating: 4, color: "#0071c5" }, { brand: "Apple", tech: "ARM64最適化", tag: ":1.5.1-apple", models: "M1, M2, M3, M4", rating: 3, color: "#a2aaad" }, { brand: "CPU", tech: "マルチスレッド", tag: ":1.5.1-cpu", models: "x86 / ARM64", rating: 2, color: "#6366f1" }] },
        changelog: { tag: "履歴", title1: "新機能", title2: "のご紹介。", versions: [{ ver: "1.5.1.1", date: "2026年2月", type: "修正", items: ["SSH設定の保存修正", "SSH接続テストボタン追加", "APIエンドポイント追加"] }, { ver: "1.5.0.0", date: "2026年1月", type: "メジャー", items: ["Dockerマイクロサービスアーキテクチャ", "プラグインサイズ削減", "Web UI"] }] },
        footer: { copyright: "© 2026 Kuschel-code。MITライセンス。" }
    }
};
