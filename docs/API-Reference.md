# 🛠️ API Reference - AI Upscaler Plugin

## 🔗 **Base URL**
All API endpoints are relative to your Jellyfin server:
```
https://your-jellyfin-server:8096/api/upscaler/
```

---

## 🖥️ **Hardware Detection**

### **GET /api/upscaler/hardware**
Returns detailed hardware information and capabilities.

**Response Example:**
```json
{
  "success": true,
  "hardware": {
    "gpuVendor": "NVIDIA",
    "gpuModel": "GeForce RTX 4080",
    "gpuMemoryMB": 16384,
    "driverVersion": "537.13",
    "av1EncodeSupported": true,
    "av1DecodeSupported": true,
    "hevcEncodeSupported": true,
    "hevcDecodeSupported": true,
    "h264EncodeSupported": true,
    "maxConcurrentStreams": 4,
    "recommendedProfile": "gaming",
    "systemRamMB": 32768,
    "cpuCores": 16,
    "isMobile": false,
    "thermalThrottling": false,
    "currentTemperature": 42
  }
}
```

**Status Codes:**
- `200`: Success
- `500`: Hardware detection failed

---

## 🎬 **Video Processing**

### **POST /api/upscaler/process**
Processes a video file with AI upscaling.

**Request Body:**
```json
{
  "inputFile": "/media/movies/sample.mkv",
  "outputFile": "/media/processed/sample_4k.mkv", 
  "settings": {
    "preset": "gaming",
    "resolution": "4K",
    "codec": "av1",
    "quality": 23,
    "sharpness": 75,
    "hdrMode": "auto",
    "audioMode": "passthrough"
  }
}
```

**Response:**
```json
{
  "success": true,
  "jobId": "upscale_job_12345",
  "estimatedTimeMinutes": 45,
  "message": "Processing started successfully"
}
```

**Status Codes:**
- `200`: Processing started
- `400`: Invalid parameters
- `404`: Input file not found
- `503`: Service unavailable (max concurrent jobs reached)

---

## 📊 **Statistics & Monitoring**

### **GET /api/upscaler/stats**  
Returns real-time processing statistics.

**Response:**
```json
{
  "success": true,
  "stats": {
    "activeJobs": 2,
    "queuedJobs": 1,
    "totalJobsProcessed": 847,
    "averageProcessingTime": "32.5 minutes",
    "serverUptime": "7 days, 14 hours",
    "memoryUsageMB": 2048,
    "gpuUtilization": 85,
    "currentTemperature": 67,
    "thermalThrottling": false
  }
}
```

### **GET /api/upscaler/jobs**
Returns list of current and recent jobs.

**Response:**
```json
{
  "success": true,
  "jobs": [
    {
      "jobId": "upscale_job_12345",
      "status": "processing",
      "progress": 65,
      "inputFile": "sample.mkv",
      "outputFile": "sample_4k.mkv",
      "startTime": "2025-06-27T20:30:00Z",
      "estimatedCompletion": "2025-06-27T21:15:00Z",
      "preset": "gaming",
      "resolution": "4K"
    }
  ]
}
```

---

## 🎯 **Presets Management**

### **GET /api/upscaler/presets**
Returns available intelligent presets.

**Response:**
```json
{
  "success": true,
  "presets": [
    {
      "id": "gaming",
      "name": "Gaming",
      "description": "Optimized for game content and streaming",
      "icon": "🎮",
      "settings": {
        "resolution": "4K",
        "codec": "av1",
        "quality": 20,
        "sharpness": 75,
        "hdrMode": "hdr10",
        "audioMode": "surround71"
      },
      "hardwareRequirements": {
        "minGpuVram": 8192,
        "recommendedGpu": ["RTX 4070", "Arc A750"]
      }
    },
    {
      "id": "appletv", 
      "name": "Apple TV",
      "description": "Cinematic quality for movies and shows",
      "icon": "🍎",
      "settings": {
        "resolution": "4K",
        "codec": "av1",
        "quality": 23,
        "sharpness": 60,
        "hdrMode": "dolbyvision",
        "audioMode": "surround51"
      }
    }
  ]
}
```

### **POST /api/upscaler/presets**
Creates a custom preset.

