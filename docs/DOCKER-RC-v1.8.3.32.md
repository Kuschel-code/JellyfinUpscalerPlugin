# Docker candidates v1.8.3.32

Verified on 2026-09-23. All seven [workflow jobs](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/actions/runs/35778659366) succeeded. Tags and commit pins have identical registry digests; ten platform configs report version 1.8.3.32 and revision 2dabc6d. NVIDIA defaults to SKIP_TENSORRT=true. This is build/registry verification, not GPU or target-hardware acceptance.

| Backend | Tag | Platforms | Digest |
|---|---|---|---|
| nvidia | `rc-v1.8.3.32` | amd64 | `sha256:fb0b6cc8ccd7795f3f15869f62ea6eec473eac76690fa83787939c57201e5e02` |
| amd | `rc-v1.8.3.32-amd` | amd64 | `sha256:de2af9ba8fbdcc9dc2066db52bb0974963a12bc2774833568689aaa6894e82cb` |
| intel | `rc-v1.8.3.32-intel` | amd64 | `sha256:e4f1b2d1100018b64a42c77cc0059486812f83181d7ee9e240f3a926b79edb67` |
| apple | `rc-v1.8.3.32-apple` | amd64, arm64 | `sha256:0b82b10a0111ff7985a9a047e87f37fa17af9cdaee5436054dab7bfbb42b8fe4` |
| vulkan | `rc-v1.8.3.32-vulkan` | amd64, arm64 | `sha256:6113fe3ba8440770729dfa32eb8e960541480567b2e9c5af1445d68ef7015840` |
| cpu | `rc-v1.8.3.32-cpu` | amd64, arm64 | `sha256:047c8cee88d38512371c072ec5806dfba6f61af792ec636e8550f491899cc47b` |
| converter | `rc-v1.8.3.32-converter` | amd64 | `sha256:d9cdcf3a4627022fae0fcdfc91af2ee6e73d01a804700f3124a4faf8f63014bd` |

All tags use `kuscheltier/jellyfin-ai-upscaler`. Exact commit pins use `rc-v1.8.3.32-2dabc6d[-backend]`. Stable `docker7[-backend]` and `latest` remain at 1.8.3.31. For example, test the CPU candidate with `docker pull kuscheltier/jellyfin-ai-upscaler:rc-v1.8.3.32-2dabc6d-cpu`; select the appropriate backend for your system. Preserve your configuration and old image before replacing a service.
