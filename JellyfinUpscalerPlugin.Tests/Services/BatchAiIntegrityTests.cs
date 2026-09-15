using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpscalerPlugin.Models;
using JellyfinUpscalerPlugin.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services;

public sealed class BatchAiIntegrityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "batch-ai-integrity-" + Guid.NewGuid());
    private string Frames => Path.Combine(_root, "frames");
    private string Output => Path.Combine(_root, "output");

    public BatchAiIntegrityTests()
    {
        Directory.CreateDirectory(Frames);
        Directory.CreateDirectory(Output);
    }

    private static byte[] ImageBytes()
    {
        using var image = new Image<Rgb24>(2, 2);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private async Task AddFrames(int count)
    {
        for (var i = 1; i <= count; i++)
            await File.WriteAllBytesAsync(Path.Combine(Frames, $"frame_{i:D6}.png"), ImageBytes());
    }

    private static VideoFrameProcessor Processor(Mock<IUpscalerCore> core, int concurrency = 1)
    {
        core.Setup(c => c.DetectHardwareAsync()).ReturnsAsync(new HardwareProfile { MaxConcurrentStreams = concurrency });
        var hub = new UpscalerProgressHub(NullLogger<UpscalerProgressHub>.Instance, new Mock<ISessionManager>().Object);
        return new VideoFrameProcessor(NullLogger.Instance, "never-executed", core.Object, hub,
            new Mock<IHttpClientFactory>().Object, new ConcurrentDictionary<string, bool>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailedResultOnlyComputesLocalResizeWhenAllowed(bool allowLocalFallback)
    {
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("http://localhost:5000/status")
            .Respond("application/json", "{\"current_model\":\"realesrgan-x4\"}");
        mockHttp.When("http://localhost:5000/upscale")
            .Respond(HttpStatusCode.Unauthorized, "application/json", "{\"detail\":\"API key required\"}");
        using var client = mockHttp.ToHttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        using var service = new HttpUpscalerService(NullLogger<HttpUpscalerService>.Instance, factory.Object);
        using var core = new UpscalerCore(NullLogger<UpscalerCore>.Instance, new Mock<IMediaEncoder>().Object,
            new Mock<IFileSystem>().Object, new Mock<IApplicationPaths>().Object, service);

        var result = await core.UpscaleImageDetailedAsync(ImageBytes(), "realesrgan-x4", 2,
            CancellationToken.None, allowLocalFallback);

        Assert.False(result.UsedAi);
        Assert.Contains("401", result.FallbackReason);
        Assert.Contains("API key required", result.FallbackReason);
        if (allowLocalFallback)
        {
            using var resized = Image.Load(result.Data);
            Assert.Equal(4, resized.Width);
        }
        else
        {
            Assert.Empty(result.Data);
        }
    }

    [Fact]
    public async Task SingleWorkerStopsAtFirstServiceFailureWithoutCopyingFrames()
    {
        await AddFrames(30);
        var calls = 0;
        var core = new Mock<IUpscalerCore>();
        core.Setup(c => c.UpscaleImageDetailedAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>(), false))
            .Returns(() =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new ImageUpscaleResult(Array.Empty<byte>(), false, "Docker unavailable"));
            });

        var error = await Assert.ThrowsAsync<AiUpscalingUnavailableException>(() => Processor(core)
            .ProcessFramesAsync(Frames, Output, new VideoProcessingOptions(), "fail-first", CancellationToken.None));

        Assert.Contains("Docker unavailable", error.Message);
        Assert.Equal(1, calls);
        Assert.Empty(Directory.GetFiles(Output));
    }

    [Fact]
    public async Task ServiceFailureCancelsAndJoinsOtherInFlightFrames()
    {
        await AddFrames(30);
        var calls = 0;
        var cancelledWorkerFinished = false;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var core = new Mock<IUpscalerCore>();
        core.Setup(c => c.UpscaleImageDetailedAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>(), false))
            .Returns(async (byte[] _, string _, int _, CancellationToken token, bool _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    await bothStarted.Task.WaitAsync(token);
                    return new ImageUpscaleResult(Array.Empty<byte>(), false, "Docker unavailable");
                }
                bothStarted.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { cancelledWorkerFinished = true; }
                return new ImageUpscaleResult(ImageBytes(), true, null);
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<AiUpscalingUnavailableException>(() => Processor(core, 2)
            .ProcessFramesAsync(Frames, Output, new VideoProcessingOptions(), "cancel-workers", timeout.Token));

        Assert.True(cancelledWorkerFinished);
        Assert.Equal(2, calls);
        Assert.Empty(Directory.GetFiles(Output));
    }

    [Fact]
    public async Task EmptyAiOutputAbortsBeforeAnyOriginalCanBeCopied()
    {
        await AddFrames(10);
        var core = new Mock<IUpscalerCore>();
        core.Setup(c => c.UpscaleImageDetailedAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(new ImageUpscaleResult(Array.Empty<byte>(), true, null));

        await Assert.ThrowsAsync<InvalidDataException>(() => Processor(core)
            .ProcessFramesAsync(Frames, Output, new VideoProcessingOptions(), "empty-result", CancellationToken.None));

        Assert.Empty(Directory.GetFiles(Output));
        core.Verify(c => c.UpscaleImageDetailedAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>(), false), Times.Once);
    }

    [Fact]
    public async Task SuccessfulFramesAreAllWrittenAndReported()
    {
        await AddFrames(5);
        var core = new Mock<IUpscalerCore>();
        core.Setup(c => c.UpscaleImageDetailedAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(new ImageUpscaleResult(ImageBytes(), true, null));
        var jobId = Guid.NewGuid().ToString();
        try
        {
            await Processor(core, 2).ProcessFramesAsync(Frames, Output, new VideoProcessingOptions(), jobId, CancellationToken.None);
            Assert.Equal(5, Directory.GetFiles(Output).Length);
            Assert.Equal(100d, UpscalerProgressHub.GetFrameProgress(jobId));
        }
        finally { UpscalerProgressHub.ClearFrameProgress(jobId); }
    }

    [Fact]
    public async Task EmptyExtractionFailsBeforeCallingDocker()
    {
        var core = new Mock<IUpscalerCore>(MockBehavior.Strict);
        await Assert.ThrowsAsync<InvalidDataException>(() => Processor(core)
            .ProcessFramesAsync(Frames, Output, new VideoProcessingOptions(), "no-frames", CancellationToken.None));
        core.Verify(c => c.DetectHardwareAsync(), Times.Never);
    }

    [Fact]
    public async Task CancelledCoreRequestNeverProducesFallbackBytes()
    {
        using var service = new HttpUpscalerService(NullLogger<HttpUpscalerService>.Instance,
            new Mock<IHttpClientFactory>(MockBehavior.Strict).Object);
        using var core = new UpscalerCore(NullLogger<UpscalerCore>.Instance, new Mock<IMediaEncoder>().Object,
            new Mock<IFileSystem>().Object, new Mock<IApplicationPaths>().Object, service);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => core.UpscaleImageDetailedAsync(
            ImageBytes(), "realesrgan-x4", 2, cancelled.Token));
    }

    [Fact]
    public void RealtimeOutputWithWrongDimensionsIsRejectedInsteadOfLocallyResized()
    {
        Assert.Null(VideoFrameProcessor.DecodeJpegToRawFrame(ImageBytes(), 4, 4));
        Assert.Equal(12, VideoFrameProcessor.DecodeJpegToRawFrame(ImageBytes(), 2, 2)!.Length);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
