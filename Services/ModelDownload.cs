using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JellyfinUpscalerPlugin.Services;

internal static class ModelDownload
{
    internal static async Task<byte[]> FetchAsync(HttpClient client, string url, long maxBytes, CancellationToken token)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException("Source reports a file above the model import limit.");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        using var destination = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, token)) != 0)
        {
            if (destination.Length + read > maxBytes)
                throw new InvalidDataException("Downloaded file exceeds the model import limit.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }
}
