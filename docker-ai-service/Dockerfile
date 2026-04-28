# === CPU Version ===
# FROM python:3.11-slim-bookworm

# === GPU Version (NVIDIA CUDA + cuDNN 9) ===
# CUDA 12.8 supports RTX 5000 series (Blackwell, sm_120) + RTX 4000/3000/2000
FROM nvidia/cuda:12.8.0-cudnn-runtime-ubuntu22.04

# Set environment variables
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV DEBIAN_FRONTEND=noninteractive
ENV NVIDIA_VISIBLE_DEVICES=all
ENV NVIDIA_DRIVER_CAPABILITIES=compute,utility
# Skip TensorRT by default — CUDA alone is more reliable across GPU types
# Set SKIP_TENSORRT=false to re-enable TensorRT if your GPU supports it
ENV SKIP_TENSORRT=true

# Build-time metadata — pass via: --build-arg APP_VERSION=1.6.1.16 --build-arg APP_COMMIT=$(git rev-parse --short HEAD)
ARG APP_VERSION=1.6.1.16
ARG APP_COMMIT=unknown
ENV APP_VERSION=${APP_VERSION} \
    APP_COMMIT=${APP_COMMIT}

LABEL org.opencontainers.image.title="Jellyfin AI Upscaler (NVIDIA CUDA)" \
      org.opencontainers.image.description="AI Upscaler Service with NVIDIA CUDA 12.8 + cuDNN 9 (RTX 50/40/30/20 + GTX 16/10)" \
      org.opencontainers.image.vendor="Kuschel-code" \
      org.opencontainers.image.source="https://github.com/Kuschel-code/JellyfinUpscalerPlugin" \
      org.opencontainers.image.documentation="https://github.com/Kuschel-code/JellyfinUpscalerPlugin/blob/main/docker-ai-service/README.md" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.revision="${APP_COMMIT}"

WORKDIR /app

# Install Python and system dependencies for OpenCV
# Note: TensorRT libraries are NOT installed — the CUDA base image does not include
# the TensorRT apt repo and SKIP_TENSORRT=true is the default. If TensorRT is needed,
# mount the TensorRT libs at runtime via -v or use the NVIDIA TensorRT container base.
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    libgl1 \
    libglib2.0-0 \
    libsm6 \
    libxext6 \
    libxrender1 \
    curl \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* \
    && ln -s /usr/bin/python3 /usr/bin/python

# Copy requirements first for better caching
COPY requirements.txt .

# Install Python dependencies (GPU version)
# Use python -m pip because the CUDA base image has pip3 not pip in PATH
# BuildKit cache mount persists pip's wheel cache across builds (shaves ~90% off rebuilds)
RUN --mount=type=cache,target=/root/.cache/pip \
    python -m pip install -r requirements.txt

# Copy application code
COPY app/ ./app/
COPY static/ ./static/
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Create non-root user for running the service
RUN groupadd -r appuser && useradd -r -g appuser -d /app -s /sbin/nologin appuser

# Create directories for models and cache
RUN mkdir -p /app/models /app/cache && \
    chown -R appuser:appuser /app

# Expose API port
EXPOSE 5000

# Run as non-root user
USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

# Run the application via entrypoint
ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "5000"]
