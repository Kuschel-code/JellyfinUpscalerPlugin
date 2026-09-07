using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpscalerPlugin.Models;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services;

public class HdrFrameContractTests
{
    private static VideoInfo Pq() => new() { IsHDR = true, ColorTransfer = "smpte2084", ColorPrimaries = "bt2020", BitDepth = 10 };
    internal static byte[] Ramp(int scale = 1, PngBitDepth depth = PngBitDepth.Bit16)
    {
        using var image = new Image<Rgb48>(8 * scale, 8 * scale);
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++) image[x, y] = new Rgb48((ushort)(1000 + x * 71), (ushort)(5000 + y * 93), (ushort)(19000 + x * 101));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream, new PngEncoder { BitDepth = depth, ColorType = PngColorType.Rgb });
        return stream.ToArray();
    }

    [Fact]
    public void PqRgb16AndNativeScaleAreAccepted()
    {
        HdrFrameContract.Validate(Pq(), ProcessingMethod.FrameByFrame, "libx265");
        var input = Ramp(); var output = Ramp(4);
        Assert.Equal(4, HdrFrameContract.ValidateOutput(input, output));
        using var decoded = Image.Load<Rgb48>(input);
        Assert.Equal((ushort)1071, decoded[1, 0].R);
        Assert.NotEqual(0, decoded[1, 0].R % 257);
    }

    [Theory]
    [InlineData("arib-std-b67")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("bt709")]
    public void NonPqHdrIsRejected(string transfer)
    {
        var info = Pq(); info.ColorTransfer = transfer;
        Assert.Throws<NotSupportedException>(() => HdrFrameContract.Validate(info, ProcessingMethod.FrameByFrame, "libx265"));
    }

    [Fact]
    public void PrimariesBitDepthAndDynamicHdrAreChecked()
    {
        var info = Pq(); info.ColorPrimaries = "bt709";
        Assert.Throws<NotSupportedException>(() => HdrFrameContract.ValidateInput(info));
        info = Pq(); info.BitDepth = 8;
        Assert.Throws<NotSupportedException>(() => HdrFrameContract.ValidateInput(info));
        info = Pq(); info.HasDynamicHDR = true;
        Assert.Throws<NotSupportedException>(() => HdrFrameContract.ValidateInput(info));
    }

    [Theory]
    [InlineData(ProcessingMethod.MultiFrame)]
    [InlineData(ProcessingMethod.RealTime)]
    [InlineData(ProcessingMethod.RealTimeAI)]
    public async Task UnsupportedMethodsFailBeforeLaunchingAProcess(ProcessingMethod method)
    {
        var executor = new ProcessingMethodExecutor(NullLogger.Instance, "must-not-run", null!, null!, null!, new());
        var job = new ProcessingJob { InputInfo = Pq(), ProcessingMethod = method };
        await Assert.ThrowsAsync<NotSupportedException>(() => executor.ExecuteProcessingAsync("none", "none", job, 1, CancellationToken.None));
    }

    [Theory]
    [InlineData("libx264")]
    [InlineData("copy")]
    [InlineData("hevc_nvenc")]
    public void UnvalidatedEncodersAreRejected(string codec) => Assert.Throws<NotSupportedException>(() =>
        HdrFrameContract.Validate(Pq(), ProcessingMethod.FrameByFrame, codec));

    [Fact]
    public void SdrIsUnaffected() => HdrFrameContract.Validate(new VideoInfo(), ProcessingMethod.RealTime, "libx264");

    [Fact]
    public void EightBitOutputIsRejected() => Assert.Throws<InvalidDataException>(() =>
        HdrFrameContract.ValidateOutput(Ramp(), Ramp(2, PngBitDepth.Bit8)));

    [Fact]
    public void StaticMetadataIsPreservedAndDynamicMetadataIsRejected()
    {
        using var doc = JsonDocument.Parse("""
        {"side_data_list":[{"side_data_type":"Mastering display metadata","red_x":"34000/50000","red_y":"16000/50000","green_x":"13250/50000","green_y":"34500/50000","blue_x":"7500/50000","blue_y":"3000/50000","white_point_x":"15635/50000","white_point_y":"16450/50000","min_luminance":"50/10000","max_luminance":"10000000/10000"},{"side_data_type":"Content light level metadata","max_content":1000,"max_average":400}]}
        """);
        var info = Pq(); HdrFrameContract.ReadMetadata(doc.RootElement, info);
        var args = HdrFrameContract.X265Parameters(info);
        Assert.Contains("master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,50)", args);
        Assert.Contains("max-cll=1000,400", args);
        using var dynamicDoc = JsonDocument.Parse("{\"side_data_type\":\"HDR Dynamic Metadata SMPTE2094-40\"}");
        HdrFrameContract.ReadMetadata(dynamicDoc.RootElement, info);
        Assert.Throws<NotSupportedException>(() => HdrFrameContract.ValidateInput(info));
    }

    private sealed class ResponseHandler(byte[] result, HttpStatusCode status) : HttpMessageHandler
    {
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new ByteArrayContent(result) };
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServiceContractUsesPqAndNeverCopiesAnOriginalAfterHdrFailure(bool succeeds)
    {
        var root = Path.Combine(Path.GetTempPath(), "hdr-contract-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var frames = Path.Combine(root, "out"); Directory.CreateDirectory(frames);
            var input = Path.Combine(root, "frame.png"); await File.WriteAllBytesAsync(input, Ramp());
            var handler = new ResponseHandler(Ramp(4), succeeds ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
            using var client = new HttpClient(handler);
            var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            var processor = new VideoFrameProcessor(NullLogger.Instance, "not-used", null!, null!, factory.Object, new ConcurrentDictionary<string, bool>());
            var action = () => processor.UpscaleSingleFrameAsync(input, frames, new VideoProcessingOptions { ScaleFactor = 2 }, true, CancellationToken.None);
            if (succeeds)
            {
                Assert.True(await action());
                Assert.Equal(4, HdrFrameContract.ValidateOutput(await File.ReadAllBytesAsync(input), await File.ReadAllBytesAsync(Path.Combine(frames, "frame.png"))));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(action);
                Assert.False(File.Exists(Path.Combine(frames, "frame.png")));
            }
            Assert.Contains("smpte2084", handler.Body);
            Assert.Contains("transfer", handler.Body);
        }
        finally { Directory.Delete(root, true); }
    }
}
