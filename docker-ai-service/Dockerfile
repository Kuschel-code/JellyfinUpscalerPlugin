# === CPU Version ===
# FROM python:3.11-slim-bookworm

# === GPU Version (NVIDIA CUDA) ===
FROM nvidia/cuda:12.2.2-cudnn8-runtime-ubuntu22.04

# Set environment variables
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV DEBIAN_FRONTEND=noninteractive
ENV NVIDIA_VISIBLE_DEVICES=all
ENV NVIDIA_DRIVER_CAPABILITIES=compute,utility

WORKDIR /app

# Install Python and system dependencies for OpenCV + FULL TensorRT + OpenSSH
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    libgl1 \
    libglib2.0-0 \
    libsm6 \
    libxext6 \
    libxrender1 \
    curl \
    openssh-server \
    # TensorRT Runtime Libraries (for ONNX Runtime TensorRT EP)
    libnvinfer8 \
    libnvinfer-plugin8 \
    libnvparsers8 \
    libnvonnxparsers8 \
    libnvinfer-bin \
    && rm -rf /var/lib/apt/lists/* \
    && apt-get clean \
    && ln -s /usr/bin/python3 /usr/bin/python

# Configure SSH
RUN sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/' /etc/ssh/sshd_config \
    && sed -i 's/#PubkeyAuthentication yes/PubkeyAuthentication yes/' /etc/ssh/sshd_config

# Copy requirements first for better caching
COPY requirements.txt .

# Install Python dependencies (GPU version)
RUN pip install --no-cache-dir -r requirements.txt

# Copy application code
COPY app/ ./app/
COPY static/ ./static/
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Create directories for models, cache, and SSH runtime
RUN mkdir -p /app/models /app/cache /run/sshd

# Expose ports (API and SSH)
EXPOSE 5000 22

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

# Run the application via entrypoint
ENTRYPOINT ["/entrypoint.sh"]
CMD ["python", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "5000"]
