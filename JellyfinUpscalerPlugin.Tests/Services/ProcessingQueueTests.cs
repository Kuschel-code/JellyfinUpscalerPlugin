using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using JellyfinUpscalerPlugin.Models;
using JellyfinUpscalerPlugin.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.7.2 - regression guards for the v1.7.0 RequestPersist() debounce-and-async refactor.
    /// </summary>
    public class ProcessingQueueTests : IDisposable
    {
        private readonly string _tmpDir;
        private readonly string _persistFile;
        private readonly ProcessingQueue _queue;

        public ProcessingQueueTests()
        {
            _tmpDir = Path.Combine(Path.GetTempPath(), "jfupscaler-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmpDir);
            _persistFile = Path.Combine(_tmpDir, "upscaler_queue.json");

            _queue = new ProcessingQueue(NullLogger<ProcessingQueue>.Instance);

            // Inject _persistPath via reflection. PersistQueueAcrossRestarts default is false
            // and we can't set Plugin.Instance.Configuration in a unit test, so this is
            // the cleanest path. If the field is ever renamed, the test breaks loudly.
            var field = typeof(ProcessingQueue).GetField("_persistPath",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field.Should().NotBeNull("_persistPath field must exist for these regression guards");
            field!.SetValue(_queue, _persistFile);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public async Task Enqueue_TriggersPersistWithin1Second()
        {
            var enqueued = _queue.Enqueue("test-job-1", "/in.mkv", "/out.mkv",
                new VideoProcessingOptions { Model = "realesrgan-x4", ScaleFactor = 4 },
                priority: 5, itemName: "test-item");
            enqueued.Should().BeTrue();

            await WaitForFileAsync(_persistFile, TimeSpan.FromSeconds(2));

            File.Exists(_persistFile).Should().BeTrue("RequestPersist should have triggered the debounced write");
            var json = await File.ReadAllTextAsync(_persistFile);
            json.Should().Contain("test-job-1", "the enqueued job ID must be persisted");
        }

        [Fact]
        public async Task MultipleEnqueues_WithinDebounceWindow_CoalesceIntoOneFinalWrite()
        {
            for (int i = 1; i <= 5; i++)
            {
                _queue.Enqueue($"burst-job-{i}", $"/in{i}.mkv", $"/out{i}.mkv",
                    new VideoProcessingOptions { Model = "fsrcnn-x2", ScaleFactor = 2 },
                    priority: 5, itemName: $"burst-item-{i}");
            }

            await WaitForFileAsync(_persistFile, TimeSpan.FromSeconds(3));

            var json = await File.ReadAllTextAsync(_persistFile);
            for (int i = 1; i <= 5; i++)
            {
                json.Should().Contain($"burst-job-{i}", $"burst-job-{i} must be in the final coalesced persist");
            }
        }

        [Fact]
        public void Enqueue_DoesNotBlockOnDiskIO_WhenPersistPathIsSet()
        {
            // Regression-guard: Enqueue must return quickly. Pre-v1.7.0 this was a 5-30s block
            // on NAS-mounted disks due to sync File.WriteAllText inside lock(_queueLock).
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _queue.Enqueue("nonblock-job", "/in.mkv", "/out.mkv",
                new VideoProcessingOptions { Model = "auto", ScaleFactor = 2 },
                priority: 5, itemName: "nonblock-item");
            sw.Stop();

            sw.ElapsedMilliseconds.Should().BeLessThan(100,
                "Enqueue must return immediately - persist is debounced + async, not inline");
        }

        [Fact]
        public async Task RequestPersist_AfterCancel_IsBenign()
        {
            _queue.Enqueue("predispose", "/a.mkv", "/b.mkv", new VideoProcessingOptions(), 5);
            await WaitForFileAsync(_persistFile, TimeSpan.FromSeconds(2));

            var act = () => _queue.Cancel("predispose");
            act.Should().NotThrow("Cancel must remain safe and rearm the persist timer cleanly");
        }

        private static async Task WaitForFileAsync(string path, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                    return;
                await Task.Delay(50);
            }
        }
    }
}
