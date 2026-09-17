using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpscalerPlugin.Services;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services;

public sealed class ModelDownloadTests
{
    private sealed class Source : Stream
    {
        public int Reads;
        public bool Disposed;
        public int Chunks = 2;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Reads++;
            if (Reads > Chunks) return ValueTask.FromResult(0);
            buffer.Span[..8].Fill(42);
            return ValueTask.FromResult(8);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Handler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    [Fact]
    public async Task ChunkedDownloadStopsReadingAsSoonAsItExceedsLimit()
    {
        var source = new Source { Chunks = 10 };
        using var client = new HttpClient(new Handler(new StreamContent(source)));
        await Assert.ThrowsAsync<InvalidDataException>(() => ModelDownload.FetchAsync(client, "https://example.test/model", 8, default));
        Assert.Equal(2, source.Reads);
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task ExactLimitIsAcceptedAndDeclaredOversizeIsRejectedBeforeReading()
    {
        var source = new Source { Chunks = 1 };
        using var client = new HttpClient(new Handler(new StreamContent(source)));
        var result = await ModelDownload.FetchAsync(client, "https://example.test/model", 8, default);
        Assert.Equal(new byte[] {42, 42, 42, 42, 42, 42, 42, 42}, result);
        Assert.True(source.Disposed);

        var oversized = new Source();
        var content = new StreamContent(oversized);
        content.Headers.ContentLength = 9;
        using var other = new HttpClient(new Handler(content));
        await Assert.ThrowsAsync<InvalidDataException>(() => ModelDownload.FetchAsync(other, "https://example.test/model", 8, default));
        Assert.Equal(0, oversized.Reads);
        Assert.True(oversized.Disposed);
    }
}
