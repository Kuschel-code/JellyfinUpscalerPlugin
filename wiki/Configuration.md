# ⚙️ Configuration Guide

The AI Upscaler Plugin offers extensive settings to balance quality and performance.

## 🛠️ Basic Settings
- **Enable Plugin**: The master switch. If disabled, all upscaling logic is bypassed.
- **Default AI Model**: The neural network used for enhancement.
- **Scale Factor**: Choose between 2x, 3x, or 4x upscaling. Higher factors require significantly more processing power.
- **Quality Level**: Adjusts the internal precision of the models.

## 🔧 Hardware Settings
- **Enable Hardware Acceleration**: Strongly recommended if you have a GPU (NVIDIA, AMD, or Intel).
- **Max VRAM Usage**: Limit how much GPU memory the plugin can consume.
- **CPU Threads**: Number of concurrent threads for image processing. Set this to half of your physical cores for best stability.

## 📊 Live Hardware Status
This section shows real-time data from your server:
- **CPU Status**: Displays the detected processor and current core utilization.
- **GPU Status**: Shows the detected GPU (e.g., NVIDIA RTX 3080) and acceleration provider (CUDA/DirectML).

## 🔍 AI Comparison Preview
Use this tool to verify your settings:
1.  **Select Item**: Pick a movie or episode from the dropdown.
2.  **Generate**: Click the "Generate Preview" button.
3.  **Compare**: Look at the side-by-side images. The AI version will be on the right.

## 🎬 Video Player Integration
- **Show AI Button**: Toggle the visibility of the "🎮 AI" button in the player controls.
- **Button Position**: Choose where the button appears in the player bar.
- **Notifications**: Enable or disable the pop-up status messages during playback.
