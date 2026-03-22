#!/usr/bin/env python3
"""Convert VSR PyTorch models to ONNX for JellyfinUpscalerPlugin.

Usage:
    python convert_to_onnx.py --model edvr-m --output edvr_m_x4.onnx
    python convert_to_onnx.py --model realbasicvsr --output realbasicvsr_x4.onnx
    python convert_to_onnx.py --model animesr --output animesr_v2_x4.onnx

Requirements:
    pip install torch torchvision basicsr onnx onnxruntime
"""
import argparse
import torch
import numpy as np


def convert_edvr_m(output_path: str, num_frames: int = 5):
    """Convert EDVR-M (no deformable conv) to ONNX."""
    # Import from basicsr
    from basicsr.archs.edvr_arch import EDVR

    model = EDVR(
        num_in_ch=3, num_out_ch=3, num_feat=64,
        num_frame=num_frames, deformable_groups=0,  # M variant: no deformable conv
        num_extract_block=5, num_reconstruct_block=10,
        center_frame_idx=num_frames // 2,
        with_tsa=False  # M variant: no temporal-spatial attention
    )
    model.eval()

    # Download pretrained weights if available
    # For now, export with random weights as proof-of-concept
    dummy_input = torch.randn(1, num_frames, 3, 64, 64)

    torch.onnx.export(
        model, dummy_input, output_path,
        opset_version=17,
        input_names=['input'],
        output_names=['output'],
        dynamic_axes={
            'input': {3: 'height', 4: 'width'},
            'output': {2: 'out_height', 3: 'out_width'}
        }
    )
    print(f"Exported EDVR-M to {output_path}")

    # Validate
    import onnxruntime as ort
    session = ort.InferenceSession(output_path)
    test_input = np.random.randn(1, num_frames, 3, 64, 64).astype(np.float32)
    result = session.run(None, {'input': test_input})[0]
    print(f"Input shape: {test_input.shape}, Output shape: {result.shape}")
    assert result.shape[2] == 64 * 4, f"Expected 4x upscale, got {result.shape}"
    print("Validation passed!")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Convert VSR models to ONNX")
    parser.add_argument("--model", choices=["edvr-m", "realbasicvsr", "animesr"], required=True)
    parser.add_argument("--output", required=True, help="Output ONNX file path")
    parser.add_argument("--num-frames", type=int, default=5)
    args = parser.parse_args()

    if args.model == "edvr-m":
        convert_edvr_m(args.output, args.num_frames)
    else:
        print(f"Model {args.model} conversion not yet implemented. Coming in Phase 2/3.")
