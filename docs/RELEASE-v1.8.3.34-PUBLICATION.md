# v1.8.3.34 — regular release

Published for Jellyfin 12.0+ / .NET 10 on 2026-09-26. Source/merge commit: `d46cff256dd92785343cfc7ba936d7c054814a4a` (PR #88). The owner approved the release; target-server acceptance was not run.

Contents: real-time upscaling starts again in the web client. Since v1.8.3.31 the player looked for the playing item in the page address, which Jellyfin 10.9 and later no longer contains, so every video was refused with "Video color metadata unavailable" (#86, #87). The player now reads the item from the video's stream address or the web client's playback request, and checks the version that plays. Documentation for the player button on Windows installs (#75).

Tests on the release tree: 454 C# (TRX verified), 212 Python, 12 script and 74 Node behaviour tests; Release build with 0 warnings; CI green on PR #88. A headless Chromium check against a server answering on the paths Jellyfin 12.1 uses reproduced the reported message with the v1.8.3.33 player and started real-time with this one. Unlike v1.8.3.33, the ZIP was **not** loaded into a Jellyfin 12.1 container: the build host ran out of memory, and Docker Desktop was OOM-killed twice when the container started. The change is limited to the player script and version stamps. No target-server playback, GPU or HDR certification.

[Release](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/tag/v1.8.3.34). Built with dotnet publish from the merge commit (informational version `1.8.3.34+d46cff2…`); exactly five runtime DLLs plus meta.json. The published ZIP was downloaded back and is byte-identical to the validated build; real MD5: `c7ef37ea0df7c1bcf1b5b7270329b676`, SHA-256 `7badfd76b81ab7b42e648828faf48d714d90bbe9d3ef52259410305ee0babb34`. All three feed entries are identical (ABI 12.0.0.0); 1.8.3.31 / ABI 10.11.8.0 is retained for Jellyfin 10.11, which still has the real-time defect. `Scripts/verify-release.ps1 -Tag v1.8.3.34` passed against GitHub.

## Docker

[Release-channel workflow](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/actions/runs/36211954284): seven backends, version 1.8.3.34 / revision d46cff2. The AI service code is unchanged since v1.8.3.33. Each `v1.8.3.34[-backend]` pin matches `docker7-v1.8.3.34[-backend]` and the rolling `docker7[-backend]` tag; NVIDIA `latest` matches too. Checked through the registry API, including the image labels. Registry verification is not a hardware or CVE-free certification.

| Backend | Digest |
|---|---|
| nvidia | `sha256:493292ce3ffe7afe1b26285f058b59e8dac98d12f7808677f93332fcea1a15c8` |
| amd | `sha256:9ba20a6fe2d04cb69f996f7d668720914b1f424a89358df2a58ee449557ac64a` |
| intel | `sha256:1dbe7b7cb4afb025e7bcf5f3cd898923f20bf07e9de033fcdb61959bf1f72fd0` |
| apple | `sha256:7fd33fb94e95d666ed7ad4bbc60157aee1d38618c6999ba74882dc5c19b83515` |
| vulkan | `sha256:358538e7a7920072a8f0a2f149c76e3df692b91383e3f25bff7c84631fe2e3ae` |
| cpu | `sha256:ef6f61cdc19bf241ef3c1deb8c1af3a538d644a924d92773bef1c5ca10b00ce3` |
| converter | `sha256:d524ea8d2e502ee6805007d0a19a2f29945a9d206849b2616c37fd8809c83115` |
