using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// Selects the optimal processing strategy based on hardware capabilities, input properties, and configuration.
    /// </summary>
    public class ProcessingStrategySelector
    {
        private readonly ILogger _logger;

        private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        // Processing constants
        private const double ProgressStartingPercent = 5.0;
        private const double ProgressMaxPercent = 95.0;
        private const double ProgressDefaultPercent = 10.0;
        private const double EstimatedProcessingSpeedRatio = 0.5;
        private const double EstimatedTotalFallbackSeconds = 60.0;

        public ProcessingStrategySelector(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Optimize processing options based on hardware and input
        /// </summary>
        public VideoProcessingOptions OptimizeProcessingOptions(
            VideoProcessingOptions options,
            VideoInfo inputInfo,
            HardwareProfile hardwareProfile)
        {
            var optimized = new VideoProcessingOptions(options);

            // Auto-select model based on hardware
            if (string.IsNullOrEmpty(optimized.Model) || optimized.Model == "auto")
            {
                optimized.Model = hardwareProfile.RecommendedModel;
            }

            // Auto-select scale based on input resolution
            if (optimized.ScaleFactor == 0)
            {
                optimized.ScaleFactor = inputInfo.Width <= 720 ? 3 : 2;
            }

            // Normalize "low" quality to "fast" (UI offers "low" but backend uses "fast")
            if (string.Equals(optimized.QualityLevel, "low", StringComparison.OrdinalIgnoreCase))
            {
                optimized.QualityLevel = "fast";
            }

            // Only auto-select quality if not explicitly configured by user
            if (string.IsNullOrEmpty(optimized.QualityLevel) || optimized.QualityLevel == "auto")
            {
                if (hardwareProfile.SupportsCUDA && hardwareProfile.VramMB > 8192)
                    optimized.QualityLevel = "high";
                else if (hardwareProfile.SupportsDirectML)
                    optimized.QualityLevel = "medium";
                else
                    optimized.QualityLevel = "fast";
            }

            // Enable hardware acceleration if available
            if (hardwareProfile.SupportsCUDA)
            {
                optimized.HardwareAcceleration = "cuda";
            }
            else if (hardwareProfile.AvailableHwAccels.Contains("vaapi"))
            {
                optimized.HardwareAcceleration = "vaapi";
            }
            else if (hardwareProfile.AvailableHwAccels.Contains("qsv"))
            {
                optimized.HardwareAcceleration = "qsv";
            }
            else if (hardwareProfile.SupportsDirectML)
            {
                optimized.HardwareAcceleration = "directml";
            }

            _logger.LogInformation("Optimized options: {Model} @ {Scale}x, {Quality} quality, {Accel} accel",
                optimized.Model, optimized.ScaleFactor, optimized.QualityLevel, optimized.HardwareAcceleration);

            return optimized;
        }

        /// <summary>
        /// Build a model fallback chain for video processing.
        /// Primary model first, then configured fallbacks from ModelFallbackChain config.
        /// </summary>
        public List<string> BuildVideoModelChain(string primaryModel)
        {
            var chain = new List<string> { primaryModel };
            var fallbackConfig = Config.ModelFallbackChain;
            if (!string.IsNullOrWhiteSpace(fallbackConfig))
            {
                var fallbacks = fallbackConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var fb in fallbacks)
                {
                    if (!chain.Exists(x => string.Equals(x, fb, StringComparison.OrdinalIgnoreCase)))
                        chain.Add(fb);
                }
            }
            return chain;
        }

        /// <summary>
        /// Determine the best processing method
        /// </summary>
        public ProcessingMethod DetermineProcessingMethod(
            VideoInfo inputInfo,
            HardwareProfile hardwareProfile,
            VideoProcessingOptions options,
            int inputFrames = 1)
        {
            // Multi-frame VSR takes priority when model supports it
            if (options.EnableAIUpscaling && inputFrames > 1)
            {
                _logger.LogInformation("Multi-frame model detected (input_frames={Frames}), using MultiFrame processing", inputFrames);
                return ProcessingMethod.MultiFrame;
            }

            // Real-time AI upscaling: pipe-based decode -> upscale -> encode
            if (options.EnableAIUpscaling && IsRealTimeAIFeasible(inputInfo, hardwareProfile, options))
            {
                _logger.LogInformation("Real-time AI upscaling selected for {Width}x{Height} @ {Fps}fps with model {Model}",
                    inputInfo.Width, inputInfo.Height, inputInfo.FrameRate, options.Model);
                return ProcessingMethod.RealTimeAI;
            }

            // Real-time processing for short videos or live streams
            if (!options.EnableAIUpscaling && (inputInfo.Duration.TotalMinutes < 5 || options.EnableRealTimeProcessing))
            {
                return ProcessingMethod.RealTime;
            }

            // Frame-by-frame for high quality
            if (options.QualityLevel == "high" && hardwareProfile.SupportsCUDA)
            {
                return ProcessingMethod.FrameByFrame;
            }

            // Batch processing for efficiency
            return ProcessingMethod.Batch;
        }

        /// <summary>
        /// Check if real-time AI upscaling is feasible for the given input.
        /// </summary>
        public bool IsRealTimeAIFeasible(VideoInfo inputInfo, HardwareProfile hardwareProfile, VideoProcessingOptions options)
        {
            // Only enable for explicit real-time request
            if (!options.EnableRealTimeProcessing)
            {
                return false;
            }

            // Input resolution must be <= 1080p
            if (inputInfo.Width > 1920 || inputInfo.Height > 1080)
            {
                _logger.LogDebug("RealTimeAI skipped: input resolution {Width}x{Height} exceeds 1080p", inputInfo.Width, inputInfo.Height);
                return false;
            }

            // Only fast models are suitable for real-time
            var fastModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "span-x2", "span-x4",
                "clearreality-x4",
                "fsrcnn-x2", "fsrcnn-x3", "fsrcnn-x4",
                "espcn-x2", "espcn-x3", "espcn-x4"
            };

            var modelName = options.Model?.ToLowerInvariant() ?? "";
            if (!fastModels.Contains(modelName) && !modelName.Contains("fsrcnn") && !modelName.Contains("espcn") && !modelName.Contains("span"))
            {
                _logger.LogDebug("RealTimeAI skipped: model '{Model}' is not in the fast model list", options.Model);
                return false;
            }

            // Check benchmark FPS if available (must be >= 80% of target FPS)
            var targetFps = inputInfo.FrameRate > 0 ? inputInfo.FrameRate : 30.0;
            var benchmarkFps = hardwareProfile.BenchmarkFps;
            if (benchmarkFps > 0 && benchmarkFps < targetFps * 0.8)
            {
                _logger.LogDebug("RealTimeAI skipped: benchmark FPS {BenchFps:F1} < {Threshold:F1} (80% of target {TargetFps:F1})",
                    benchmarkFps, targetFps * 0.8, targetFps);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Calculate job progress percentage
        /// </summary>
        public double CalculateJobProgress(ProcessingJob job)
        {
            if (job.Status == ProcessingStatus.Completed)
                return 100.0;
            if (job.Status == ProcessingStatus.Cancelled || job.Status == ProcessingStatus.Failed)
                return 0;
            if (job.Status == ProcessingStatus.Starting || job.Status == ProcessingStatus.Analyzing)
                return ProgressStartingPercent;

            if (job.Status == ProcessingStatus.Processing && job.InputInfo != null && job.InputInfo.Duration.TotalSeconds > 0)
            {
                var elapsed = (DateTime.UtcNow - job.StartTime).TotalSeconds;
                var estimatedTotal = job.InputInfo.Duration.TotalSeconds * EstimatedProcessingSpeedRatio;
                if (estimatedTotal <= 0) estimatedTotal = EstimatedTotalFallbackSeconds;
                return Math.Min(ProgressMaxPercent, (elapsed / estimatedTotal) * 100);
            }

            return ProgressDefaultPercent;
        }
    }
}
