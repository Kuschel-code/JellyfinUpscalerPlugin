using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.8.3.35 - adds the player script tag to jellyfin-web's index.html while Jellyfin serves it,
    /// so the player button no longer depends on write access to the web folder (#75). Jellyfin's
    /// Windows installer runs the server as NetworkService under Program Files, the Debian package
    /// keeps the folder owned by root, and some containers mount it read-only: in all of them the
    /// file edit in <see cref="Plugin"/> fails and the button existed only in a tab that had opened
    /// the plugin's settings. The file on disk is not touched here, and every other request passes
    /// straight through.
    /// </summary>
    public sealed class PlayerScriptMiddleware
    {
        private const int MaxPageBytes = 1024 * 1024;

        private readonly RequestDelegate _next;
        private readonly ILogger<PlayerScriptMiddleware> _logger;
        private readonly string _tag;
        private int _announced;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlayerScriptMiddleware"/> class.
        /// </summary>
        public PlayerScriptMiddleware(RequestDelegate next, ILogger<PlayerScriptMiddleware> logger)
        {
            _next = next;
            _logger = logger;
            _tag = PlayerScriptTag.For(typeof(Plugin).Assembly.GetName().Version);
        }

        /// <summary>
        /// The web client's page: {BaseUrl}/web/ or {BaseUrl}/web/index.html. This runs ahead of
        /// Jellyfin's BaseUrl mapping, so the base URL is still part of the path.
        /// </summary>
        internal static bool IsIndexRequest(HttpRequest request)
        {
            if (!HttpMethods.IsGet(request.Method))
            {
                return false;
            }

            var path = request.PathBase.Add(request.Path).Value ?? string.Empty;
            return path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Serves the page with the tag, or unchanged when it is not the HTML page expected.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            if (!IsIndexRequest(context.Request))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            // Ask for a full, uncompressed answer: compressed bytes cannot be edited, and a 304
            // for the unchanged file would keep a page that carries an older release's tag.
            var headers = context.Request.Headers;
            headers.Remove(HeaderNames.AcceptEncoding);
            headers.Remove(HeaderNames.IfNoneMatch);
            headers.Remove(HeaderNames.IfModifiedSince);
            headers.Remove(HeaderNames.IfRange);
            headers.Remove(HeaderNames.Range);

            var original = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await _next(context).ConfigureAwait(false);
            }
            finally
            {
                context.Response.Body = original;
            }

            var page = buffer.ToArray();
            byte[]? edited = null;
            if (!context.Response.HasStarted)
            {
                try
                {
                    edited = Edit(context.Response, page);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI Upscaler: Could not add the player script to index.html; serving it unchanged");
                }
            }

            if (edited is not null)
            {
                // The file's validators describe the file, not this page.
                context.Response.Headers.Remove(HeaderNames.ETag);
                context.Response.Headers.Remove(HeaderNames.LastModified);
                context.Response.ContentLength = edited.Length;
                page = edited;
                if (Interlocked.Exchange(ref _announced, 1) == 0)
                {
                    _logger.LogInformation("AI Upscaler: Player script added to index.html as Jellyfin serves it");
                }
            }

            await original.WriteAsync(page, context.RequestAborted).ConfigureAwait(false);
        }

        private byte[]? Edit(HttpResponse response, byte[] page)
        {
            if (response.StatusCode != StatusCodes.Status200OK || page.Length == 0 || page.Length > MaxPageBytes
                || response.Headers.ContainsKey(HeaderNames.ContentEncoding)
                || response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) != true)
            {
                return null;
            }

            // jellyfin-web's index.html starts with a byte order mark; it survives the round trip.
            var html = Encoding.UTF8.GetString(page);
            if (!PlayerScriptTag.TryInject(html, _tag, out var result) || ReferenceEquals(result, html))
            {
                return null;
            }

            return Encoding.UTF8.GetBytes(result);
        }
    }

    /// <summary>
    /// Puts <see cref="PlayerScriptMiddleware"/> in front of Jellyfin's request pipeline. Jellyfin
    /// builds its web host from the service collection that plugins register into
    /// (ApplicationHost.Init), so startup filters registered by a plugin are applied.
    /// </summary>
    public sealed class PlayerScriptStartupFilter : IStartupFilter
    {
        /// <inheritdoc />
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseMiddleware<PlayerScriptMiddleware>();
                next(app);
            };
        }
    }
}
