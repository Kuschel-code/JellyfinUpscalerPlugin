using System.Threading;
using System.Threading.Tasks;
using JellyfinUpscalerPlugin.Models;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.7.3.1 - Minimal interface over UpscalerCore for the test seam.
    /// Exposes only the methods VideoFrameProcessor consumes so unit tests can mock
    /// without reimplementing the whole 615-LoC UpscalerCore class.
    /// </summary>
    public interface IUpscalerCore
    {
        Task<byte[]> UpscaleImageAsync(
            byte[] imageData,
            string model = "auto",
            int scale = 2,
            CancellationToken cancellationToken = default);

        Task<HardwareProfile> DetectHardwareAsync();
    }
}
