# ⚙️ Configuration Guide

The AI Upscaler Plugin offers extensive settings to optimally balance image quality and system performance.

## 🛠️ Basic Settings
- **Enable Plugin**: The main switch. If disabled, all upscaling logic is skipped.
- **Default AI Model**: The neural network used for enhancement (e.g., Real-ESRGAN).
- **Scale Factor**: Choose between 2x, 3x, or 4x upscaling. Higher factors require significantly more computing power.
- **Quality Level**: Adjusts the internal precision of the models (Low, Medium, High).

## 🔧 Hardware Settings
- **Hardware Acceleration**: Highly recommended if you own a GPU (NVIDIA, AMD, or Intel).
- **Max VRAM Usage**: Limits the graphics memory that the plugin may consume.
- **CPU Threads**: Number of simultaneous threads for image processing. Recommendation: Half of your physical cores for best stability.

## 📊 Live Hardware Status
This area displays real-time data from your server:
- **CPU Status**: Shows the detected processor and current core usage.
- **GPU Status**: Shows the detected GPU (e.g., NVIDIA RTX 3080) and the acceleration provider (CUDA/DirectML).

## 🔍 AI Comparison Preview
Use this tool to check your settings:
1.  **Select Item**: Choose a movie or an episode from the dropdown menu.
2.  **Generate**: Click on "✨ Generate Preview".
3.  **Compare**: View the images side-by-side. The AI-enhanced version is on the right.

## 🎬 Video Player Integration
- **Show Player Button**: Toggles the visibility of the "🎮 AI" button in the player controls.
- **Button Position**: Choose where the button should appear in the player bar.
- **Notifications**: Enables or disables status popups during playback.
