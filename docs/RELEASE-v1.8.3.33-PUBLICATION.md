# v1.8.3.33 — regular release

Published for Jellyfin 12.0+ / .NET 10 on 2026-09-24. Source/merge commit: `ce234d6ba9eeba7b2b63ce8e38a02a55910f22c7` (PR #85, on top of PR #84). The owner approved the release; target-server acceptance was not run.

Contents: redesigned in-player menu and settings page; SDR videos (DVD/SD rips, untagged 10-bit) no longer refused as HDR in real time or in library jobs; HDR output keeps the model's detail; Server AI hides stale frames during outages and hands over to Lanczos when the server cannot keep up.

Tests on the release tree: 454 C# (TRX verified), 212 Python, 12 script and 57 Node behaviour tests; Release build with 0 warnings. The release ZIP was loaded into an isolated official Jellyfin 12.1.0 container: all assemblies loaded, "Loaded plugin: AI Upscaler Plugin 1.8.3.33", player script injected, Healthy, and the served player script and settings page carry the 1.8.3.33 redesign. No target-server playback, GPU or HDR certification.

[Release](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/tag/v1.8.3.33). Built with dotnet publish from the merge commit; exactly five runtime DLLs plus meta.json. The published ZIP was downloaded back and is byte-identical to the validated build; real MD5: `0d9d50629f7a681e02ce8c3a60662b24`. All three feed entries are identical (ABI 12.0.0.0); 1.8.3.31 / ABI 10.11.8.0 is retained for Jellyfin 10.11. `Scripts/verify-release.ps1 -Tag v1.8.3.33` passed against GitHub.

## Docker

[Release-channel workflow](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/actions/runs/35998072311): seven backends, version 1.8.3.33 / revision ce234d6. Each `v1.8.3.33[-backend]` pin matches `docker7-v1.8.3.33[-backend]` and the rolling `docker7[-backend]` tag; NVIDIA `latest` matches too. SKIP_TENSORRT=true. Registry verification is not a hardware or CVE-free certification.

| Backend | Digest |
|---|---|
| nvidia | `sha256:41273e6dc4f41f15c756da4741adaa57b96dcbce2b614fc3562e011b764d0f9c` |
| amd | `sha256:a3c9626c93c12e427c8596b7fc2f747de10f7dd61104c40324ed9f5850a8a4e9` |
| intel | `sha256:5793e8b26b67db834dedf9137baaa2c761f2326b34e587ebb7863c9c2af337d6` |
| apple | `sha256:2d79226daf9850821665c9697baa66813a4a03a9faa74da476c147f411a874ed` |
| vulkan | `sha256:8504bf3e3dcc4d43d06fe9726d01cb6e732311183cba3b5baaa0b9ba32665c32` |
| cpu | `sha256:089145712dac4812991569854277d23180997014b0b225398f1c7cb236bb5775` |
| converter | `sha256:3b83ba4dc6f9db3d8ae2d4db52b8231f5eef190562a73d8553327e3f9f1371cf` |
