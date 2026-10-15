using System.Linq;
using System.Reflection;
using FluentAssertions;
using JellyfinUpscalerPlugin.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// Endpoints only the admin dashboard calls. They carried just the controller-wide
    /// [Authorize], so any signed-in user could list every library with its server paths,
    /// read the queue, queue GPU work, and cancel or reprioritise other people's jobs,
    /// while pause and resume next to them were already admin-only.
    /// </summary>
    public class AdminOnlyEndpointTests
    {
        [Theory]
        [InlineData(nameof(UpscalerController.GetLibraries))]
        [InlineData(nameof(UpscalerController.GetQueueStatus))]
        [InlineData(nameof(UpscalerController.EnqueueJob))]
        [InlineData(nameof(UpscalerController.CancelQueuedJob))]
        [InlineData(nameof(UpscalerController.SetJobPriority))]
        [InlineData(nameof(UpscalerController.PauseQueue))]
        [InlineData(nameof(UpscalerController.ResumeQueue))]
        [InlineData(nameof(UpscalerController.GetPreUpscaledVideos))]
        public void Requires_an_administrator(string action)
        {
            var method = typeof(UpscalerController).GetMethods().Single(m => m.Name == action);
            method.GetCustomAttributes<AuthorizeAttribute>().Should().Contain(a => a.Policy == "RequiresElevation",
                $"{action} is called only by the admin dashboard");
        }
    }
}