**Request Body:**
```json
{
  "name": "My Custom Preset",
  "description": "Custom settings for anime content",
  "settings": {
    "resolution": "1440p",
    "codec": "hevc", 
    "quality": 25,
    "sharpness": 65,
    "hdrMode": "auto",
    "audioMode": "stereo"
  }
}
```

---

## ⚙️ **Configuration**

### **GET /api/upscaler/config**
Returns current plugin configuration.

**Response:**
```json
{
  "success": true,
  "config": {
    "enabled": true,
    "enableAV1": true,
    "maxConcurrentStreams": 2,
    "enableLightMode": false,
    "thermalThrottleTemperature": 85,
    "enableMobileOptimization": true,
    "defaultPreset": "auto",
    "enableDebugLogging": false
  }
}
```

### **POST /api/upscaler/config**
Updates plugin configuration.

**Request Body:**
```json
{
  "enabled": true,
  "maxConcurrentStreams": 3,
  "thermalThrottleTemperature": 80,
  "enableDebugLogging": true
}
```

---

## 🧪 **Testing & Diagnostics**

### **POST /api/upscaler/test-av1**
Tests AV1 encoding capability.

**Response:**
```json
{
  "success": true,
  "av1Available": true,
  "encoder": "av1_nvenc",
  "testResult": {
    "encodingSpeed": "2.3x realtime",
    "qualityScore": 42.5,
    "timeElapsed": "5.2 seconds"
  }
}
```

### **GET /api/upscaler/diagnostics**
Returns detailed diagnostic information.

**Response:**
```json
{
  "success": true,
  "diagnostics": {
    "ffmpegVersion": "6.0.1",
    "availableEncoders": ["av1_nvenc", "hevc_nvenc", "h264_nvenc"],
    "availableDecoders": ["av1", "hevc", "h264"],
    "systemInfo": {
      "os": "Windows 11",
      "jellyfin": "10.10.0",
      "plugin": "1.3.5"
    },
    "lastError": null,
    "performanceTests": [
      {
        "test": "1080p_to_4k_av1",
        "result": "2.8x realtime",
        "timestamp": "2025-06-27T20:00:00Z"
      }
    ]
  }
}
```

---

## 🎥 **Media Information**

### **GET /api/upscaler/media-info**  
Analyzes media file and suggests optimal settings.

**Query Parameters:**
- `file`: Path to media file

**Response:**
```json
{
  "success": true,
  "mediaInfo": {
    "filename": "sample.mkv",
    "duration": "02:15:30",
    "resolution": "1920x1080",
    "codec": "h264",
    "bitrate": 8500,
    "framerate": 23.976,
    "hdrType": "none",
    "audioChannels": 6,
    "contentType": "movie",
    "suggestedPreset": "appletv", 
    "suggestedResolution": "4K",
    "estimatedProcessingTime": "45 minutes"
  }
}
```

---

## 🔄 **Job Management**

### **GET /api/upscaler/jobs/{jobId}**
Gets detailed information about a specific job.

**Response:**
```json
{
  "success": true,
  "job": {
    "jobId": "upscale_job_12345",
    "status": "processing",
    "progress": 75,
    "inputFile": "/media/sample.mkv",
    "outputFile": "/media/sample_4k.mkv",
    "settings": {
      "preset": "gaming",
      "resolution": "4K",
      "codec": "av1"
    },
    "startTime": "2025-06-27T20:30:00Z",
    "currentStep": "Video encoding",
    "fps": 35.2,
    "eta": "10 minutes",
    "logs": [
      "Started processing at 20:30:00",
      "Hardware acceleration detected: av1_nvenc", 
      "Processing frame 15400 of 20600"
    ]
  }
}
```

### **DELETE /api/upscaler/jobs/{jobId}**
Cancels a running job.

**Response:**
```json
{
  "success": true,
  "message": "Job upscale_job_12345 cancelled successfully"
}
```

---

## 📱 **Mobile Optimization**

### **GET /api/upscaler/mobile-status**
Returns mobile-specific information and optimizations.

**Response:**
```json
{
  "success": true,
  "mobile": {
    "isMobileDevice": true,
    "batteryLevel": 75,
    "batteryOptimizationEnabled": true,
    "thermalThrottling": false,
    "recommendedSettings": {
      "maxResolution": "1080p",
      "codec": "h264",
      "quality": 28,
      "maxConcurrentJobs": 1
    },
    "touchOptimizationsEnabled": true
  }
}
```

---

## 🔐 **Authentication**

