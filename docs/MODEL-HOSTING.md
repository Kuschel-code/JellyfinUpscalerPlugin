# Self-Hosting Models Flagged `[self-host required]`

Five models in the v1.6.1.12 catalog show `[self-host required]` in their name and remain `available: False` because no public ONNX mirror exists. They are real models with official PyTorch / checkpoint releases — but no maintainer has published a publicly-downloadable ONNX export. To turn them on for your own instance, you need to (1) export the weights to ONNX yourself, (2) host the file somewhere your AI service can reach, and (3) edit the URL in `docker-ai-service/app/main.py`.

## Affected models

| Key | Architecture | Source weights | Paper |
|---|---|---|---|
| `edvr-m-x4` | Multi-frame SR (5 frames) | [xinntao/EDVR](https://github.com/xinntao/EDVR) — `EDVR_M_x4_SR_REDS_official.pth` | CVPR 2019 |
| `realbasicvsr-x4` | Recurrent VSR + optical flow | [ckkelvinchan/RealBasicVSR](https://github.com/ckkelvinchan/RealBasicVSR) — `RealBasicVSR_x4.pth` | CVPR 2022 |
| `animesr-v2-x4` | Anime multi-frame VSR | [TencentARC/AnimeSR](https://github.com/TencentARC/AnimeSR) — `AnimeSR_v2.pth` | NeurIPS 2022 |
| `apisr-x3` | Single-image RRDB (anime/general) | [Kiteretsu77/APISR](https://github.com/Kiteretsu77/APISR) — `3x_APISR_RRDB_GAN_generator.pth` | CVPR 2024 |
| `nomos8k-hat-x4` | HAT transformer | [Phhofm/models](https://github.com/Phhofm/models) — `4xNomos8kSCHAT-S` | — |

`nomos8k-hat-x4` is a separate case: the URL works, but HAT ops fail on CPU ExecutionProvider in the current ONNX Runtime build. It only needs re-enabling once you run on CUDA/ROCm/OpenVINO.

## Exporting PyTorch → ONNX

For single-image models (APISR, HAT) the standard torch.onnx.export recipe works:

```python
import torch
model = load_model_from_pth("3x_APISR_RRDB_GAN_generator.pth")
model.eval()
dummy = torch.randn(1, 3, 256, 256)  # adjust for your typical tile size
torch.onnx.export(
    model, dummy, "apisr_x3.onnx",
    opset_version=17,
    input_names=["input"], output_names=["output"],
    dynamic_axes={"input": {2: "H", 3: "W"}, "output": {2: "H", 3: "W"}},
)
```

For multi-frame models (EDVR, RealBasicVSR, AnimeSR) the input tensor has a time dimension — `(1, 5, 3, H, W)` for 5-frame input. Check each repo's README for the exact forward signature; some require pre-computed optical flow as a second input which will not round-trip through ONNX cleanly. RealBasicVSR in particular uses a recurrent hidden-state pattern that needs manual unrolling before export.

## Hosting

After you have the `.onnx` file:

1. Create a public Hugging Face repo (e.g. `your-user/jellyfin-vsr-models`).
2. `git lfs track '*.onnx'` and push the file.
3. Edit `docker-ai-service/app/main.py`: replace the `url` value for the affected model with your resolved raw URL (`https://huggingface.co/your-user/jellyfin-vsr-models/resolve/main/YOUR_FILE.onnx`) and flip `available: True`.
4. Rebuild the Docker image (`docker build -t your-tag docker-ai-service/`) or, if you run the stock image, set the env var `MODEL_URL_OVERRIDES='{"edvr-m-x4":"https://..."}'` (this is currently wired only for a few keys — check `AVAILABLE_MODELS` for the override keys).

## Why we don't just bundle the weights

Each model has a different license: APISR is Apache-2.0, AnimeSR is custom (research-only redistribution requires attribution), RealBasicVSR is S-Lab with a non-commercial clause. Re-hosting them under the plugin's release would create a compliance surface we'd rather not own. Mirroring is your call per your deployment context.

## Publishing a shared mirror

If you do export ONNX weights under a license that permits redistribution and you're willing to share, open an issue at [github.com/Kuschel-code/JellyfinUpscalerPlugin/issues](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/issues) with a link and the license. If multiple users end up hosting the same file, we can canonicalize a community mirror and flip the model to `available: True` by default in a future release.
