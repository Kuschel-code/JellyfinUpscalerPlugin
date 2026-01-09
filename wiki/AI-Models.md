# 🎨 AI Models

The AI Upscaler Plugin supports various neural networks, each optimized for different types of content and hardware performance.

## 🌟 Main Models

### **Real-ESRGAN**
*   **Best for**: Live-action movies, nature footage, photos.
*   **Advantages**: Excellent texture restoration, very realistic.
*   **Requirement**: High (NVIDIA RTX 30/40 recommended).

### **ESRGAN Pro**
*   **Best for**: Movies, TV shows.
*   **Advantages**: Good compromise between sharpness and naturalness.
*   **Requirement**: Medium.

### **SwinIR**
*   **Best for**: Complex scenes, image noise.
*   **Advantages**: Uses transformer technology for precise details.
*   **Requirement**: High.

### **Waifu2x**
*   **Best for**: Anime, cartoons, drawn art.
*   **Advantages**: Reduces compression artifacts in flat colors extremely well.
*   **Requirement**: Low to Medium.

## ⚡ Lightweight Models

### **FSRCNN / SRCNN**
*   **Best for**: Weaker hardware (NAS, older laptops).
*   **Advantages**: Very fast, significantly better than traditional scaling.
*   **Requirement**: Low.

## 📂 Installing Models
1.  Download the `.onnx` version of your desired model.
2.  Navigate to the plugin data folder:
    *   **Windows**: `%AppData%\Jellyfin-Server\plugins\configurations\JellyfinUpscalerPlugin\models`
    *   **Linux**: `/etc/jellyfin/plugins/configurations/JellyfinUpscalerPlugin/models`
3.  Place the file in the `models` folder.
4.  Restart Jellyfin so that the model appears in the settings.
