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

## Reproducibility: what each variant actually guarantees

Since v1.8.3.20 the images install a resolved `.lock` instead of the `.txt` with
ranges, and five of seven verify every wheel's hash.

| Variant | Installs | `--require-hashes` |
|---|---|:--:|
| `docker7` (NVIDIA) | `requirements-nvidia.lock` | yes |
| `docker7-cpu` | `requirements-cpu.lock` | yes |
| `docker7-intel` | `requirements-intel.lock` | yes |
| `docker7-vulkan` | `requirements-vulkan.lock` | yes |
| `docker7-apple` | `requirements-apple.lock` | yes |
| `docker7-converter` | `requirements-converter.txt` | **no** |
| `docker7-amd` | `requirements-amd.txt` | **no** |

The two exceptions are deliberate and neither is a shortcut:

- **converter** pulls CPU-only torch from `--extra-index-url download.pytorch.org`,
  which pip-tools cannot generate hashes against. Removing that index would pull
  PyPI's torch, i.e. the CUDA build, taking the image from 0.27 GB of shared base
  plus deps to roughly 2.5 GB - a very large price for a hash.
- **amd** resolves its stack inside the ROCm base. It still keeps `numpy<2` and
  `opencv<4.12` (see below); locking it here would fight that resolution rather than
  record it.

Both still pin versions through their `.txt` ranges. What they do without is the
content guarantee - a re-uploaded wheel at the same version would install.

`intel` and `nvidia` had **never** produced a lock before v1.8.3.20: the OpenVINO
base runs as a non-root user and could not write the output file, and the CUDA
runtime base ships no Python at all. Both are fixed in the generator workflow.

## The `docker7-amd` base (changed for issue #98)

Until v1.8.3.35 the base was `rocm/pytorch:rocm6.2_…_pytorch_2.3.0` (about 20 GB, almost all
of it a PyTorch build this service never used). It also could not work: the
`pip install "onnxruntime-rocm<=1.22.99"` line resolved to **1.22.2.post3**, which is built
against **ROCm 7.2.4** (`NEEDED libamdhip64.so.7`, `libhipblas.so.3`, `RUNPATH /opt/rocm-7.2.4/lib`),
while ROCm 6.2 ships `libamdhip64.so.6`. The ROCm EP library could not be opened and ONNX
Runtime silently ran on the CPU. `get_available_providers()` still listed
`ROCMExecutionProvider` (it reports the build, not a load), so the build guard passed. On top
of that, the service had no ROCm provider chain at all.

Now the base is `rocm/dev-ubuntu-22.04:7.2.4-complete` (ROCm runtime and libraries, no
PyTorch; about 7.3 GB compressed), and the wheel is pinned to exactly `1.22.2.post3`. **Bump the
two together or not at all.** The build fails if the ROCm EP library does not resolve against
the image (`ldd`, not `dlopen`: loading it initialises HIP, which aborts without a GPU).

Verified on 2026-10-07 by unpacking that base and running the Dockerfile's apt, pip and guard
steps in it: the guard passes, and it fails with `libamdhip64.so.7 => not found` when that
library is removed (the old mismatch). Without a GPU, creating a ROCm session **aborts the
process** (`HIP failure 100: no ROCm-capable device is detected`), so the service probes ROCm
in a child process first and falls back to the CPU with a reason in `/status` and `/doctor`.
Not verified: inference on a real AMD GPU (no AMD hardware in CI or the dev sandbox).

GPU targets of the wheel: gfx908, gfx942, gfx1030, gfx1100, gfx1101, gfx1200, gfx1201. Other
RDNA2/RDNA3 cards need `HSA_OVERRIDE_GFX_VERSION=10.3.0` / `11.0.0`.

**Dependency caps:** `numpy<2` and `opencv-contrib-python<4.12` were forced by the base's
torch 2.3. There is no torch now, so they can be lifted (opencv 4.12+ also closes
**CVE-2025-53644**, JPEG2000, not reachable here). They are kept for this change on purpose:
lifting them is a separate, behaviour-changing dependency jump that needs its own full
dry-run.

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
