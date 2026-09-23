# v1.8.3.32 — regular release

Published for Jellyfin 12.0+ / .NET 10. Source/merge commit: `09162a4c8225110c5ee9fc5c7967626fa4a1eeaf`. The owner explicitly approved PR #83 and waived target-server acceptance.

443 C# tests passed against each of 12.0 and 12.1, 209 Python tests and 36 Node behavior tests passed. The 12.0 build loaded in an isolated official Jellyfin 12.1 container, injected the player script and reached Healthy startup. No target-server playback, GPU or HDR certification.

[Release](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases/tag/v1.8.3.32). Published ZIP downloaded and byte-compared; real MD5: `0cda1c6b1f9806737a686e2f56288a45`. Exactly five runtime DLLs plus meta.json; published with dotnet publish. All three new feed entries are identical with ABI 12.0.0.0; older 1.8.3.31 / ABI 10.11.8.0 retained. Full v1.8.3.31 online release validation passed. v1.8.3.32 online validation follows feed publication.

## Docker

[Regular workflow](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/actions/runs/35803815601). Seven backends, ten platform configurations; version 1.8.3.32 / revision 09162a4. Version pins match rolling tags and NVIDIA latest. SKIP_TENSORRT=true. Registry verification is not a hardware or CVE-free certification.

| Backend | Digest |
|---|---|
| nvidia | `sha256:d6e99cbb31842bd522c4eb433ff0654a069c915ea798f59ddf04c86d9851b729` |
| amd | `sha256:32d01742eb8e73a1827cf57eb4f81f75edc288817f624b5beb98401649b2e615` |
| intel | `sha256:16ea74d1a304399d9d60df0514da6faaf9e54cf0541708b9645ad16da7b9d256` |
| apple | `sha256:b98f2cefd7a74da3db173b5c3f2be46eba97b2c6476f2175cbbd90a8b0db2e78` |
| vulkan | `sha256:4eb784d74f516221634ce8dd1e72da181b56e4231eb86052ecec5e473b07a3e5` |
| cpu | `sha256:2984169be90f1cfbe972779a2b92656e40ff2d8b51251f2284c3096dd6d9c396` |
| converter | `sha256:c2a732625165b1c4c2793858aa06963fade0cd88b9a05d4aa301ae25415fa71f` |
