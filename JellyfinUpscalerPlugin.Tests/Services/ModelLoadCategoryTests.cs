using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpscalerPlugin.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services;

public sealed class ModelLoadCategoryTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"loaded\":true}") };
        }
    }

    [Theory]
    [InlineData("rife-v4.9", false)]
    [InlineData("tiny-yolov3", false)]
    [InlineData("gfpgan-v1.4", false)]
    [InlineData("edvr-m-x4", false)]
    [InlineData("span-x2", true)]
    [InlineData("my-import-x4", true)]
    public async Task NormalModelLoadRejectsOtherPipelinesAndUnavailableEntries(string modelId, bool allowed)
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        var controller = new UpscalerController(NullLogger<UpscalerController>.Instance,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, factory.Object, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.QueryString = new QueryString("?model_name=" + Uri.EscapeDataString(modelId));

        var result = await controller.LoadModel();

        if (allowed)
        {
            Assert.Equal(200, Assert.IsType<ContentResult>(result).StatusCode);
            Assert.Equal(1, handler.Calls);
            Assert.Contains(modelId, handler.Body);
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, handler.Calls);
        }
    }
}
