using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    public class MediaProcessingService
    {
        private readonly ILogger<MediaProcessingService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly VideoProcessor _videoProcessor;
        private readonly UpscalerCore _upscalerCore;

        public MediaProcessingService(
            ILogger<MediaProcessingService> logger,
            ILibraryManager libraryManager,
            VideoProcessor videoProcessor,
            UpscalerCore upscalerCore)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _videoProcessor = videoProcessor;
            _upscalerCore = upscalerCore;
        }

        public async Task<VideoProcessingResult> ProcessLibraryItemAsync(string itemId, string? model = null, int? scale = null)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                throw new ArgumentException("Item not found");
            }

            var config = Plugin.Instance?.Configuration;
            var options = new VideoProcessingOptions
            {
                Model = model ?? config?.Model ?? "auto",
                ScaleFactor = scale ?? config?.ScaleFactor ?? 2,
                QualityLevel = config?.QualityLevel ?? "medium"
            };

            var outputPath = Path.Combine(
                Path.GetDirectoryName(item.Path) ?? "",
                Path.GetFileNameWithoutExtension(item.Path) + "_upscaled" + Path.GetExtension(item.Path)
            );

            var result = await _videoProcessor.ProcessVideoAsync(item.Path, outputPath, options);

            if (result.Success)
            {
                await TagItemAsUpscaled(item);
            }

            return result;
        }

        public async Task TagItemAsUpscaled(BaseItem item)
        {
            var tags = item.Tags.ToList();
            if (!tags.Contains("AI-Upscaled"))
            {
                tags.Add("AI-Upscaled");
                item.Tags = tags.ToArray();
                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None);
            }
        }

        public async Task<object> GenerateComparisonDataAsync(string itemId, string model, int scale)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                throw new ArgumentException("Item not found");
            }

            var imagePath = item.GetImagePath(ImageType.Primary, 0);
            if (string.IsNullOrEmpty(imagePath))
            {
                var images = item.GetImages(ImageType.Primary).ToList();
                if (images.Count == 0)
                {
                    throw new InvalidOperationException("No image available for this item");
                }
                imagePath = images[0].Path;
            }

            if (!File.Exists(imagePath))
            {
                throw new FileNotFoundException("Image file not found on disk");
            }

            byte[] originalData = await File.ReadAllBytesAsync(imagePath);
            
            // Optimize memory: Resize large images before upscaling for preview
            using (var image = Image.Load(originalData))
            {
                if (image.Width > 1280 || image.Height > 720)
                {
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(1280, 720),
                        Mode = ResizeMode.Max
                    }));
                    
                    using var ms = new MemoryStream();
                    image.SaveAsJpeg(ms);
                    originalData = ms.ToArray();
                }
            }

            var upscaledData = await _upscalerCore.UpscaleImageAsync(originalData, model, scale);

            return new
            {
                itemId = itemId,
                model = model,
                scale = scale,
                originalBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(originalData)}",
                upscaledBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(upscaledData)}",
                timestamp = DateTime.UtcNow
            };
        }

        public async Task<byte[]> UpscaleImageAsync(byte[] data, string model, int scale)
        {
            return await _upscalerCore.UpscaleImageAsync(data, model, scale);
        }

        public async Task<bool> PreProcessVideoAsync(string inputPath, string? model, int? scale, string? quality, CacheManager cacheManager)
        {
            return await cacheManager.PreProcessContentAsync(
                inputPath,
                model ?? "auto",
                scale ?? 2,
                quality ?? "medium",
                _videoProcessor);
        }
    }
}
