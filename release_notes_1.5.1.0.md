# v1.5.1.0: Remote Transcoding (SSH)

This release implements the "rffmpeg" architecture, allowing true hardware-accelerated transcoding by offloading FFmpeg processing to a remote Docker container via SSH tunneling.

### Features
*   **Remote Transcoding**: Connects to Docker via SSH to execute FFmpeg commands.
*   **Path Mapping**: Configuration options to map local Jellyfin paths (e.g., `Z:\Movies`) to remote Docker paths (e.g., `/media/movies`).
*   **Multi-Architecture Docker**: Updated Docker images for NVIDIA, Intel Arc/iGPU, Apple Silicon, and CPU-only.
*   **Enhanced UI**: New settings section for SSH credentials and path mappings.

### Installation
1.  Install the ZIP file in Jellyfin.
2.  Pull the latest Docker image: `docker pull kuscheltier/jellyfin-ai-upscaler:1.5.1` (or `:1.5.1-intel`, `:1.5.1-apple`).
3.  Configure SSH settings in the plugin configuration page.
