# Docker images — sizes, what they are for, and the frozen AMD stack

Measured from the Docker Hub registry manifests on 2026-07-25 for v1.8.3.19 (compressed
download size, `linux/amd64`). Pick deliberately: the AMD image is two orders
of magnitude larger than the CPU one.

| Tag | Download | Use it when | Notes |
|---|---:|---|---|
| `docker7-cpu` | **0.27 GB** | no usable GPU, or a NAS/mini-PC | multi-arch (amd64 + arm64) |
| `docker7-converter` | **0.53 GB** | you want to convert OpenModelDB `.pth` models to ONNX | CPU image + torch-cpu + spandrel; the CPU-only torch index keeps it small (only +0.26 GB over `docker7-cpu`) |
| `docker7-intel` | 0.67 GB | Intel Arc / Iris (OpenVINO) | |
| `docker7-apple` | 0.27 GB | macOS Apple Silicon | multi-arch; Docker on macOS cannot pass through the Apple GPU — CPU-only in practice, native run required for GPU |
| `docker7-vulkan` | 0.53 GB | AMD pre-RDNA2, Intel iGPU (ncnn/Vulkan) | multi-arch |
| `docker7` (NVIDIA) | 3.38 GB | NVIDIA CUDA + cuDNN 9 | also published as `latest` |
| `docker7-amd` | **20.36 GB** | AMD ROCm | see the frozen-stack section below before pulling |

> `docker7-apple` (0.51 -> 0.27 GB) and `docker7-vulkan` (0.98 -> 0.53 GB) shrank
> between v1.8.3.13 and v1.8.3.19. Both are re-measured here with the same method,
> not estimated. Apple now matches the CPU image, which is consistent with what the
> table already says: Docker on macOS cannot pass through the Apple GPU.

## Why `docker7-amd` is 20 GB

The base is `rocm/pytorch:rocm6.2_…_pytorch_2.3.0`, which ships a full PyTorch
build even though this service runs inference through **ONNX Runtime**. Almost
all of the 20 GB comes from that base, not from our layers.

It also forces a frozen dependency stack:

- `numpy>=1.24,<2.0` — the base's torch 2.3 is compiled against numpy 1.x; numpy 2 breaks it.
- `opencv-contrib-python>=4.10,<4.12` — opencv 4.12+ requires numpy 2, which the cap above forbids.

**Accepted risk:** the capped opencv carries **CVE-2025-53644**, which affects
JPEG2000 decoding. This service decodes PNG and JPEG frames only and never
touches JPEG2000, so the vulnerable path is not reachable from our code.

**Exit trigger — revisit when either becomes true:**

1. A ROCm base image with **torch ≥ 2.4** (numpy-2 compatible) is published, **or**
2. `onnxruntime-rocm` works on a slim base (e.g. `rocm/dev-ubuntu-22.04`) without the PyTorch payload.

Either one lets us drop both caps *and* cut the image size dramatically — the
frozen stack and the 20 GB are the same problem. The weekly
`lock-requirements` workflow re-resolves the AMD requirements against the base
and fails loudly if the caps ever stop holding, so this cannot rot silently.

## Converter image: RAM guidance

`/models/convert-from-catalog` and `/models/convert-upload` hold the model in
memory while converting: the downloaded `.pth`, the torch model, and the
exported ONNX coexist briefly. Rules of thumb:

- Typical community models (1–100 MB): unproblematic, ~1 GB peak.
- Near the 500 MB upload cap (`MAX_MODEL_UPLOAD_BYTES`): expect **2–4 GB peak**.
- The shipped `docker-compose.yml` sets `mem_limit: 8g`, which covers this. On a
  box with less RAM, lower `MAX_MODEL_UPLOAD_BYTES` accordingly.

Transformer architectures (DAT, SwinIR class) additionally need a CPU with
**AVX2** to convert — older CPUs fail with `Your CPU does not support FBGEMM`.
The UI shows that explanation verbatim. CNN models (Compact, ESRGAN, SPAN,
RealPLKSR — the large majority) convert on any CPU.

## Reproducible builds

Base images are pinned by `@sha256` digest in every Dockerfile. The Python
layer is resolved by the `lock-requirements` workflow **inside each variant's
real base image** (pre-installed packages change the outcome) and re-checked
weekly, so a floating transitive dependency cannot silently break the next
release — the failure mode that broke all six image builds in v1.8.3.4.

`APP_VERSION` carries no default in any Dockerfile: CI injects it from the
release tag, and `verify-release.ps1` fails if a default reappears. A stale
default is what made the service report a wrong version through `/status`,
`/health` and the dashboard before v1.8.3.13.
