# 🛠️ Installation

Follow these steps to install the Jellyfin AI Upscaler Plugin on your server.

## 📦 Option 1: Via Repository (Recommended)

1.  Open your Jellyfin Dashboard.
2.  Go to **Plugins** -> **Catalog**.
3.  Click the gear icon at the top right (Repositories).
4.  Add the repository URL: `https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/repository.json`
5.  Search for **AI Upscaler Plugin** in the catalog and click **Install**.
6.  Restart your Jellyfin server.

## 📂 Option 2: Manual Installation

1.  Download the latest `.zip` file from the [Releases page](https://github.com/Kuschel-code/JellyfinUpscalerPlugin/releases).
2.  Extract the contents into your Jellyfin `plugins` directory:
    *   **Windows**: `C:\ProgramData\Jellyfin\Server\plugins\AIUpscaler`
    *   **Linux**: `/var/lib/jellyfin/plugins/AIUpscaler`
    *   **Docker**: Map the `/plugins` volume accordingly.
3.  Restart your Jellyfin server.

## 📦 Adding AI Models

The plugin requires ONNX models to function.

1.  Create a folder named `models` in your plugin configuration directory:
    *   **Windows**: `%AppData%\Jellyfin-Server\plugins\configurations\JellyfinUpscalerPlugin\models`
    *   **Linux**: `/etc/jellyfin/plugins/configurations/JellyfinUpscalerPlugin/models`
2.  Download compatible `.onnx` models (e.g., Real-ESRGAN) and place them in this folder.
3.  Models will be automatically detected on the next start.

## ⚙️ Requirements

*   **Jellyfin Server v10.10.0** or higher.
*   **Graphics Card (Optional but recommended)**: NVIDIA GPU for CUDA or a DirectML compatible GPU for best performance.
*   **RAM**: At least 4GB (8GB+ recommended for 4K upscaling).
