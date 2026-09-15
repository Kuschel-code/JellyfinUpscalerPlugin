using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpscalerPlugin.Models;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services;

public sealed class AiFrameOutputTests
{
    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(31, 67, 103));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    public void NativeOutputIsDerivedFromDecodedPixelsWithoutResizing(int scale)
    {
        var output = VideoFrameProcessor.DecodeNativeAiFrame(Png(3 * scale, 2 * scale), 3, 2);
        Assert.Equal(scale, output.Scale);
        Assert.Equal(3 * scale, output.Width);
        Assert.Equal(2 * scale, output.Height);
        Assert.Equal(3 * 2 * scale * scale * 3, output.Data.Length);
        for (var i = 0; i < output.Data.Length; i += 3)
            Assert.Equal(new byte[] { 31, 67, 103 }, output.Data.AsSpan(i, 3).ToArray());
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(6, 5)]
    [InlineData(1, 1)]
    [InlineData(27, 18)]
    public void InvalidScaleOrAspectRatioIsRejected(int width, int height)
    {
        Assert.Throws<InvalidDataException>(() => VideoFrameProcessor.DecodeNativeAiFrame(Png(width, height), 3, 2));
    }

    [Fact]
    public void NonImageSuccessBodyIsRejected()
    {
        Assert.Throws<UnknownImageFormatException>(() => VideoFrameProcessor.DecodeNativeAiFrame(
            System.Text.Encoding.UTF8.GetBytes("{\"detail\":\"not an image\"}"), 3, 2));
    }

    [Fact]
    public async Task CorruptNonemptyAiResponseIsNeverWrittenAsAProcessedFrame()
    {
        var directory = Path.Combine(Path.GetTempPath(), "corrupt-ai-frame-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.png");
            var output = Path.Combine(directory, "output");
            Directory.CreateDirectory(output);
            await File.WriteAllBytesAsync(source, Png(3, 2));
            var core = new Mock<IUpscalerCore>();
            core.Setup(c => c.UpscaleImageDetailedAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>(), false))
                .ReturnsAsync(new ImageUpscaleResult(new byte[] { 1, 2, 3 }, true, null));
            var processor = new VideoFrameProcessor(NullLogger.Instance, "never-executed", core.Object, null!, null!, new());
            await Assert.ThrowsAsync<UnknownImageFormatException>(() => processor.UpscaleSingleFrameAsync(
                source, output, new VideoProcessingOptions(), false, CancellationToken.None));
            Assert.Empty(Directory.GetFiles(output));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteOrMixedSizeSequenceIsRejectedBeforeEncoding(bool mixedSize)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ai-sequence-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "frame_000001.png"), Png(6, 4));
            File.WriteAllBytes(Path.Combine(directory, mixedSize ? "frame_000002.png" : "frame_000003.png"),
                mixedSize ? Png(12, 8) : Png(6, 4));
            Assert.Throws<InvalidDataException>(() => VideoFrameProcessor.ValidateFrameSequence(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CompleteSequenceIsAccepted()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ai-sequence-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            for (var i = 1; i <= 3; i++) File.WriteAllBytes(Path.Combine(directory, $"frame_{i:D6}.png"), Png(6, 4));
            VideoFrameProcessor.ValidateFrameSequence(directory);
        }
        finally { Directory.Delete(directory, true); }
    }
}
