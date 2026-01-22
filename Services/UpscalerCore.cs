using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.IO;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using FFMpegCore;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using JellyfinUpscalerPlugin.Models;
using Image = SixLabors.ImageSharp.Image;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Core upscaling engine with real AI hardware acceleration - Phase 1 Implementation
    /// </summary>
    public class UpscalerCore : IDisposable
    {
        private readonly ILogger<UpscalerCore> _logger;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IFileSystem _fileSystem;
        private readonly IApplicationPaths _appPaths;
        
        // AI Model Sessions
        private readonly Dictionary<string, InferenceSession?> _modelSessions = new();
        private readonly Dictionary<string, SessionOptions> _sessionOptions = new();
        
        // Hardware detection cache with thread safety
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _hardwareCache = new();
        private static DateTime _lastHardwareCheck = DateTime.MinValue;
        private static readonly object _hardwareCacheLock = new();
        
        // Performance monitoring
        private readonly Dictionary<string, PerformanceMetrics> _performanceMetrics = new();
        
        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();
        
        private readonly ModelManager _modelManager;
        
        public UpscalerCore(
            ILogger<UpscalerCore> logger,
            IMediaEncoder mediaEncoder,
            IFileSystem fileSystem,
            IApplicationPaths appPaths,
            ModelManager modelManager)
        {
            _logger = logger;
            _mediaEncoder = mediaEncoder;
            _fileSystem = fileSystem;
            _appPaths = appPaths;
            _modelManager = modelManager;
            
            InitializeAIModels();
        }

        /// <summary>
        /// Initialize AI models with ONNX Runtime
        /// </summary>
        private void InitializeAIModels()
        {
            try
            {
                _logger.LogInformation("🚀 Initializing AI models with ONNX Runtime...");
                
                // Configure session options for GPU acceleration
                var sessionOptions = new SessionOptions
                {
                    LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING,
                    EnableCpuMemArena = true,
                    EnableMemoryPattern = true,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };

                // Try to enable GPU acceleration
                try
                {
                    if (Config.HardwareAcceleration)
                    {
                        // Try CUDA first
                        try
                        {
                            sessionOptions.AppendExecutionProvider_CUDA(0);
                            _logger.LogInformation("✅ CUDA GPU acceleration enabled");
                        }
                        catch
                        {
                            try
                            {
                                // Fall back to DirectML (Windows)
                                sessionOptions.AppendExecutionProvider_DML(0);
                                _logger.LogInformation("✅ DirectML GPU acceleration enabled");
                            }
                            catch
                            {
                                _logger.LogWarning("⚠️ GPU acceleration not available, using CPU");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to enable hardware acceleration");
                }

                _sessionOptions["default"] = sessionOptions;
                
                // Load available models
                LoadAvailableModels();
                
                _logger.LogInformation($"✅ AI Core initialized with {_modelSessions.Count} models");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize AI models");
            }
        }

        /// <summary>
        /// Load available AI models using ModelManager
        /// </summary>
        private void LoadAvailableModels()
        {
            // Define core models we want to ensure are available
            var coreModels = new[] { "fsrcnn-light", "fsrcnn", "esrgan", "realesrgan", "waifu2x" };
            
            _ = Task.Run(async () =>
            {
                foreach (var modelName in coreModels)
                {
                    try
                    {
                        var modelPath = await _modelManager.GetModelPathAsync(modelName);
                        
                        if (modelPath != null && File.Exists(modelPath))
                        {
                            var session = new InferenceSession(modelPath, _sessionOptions["default"]);
                            lock (_modelSessions)
                            {
                                _modelSessions[modelName] = session;
                            }
                            _logger.LogInformation($"📦 Loaded AI model: {modelName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"⚠️ Failed to load model: {modelName}");
                    }
                }

                if (_modelSessions.Count == 0)
                {
                    _logger.LogWarning("⚠️ No valid ONNX models loaded. Upscaling will fallback to standard algorithms.");
                }
            });
        }



        /// <summary>
        /// Real hardware detection and optimization
        /// </summary>
        public async Task<HardwareProfile> DetectHardwareAsync()
        {
            // Cache hardware detection for 5 minutes with thread safety
            lock (_hardwareCacheLock)
            {
                if (_hardwareCache.TryGetValue("profile", out var cachedProfile) && 
                    DateTime.Now - _lastHardwareCheck < TimeSpan.FromMinutes(5))
                {
                    return cachedProfile as HardwareProfile ?? new HardwareProfile();
                }
            }

            _logger.LogInformation("🔍 Detecting hardware capabilities...");
            
            var profile = new HardwareProfile();
            
            try
            {
                // 1. GPU Detection via ONNX Runtime
                await DetectGpuCapabilities(profile);
                
                // 2. System Resources
                await DetectSystemResources(profile);
                
                // 3. FFmpeg Hardware Acceleration
                await DetectFFmpegAcceleration(profile);
                
                // 4. OpenCV Acceleration
                await DetectOpenCVAcceleration(profile);
                
                // 5. Apply Hardware Optimizations
                ApplyHardwareOptimizations(profile);
                
                lock (_hardwareCacheLock)
                {
                    _hardwareCache["profile"] = profile;
                    _lastHardwareCheck = DateTime.Now;
                }
                
                _logger.LogInformation($"✅ Hardware Profile: {profile.GpuVendor} {profile.GpuModel}, CUDA: {profile.SupportsCUDA}");
                
                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Hardware detection failed, using fallback profile");
                return GetFallbackProfile();
            }
        }

        /// <summary>
        /// Detect GPU capabilities using ONNX Runtime
        /// </summary>
        private async Task DetectGpuCapabilities(HardwareProfile profile)
        {
            try
            {
                var providers = OrtEnv.Instance().GetAvailableProviders();
                profile.AvailableProviders = providers.ToList();
                
                if (providers.Contains("CUDAExecutionProvider"))
                {
                    profile.SupportsCUDA = true;
                    profile.GpuVendor = "NVIDIA";
                    await DetectNvidiaGpu(profile);
                }
                else if (providers.Contains("DmlExecutionProvider"))
                {
                    profile.SupportsDirectML = true;
                    profile.GpuVendor = "DirectML";
                    await DetectDirectMLGpu(profile);
                }
                else
                {
                    profile.GpuVendor = "CPU";
                    _logger.LogInformation("🖥️ Using CPU inference");
                }
                
                _logger.LogInformation($"🔧 Available providers: {string.Join(", ", providers)}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ GPU detection failed");
            }
        }

        /// <summary>
        /// Detect NVIDIA GPU specifics
        /// </summary>
        private async Task DetectNvidiaGpu(HardwareProfile profile)
        {
            try
            {
                // Try nvidia-smi for detailed info
                var nvidiaSmiPath = FindNvidiaSmi();
                if (!string.IsNullOrEmpty(nvidiaSmiPath))
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = nvidiaSmiPath,
                        Arguments = "--query-gpu=name,driver_version,memory.total --format=csv,noheader,nounits",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(processInfo);
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync();
                        
                        if (!string.IsNullOrEmpty(output))
                        {
                            var parts = output.Trim().Split(',');
                            if (parts.Length >= 3)
                            {
                                profile.GpuModel = parts[0].Trim();
                                profile.DriverVersion = parts[1].Trim();
                                profile.VramMB = int.TryParse(parts[2].Trim(), out var vram) ? vram : 0;
                            }
                        }
                    }
                }
                
                _logger.LogInformation($"🎮 NVIDIA GPU: {profile.GpuModel} ({profile.VramMB}MB VRAM)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ NVIDIA GPU detection failed");
            }
        }

        /// <summary>
        /// Detect DirectML GPU capabilities
        /// </summary>
        private async Task DetectDirectMLGpu(HardwareProfile profile)
        {
            try
            {
                profile.GpuModel = "DirectML Compatible GPU";
                profile.SupportsDirectML = true;
                
                _logger.LogInformation("🎮 DirectML GPU acceleration available");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ DirectML detection failed");
            }
        }

        /// <summary>
        /// Detect system resources
        /// </summary>
        private async Task DetectSystemResources(HardwareProfile profile)
        {
            try
            {
                // RAM Detection
                var totalMemory = GC.GetTotalMemory(false);
                profile.SystemRamMB = (int)(totalMemory / 1024 / 1024);
                
                // CPU Detection
                profile.CpuCores = Environment.ProcessorCount;
                
                // Available disk space
                var tempPath = Path.GetTempPath();
                var root = Path.GetPathRoot(tempPath);
                if (!string.IsNullOrEmpty(root))
                {
                    var driveInfo = new DriveInfo(root);
                    profile.TempDiskSpaceGB = (int)(driveInfo.AvailableFreeSpace / 1024 / 1024 / 1024);
                }
                
                _logger.LogInformation($"💾 System: {profile.SystemRamMB}MB RAM, {profile.CpuCores} CPU cores, {profile.TempDiskSpaceGB}GB temp space");
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ System resource detection failed");
            }
        }

        /// <summary>
        /// Detect FFmpeg hardware acceleration
        /// </summary>
        private async Task DetectFFmpegAcceleration(HardwareProfile profile)
        {
            try
            {
                var ffmpegPath = _mediaEncoder.EncoderPath;
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    _logger.LogWarning("⚠️ FFmpeg path not available");
                    return;
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-hide_banner -hwaccels",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    
                    profile.AvailableHwAccels = output.Split('\n')
                        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.Contains("Hardware"))
                        .Select(line => line.Trim())
                        .ToList();
                }

                _logger.LogInformation($"🎬 FFmpeg HW Accels: {string.Join(", ", profile.AvailableHwAccels)}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ FFmpeg acceleration detection failed");
            }
        }

        /// <summary>
        /// Detect OpenCV acceleration
        /// </summary>
        private async Task DetectOpenCVAcceleration(HardwareProfile profile)
        {
            try
            {
                var buildInfo = Cv2.GetBuildInformation();
                profile.OpenCVInfo = buildInfo;
                
                // Check for CUDA support in OpenCV
                profile.OpenCVSupportsCUDA = buildInfo.Contains("CUDA");
                
                _logger.LogInformation($"🖼️ OpenCV: CUDA={profile.OpenCVSupportsCUDA}");
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ OpenCV acceleration detection failed");
            }
        }

        /// <summary>
        /// Apply hardware-specific optimizations
        /// </summary>
        private void ApplyHardwareOptimizations(HardwareProfile profile)
        {
            // Optimize based on available hardware
            if (profile.SupportsCUDA && profile.VramMB > 4096)
            {
                profile.RecommendedModel = "realesrgan";
                profile.MaxConcurrentStreams = 2;
                profile.RecommendedScale = 4;
            }
            else if (profile.SupportsDirectML)
            {
                profile.RecommendedModel = "esrgan";
                profile.MaxConcurrentStreams = 1;
                profile.RecommendedScale = 2;
            }
            else if (profile.CpuCores >= 8)
            {
                profile.RecommendedModel = "fsrcnn";
                profile.MaxConcurrentStreams = 1;
                profile.RecommendedScale = 2;
            }
            else
            {
                profile.RecommendedModel = "bicubic";
                profile.MaxConcurrentStreams = 1;
                profile.RecommendedScale = 2;
            }
            
            _logger.LogInformation($"🎯 Optimized: {profile.RecommendedModel} @ {profile.RecommendedScale}x, {profile.MaxConcurrentStreams} streams");
        }

        /// <summary>
        /// Find nvidia-smi executable
        /// </summary>
        private string FindNvidiaSmi()
        {
            var possiblePaths = new[]
            {
                @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
                @"C:\Windows\System32\nvidia-smi.exe",
                "/usr/bin/nvidia-smi",
                "/usr/local/cuda/bin/nvidia-smi"
            };

            return possiblePaths.FirstOrDefault(File.Exists) ?? "";
        }

        /// <summary>
        /// Get fallback hardware profile
        /// </summary>
        private HardwareProfile GetFallbackProfile()
        {
            return new HardwareProfile
            {
                GpuVendor = "CPU",
                GpuModel = "Software Fallback",
                CpuCores = Environment.ProcessorCount,
                SystemRamMB = 4096,
                RecommendedModel = "bicubic",
                RecommendedScale = 2,
                MaxConcurrentStreams = 1
            };
        }

        /// <summary>
        /// Upscale image using AI model
        /// </summary>
        public async Task<byte[]> UpscaleImageAsync(byte[] inputImage, string model, int scale = 2)
        {
            try
            {
                if (!_modelSessions.ContainsKey(model) || _modelSessions[model] == null)
                {
                    _logger.LogWarning($"⚠️ Model {model} not available, using fallback");
                    return await FallbackUpscaleAsync(inputImage, scale);
                }

                var session = _modelSessions[model]!;
                
                // Load image with ImageSharp
                using var image = Image.Load(inputImage);
                
                // Prepare input tensor
                var inputTensor = PrepareInputTensor(image);
                
                // Run inference
                var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", inputTensor) };
                
                // Session.Run is not thread-safe for some providers, but ONNX Runtime generally handles it.
                // However, we use a semaphore in VideoProcessor to be safe.
                using var outputs = session.Run(inputs);
                
                // Process output
                var outputTensor = outputs.First().AsEnumerable<float>().ToArray();
                using var outputImage = ProcessOutputTensor(outputTensor, image.Width * scale, image.Height * scale);
                
                // Convert back to byte array
                using var outputStream = new MemoryStream();
                outputImage.SaveAsJpeg(outputStream);
                
                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ AI upscaling failed for model {model}");
                return await FallbackUpscaleAsync(inputImage, scale);
            }
        }

        /// <summary>
        /// Upscale a batch of images for higher throughput
        /// </summary>
        public async Task<List<byte[]>> UpscaleBatchAsync(List<byte[]> inputImages, string model, int scale = 2)
        {
            _logger.LogDebug($"📦 Batch processing {inputImages.Count} images with {model}");
            
            var results = new List<byte[]>();
            
            // For most SR models, we process sequentially or in small parallel groups
            // as GPU memory is the bottleneck.
            foreach (var img in inputImages)
            {
                results.Add(await UpscaleImageAsync(img, model, scale));
            }
            
            return results;
        }

        /// <summary>
        /// Fallback upscaling using traditional methods
        /// </summary>
        private async Task<byte[]> FallbackUpscaleAsync(byte[] inputImage, int scale)
        {
            try
            {
                using var image = Image.Load(inputImage);
                var newWidth = image.Width * scale;
                var newHeight = image.Height * scale;
                
                image.Mutate(x => x.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
                
                using var outputStream = new MemoryStream();
                image.SaveAsJpeg(outputStream);
                
                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Fallback upscaling failed");
                return inputImage; // Return original if all else fails
            }
        }

        /// <summary>
        /// Prepare input tensor for ONNX model
        /// </summary>
        private Microsoft.ML.OnnxRuntime.Tensors.Tensor<float> PrepareInputTensor(Image image)
        {
            var width = image.Width;
            var height = image.Height;
            
            var tensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(new[] { 1, 3, height, width });
            
            // Convert to Rgb24 to ensure standard pixel format
            using var rgbImage = image.CloneAs<SixLabors.ImageSharp.PixelFormats.Rgb24>();
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = rgbImage[x, y];
                    // NCHW format: [Batch, Channel, Height, Width]
                    tensor[0, 0, y, x] = pixel.R / 255.0f;
                    tensor[0, 1, y, x] = pixel.G / 255.0f;
                    tensor[0, 2, y, x] = pixel.B / 255.0f;
                }
            }
            
            return tensor;
        }

        /// <summary>
        /// Process output tensor from ONNX model
        /// </summary>
        private Image ProcessOutputTensor(float[] tensor, int width, int height)
        {
            var outputImage = new Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(width, height);
            int channelSize = width * height;
            int requiredSize = channelSize * 3;
            
            if (tensor.Length < requiredSize)
            {
                throw new InvalidOperationException($"Tensor size mismatch: expected at least {requiredSize} elements, got {tensor.Length}");
            }
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = y * width + x;
                    
                    // Map from NCHW flat array back to pixels
                    // Channel 0 = Red, 1 = Green, 2 = Blue
                    var r = Math.Clamp(tensor[0 * channelSize + pixelIndex] * 255.0f, 0, 255);
                    var g = Math.Clamp(tensor[1 * channelSize + pixelIndex] * 255.0f, 0, 255);
                    var b = Math.Clamp(tensor[2 * channelSize + pixelIndex] * 255.0f, 0, 255);
                    
                    outputImage[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgb24((byte)r, (byte)g, (byte)b);
                }
            }
            
            return outputImage;
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            foreach (var session in _modelSessions.Values)
            {
                session?.Dispose();
            }
            _modelSessions.Clear();
            
            foreach (var option in _sessionOptions.Values)
            {
                option?.Dispose();
            }
            _sessionOptions.Clear();
        }
    }
}