### **API Key Authentication**
All API requests require authentication. Include your API key in the header:

```http
Authorization: MediaBrowser Token="your-api-key-here"
```

### **Getting an API Key**
1. Jellyfin Admin Dashboard → API Keys
2. Create new API Key for "AI Upscaler Plugin"
3. Use the generated key for all requests

---

## 🚨 **Error Handling**

### **Common Error Responses**

#### **400 Bad Request**
```json
{
  "success": false,
  "error": "INVALID_PARAMETERS",
  "message": "Resolution '8K' is not supported",
  "details": {
    "parameter": "resolution", 
    "allowedValues": ["720p", "1080p", "1440p", "4K"]
  }
}
```

#### **404 Not Found**
```json
{
  "success": false,
  "error": "FILE_NOT_FOUND",
  "message": "Input file '/media/missing.mkv' does not exist"
}
```

#### **503 Service Unavailable**
```json
{
  "success": false,
  "error": "MAX_JOBS_REACHED", 
  "message": "Maximum concurrent jobs (2) reached. Please wait.",
  "retryAfter": 300
}
```

#### **500 Internal Server Error**
```json
{
  "success": false,
  "error": "PROCESSING_FAILED",
  "message": "FFmpeg encoding failed",
  "details": {
    "exitCode": 1,
    "stderr": "Encoder 'av1_nvenc' not found"
  }
}
```

---

## 📝 **Rate Limiting**

### **Limits**
- **Hardware detection**: 60 requests/minute
- **Job creation**: 10 requests/minute  
- **Statistics**: 120 requests/minute
- **Configuration**: 20 requests/minute

### **Headers**
Response includes rate limit headers:
```http
X-RateLimit-Limit: 60
X-RateLimit-Remaining: 45
X-RateLimit-Reset: 1640995200
```

---

## 🧩 **WebSocket Events**

### **Connection**
```javascript
const ws = new WebSocket('ws://your-jellyfin:8096/api/upscaler/websocket');
```

### **Event Types**

#### **Job Progress**
```json
{
  "type": "job_progress",
  "jobId": "upscale_job_12345",
  "progress": 85,
  "fps": 32.1,
  "eta": "5 minutes"
}
```

#### **Job Completed**
```json
{
  "type": "job_completed",
  "jobId": "upscale_job_12345", 
  "success": true,
  "outputFile": "/media/sample_4k.mkv",
  "processingTime": "42 minutes"
}
```

#### **Hardware Status**
```json
{
  "type": "hardware_status",
  "gpuUtilization": 90,
  "temperature": 72,
  "thermalThrottling": false
}
```

---

## 📚 **SDK Examples**

### **JavaScript/Node.js**
```javascript
const JellyfinUpscaler = {
  baseUrl: 'http://localhost:8096/api/upscaler',
  apiKey: 'your-api-key',
  
  async getHardware() {
    const response = await fetch(`${this.baseUrl}/hardware`, {
      headers: {
        'Authorization': `MediaBrowser Token="${this.apiKey}"`
      }
    });
    return await response.json();
  },
  
  async processVideo(inputFile, preset = 'auto') {
    const response = await fetch(`${this.baseUrl}/process`, {
      method: 'POST',
      headers: {
        'Authorization': `MediaBrowser Token="${this.apiKey}"`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        inputFile,
        settings: { preset }
      })
    });
    return await response.json();
  }
};
```

### **Python**  
```python
import requests

class JellyfinUpscaler:
    def __init__(self, base_url, api_key):
        self.base_url = f"{base_url}/api/upscaler"
        self.headers = {"Authorization": f"MediaBrowser Token=\"{api_key}\""}
    
    def get_hardware(self):
        response = requests.get(f"{self.base_url}/hardware", headers=self.headers)
        return response.json()
    
    def process_video(self, input_file, preset="auto"):
        data = {
            "inputFile": input_file,
            "settings": {"preset": preset}
        }
        response = requests.post(f"{self.base_url}/process", 
                               json=data, headers=self.headers)
        return response.json()
```

---

## 🔗 **Related Documentation**

- **[Installation Guide](Installation-Guide)** - Setting up the plugin
- **[Hardware Compatibility](Hardware-Compatibility)** - Supported GPUs
- **[Quick Settings UI](Quick-Settings-UI)** - User interface guide
- **[Performance Tuning](Performance-Tuning)** - Optimization tips