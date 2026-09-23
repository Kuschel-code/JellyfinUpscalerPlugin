using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpscalerPlugin.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services;

public class RealtimeFrameProxyTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string? RetryAfter;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var response = new HttpResponseMessage(Status) { Content = new StringContent("{\"detail\":\"model warming up\"}") };
            if (RetryAfter != null) response.Headers.TryAddWithoutValidation("Retry-After", RetryAfter);
            return Task.FromResult(response);
        }
    }

    private static UpscalerController Controller(HttpClient client, string user)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return new UpscalerController(NullLogger<UpscalerController>.Instance,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, factory.Object, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user) }, "test"))
                }
            }
        };
    }

    private static Task<ActionResult> Frame(UpscalerController controller, bool chunk)
    {
        controller.Request.Body = new MemoryStream(new byte[] { 1, 2, 3 });
        controller.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
            new FormFileCollection { new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "frame_0", "frame.png") });
        return chunk ? controller.UpscaleVideoChunk() : controller.UpscaleFrame();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Playback_frames_do_not_exhaust_the_image_budget(bool chunk)
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var controller = Controller(client, Guid.NewGuid().ToString());
        for (var frame = 1; frame <= 24; frame++)
        {
            var result = await Frame(controller, chunk);
            Assert.True(result is FileContentResult, $"Playback frame {frame} was rejected: {JsonSerializer.Serialize(result)}");
        }
        Assert.Equal(24, handler.Calls);
        // Invalid image actions still exercise the real limiter before argument validation.
        for (var action = 1; action <= 10; action++)
            Assert.IsType<BadRequestObjectResult>(await controller.UpscaleImage(scale: 0));
        Assert.Equal(429, Assert.IsType<ObjectResult>(await controller.UpscaleImage(scale: 0)).StatusCode);
        Assert.IsType<FileContentResult>(await Frame(controller, chunk));
    }

    [Theory]
    [InlineData(false, 429, "2")]
    [InlineData(false, 503, "Wed, 09 Sep 2026 10:00:00 GMT")]
    [InlineData(true, 429, "2")]
    [InlineData(true, 503, "Wed, 09 Sep 2026 10:00:00 GMT")]
    public async Task Backpressure_preserves_status_detail_retry_and_recovers(bool chunk, int status, string retry)
    {
        var handler = new Handler { Status = (HttpStatusCode)status, RetryAfter = retry };
        using var client = new HttpClient(handler);
        var controller = Controller(client, Guid.NewGuid().ToString());
        var result = Assert.IsType<ObjectResult>(await Frame(controller, chunk));
        Assert.Equal(status, result.StatusCode);
        Assert.Contains("model warming up", JsonSerializer.Serialize(result.Value));
        Assert.Equal(retry, controller.Response.Headers.RetryAfter.ToString());
        handler.Status = HttpStatusCode.OK;
        Assert.IsType<FileContentResult>(await Frame(controller, chunk));
    }
}
