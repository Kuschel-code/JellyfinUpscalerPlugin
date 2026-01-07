# 📖 Usage Guide

Using the AI Upscaler Plugin is simple and intuitive.

## 🎮 The AI Player Button
When watching a movie or TV show, look for the **"🎮 AI"** button in the bottom right corner of the player (next to the settings cog).

### Quick Toggle
- **Click once**: Instantly toggles the AI upscaler on or off using your default settings.
- **Visual Feedback**: The button will glow blue when AI enhancement is active.

### Settings Menu
Click and hold or right-click the AI button to open the **Quick Settings Menu**:
- **Change Model**: Switch between different AI models on the fly.
- **Adjust Scale**: Change the upscaling factor (2x, 3x, 4x).
- **Toggle Cache**: Enable or disable the pre-processing cache for the current item.

## 📊 Monitoring Progress
When you start upscaling, a notification will appear in the top-right corner of the player showing the status:
- **"📦 Loading AI Model..."**: The plugin is preparing the ONNX session.
- **"🚀 AI Active"**: The video is being enhanced in real-time.
- **"⚠️ Fallback Active"**: The system detected high load and switched to a lighter model.

## 🔍 Using the Comparison Preview
If you're not sure which model looks best:
1.  Go to the **Dashboard** -> **Plugins** -> **AI Upscaler**.
2.  Scroll down to the **AI Comparison Preview**.
3.  Select the movie or episode you want to test.
4.  Click **Generate Preview**.
5.  Wait a few seconds for the AI to process a sample frame.
6.  Compare the "Original" and "AI Upscaled" images to see the difference in detail and sharpness.

## 💡 Tips for Best Results
- **Source Quality**: AI works best on high-bitrate SD or 720p HD content. Very noisy or highly compressed videos may produce artifacts.
- **Hardware**: For 4K upscaling, a dedicated GPU with at least 4GB of VRAM is recommended.
- **Models**: Use **Real-ESRGAN** for high-quality movies and **Waifu2x** specifically for anime.
