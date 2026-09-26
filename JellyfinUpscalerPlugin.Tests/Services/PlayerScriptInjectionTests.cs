using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JellyfinUpscalerPlugin.Services;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services;

/// <summary>
/// v1.8.3.35 - #75: the player script reaches jellyfin-web's index.html even when the file
/// cannot be written (Windows installer running as NetworkService, Debian's root-owned web
/// folder, read-only containers). The host is built the way Jellyfin 12.1 builds its own:
/// Program.cs uses Host.CreateDefaultBuilder with the services from ApplicationHost.Init, where
/// plugin registrators run, and Startup.Configure serves the web client inside the BaseUrl map
/// after response compression, from UseDefaultFiles/UseStaticFiles under /web with no-cache on
/// index.html. The web folder here is never written to.
/// </summary>
public sealed class PlayerScriptInjectionTests : IDisposable
{
    private const string Page = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Jellyfin</title></head><body></body></html>";
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly string Tag = PlayerScriptTag.For(typeof(Plugin).Assembly.GetName().Version);
    private readonly string _web = Directory.CreateTempSubdirectory("jf-web-").FullName;

    public PlayerScriptInjectionTests()
    {
        WriteIndex(Page);
        File.WriteAllText(Path.Combine(_web, "main.jellyfin.bundle.js"), "console.log('jellyfin');");
    }

    public void Dispose() => Directory.Delete(_web, true);

    // jellyfin-web 12.1 ships index.html with a byte order mark.
    private void WriteIndex(string html) => File.WriteAllBytes(IndexPath, Bom.Concat(Encoding.UTF8.GetBytes(html)).ToArray());

    private string IndexPath => Path.Combine(_web, "index.html");

    private static int Tags(string html) => Regex.Matches(html, "UPSCALERPlayerIntegration").Count;

    private async Task<(IHost Host, HttpClient Client)> StartAsync(string baseUrl = "", bool plugin = true)
    {
        var pluginServices = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(pluginServices, Mock.Of<IServerApplicationHost>());

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddResponseCompression();
                if (plugin)
                {
                    // Only what the plugin adds to the pipeline; its hosted services need a real server.
                    foreach (var descriptor in pluginServices.Where(d => d.ServiceType == typeof(IStartupFilter)))
                    {
                        services.Add(descriptor);
                    }
                }
            })
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls("http://127.0.0.1:0");
                web.Configure(app => app.Map(baseUrl, main =>
                {
                    main.UseResponseCompression();
                    var files = new PhysicalFileProvider(_web);
                    main.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files, RequestPath = "/web" });
                    main.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = files,
                        RequestPath = "/web",
                        OnPrepareResponse = context =>
                        {
                            if (Path.GetFileName(context.File.Name).Equals("index.html", StringComparison.Ordinal))
                            {
                                context.Context.Response.Headers.CacheControl = new StringValues("no-cache");
                            }
                        }
                    });
                }));
            })
            .Build();
        await host.StartAsync();
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None }) { BaseAddress = new Uri(address) };
        return (host, client);
    }

    private static async Task<(HttpResponseMessage Response, byte[] Body)> GetAsync(HttpClient client, string path, HttpMethod? method = null, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        var response = await client.SendAsync(request);
        return (response, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Page_IsServedWithTheTag_AndTheFileIsUntouched()
    {
        var before = File.ReadAllBytes(IndexPath);
        var (host, client) = await StartAsync();
        using (host)
        using (client)
        {
            var (response, body) = await GetAsync(client, "/web/", null, ("Accept-Encoding", "gzip, br"));
            var html = Encoding.UTF8.GetString(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(response.Content.Headers.ContentEncoding);
            Assert.Equal(body.Length, response.Content.Headers.ContentLength);
            Assert.Contains(Tag + "</head>", html, StringComparison.Ordinal);
            Assert.Equal(1, Tags(html));
            Assert.Equal(Bom, body.Take(3));
            Assert.Null(response.Headers.ETag);
            Assert.Null(response.Content.Headers.LastModified);
            Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
            await host.StopAsync();
        }

        Assert.Equal(before, File.ReadAllBytes(IndexPath));
    }

    [Fact]
    public async Task BaseUrl_IndexHtmlPath_GetsTheTag()
    {
        var (host, client) = await StartAsync("/jellyfin");
        using (host)
        using (client)
        {
            var (response, body) = await GetAsync(client, "/jellyfin/web/index.html");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(Tag, Encoding.UTF8.GetString(body), StringComparison.Ordinal);
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task ConditionalRequest_GetsTheFullPageNotA304()
    {
        // A 304 for the unchanged file would keep a page cached with an older release's tag.
        var (host, client) = await StartAsync();
        using (host)
        using (client)
        {
            var (response, body) = await GetAsync(client, "/web/index.html", null,
                ("If-None-Match", "*"), ("If-Modified-Since", DateTime.UtcNow.AddDays(1).ToString("R")));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(Tag, Encoding.UTF8.GetString(body), StringComparison.Ordinal);
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task OtherFilesAndHeadRequests_PassThroughUnchanged()
    {
        var (host, client) = await StartAsync();
        using (host)
        using (client)
        {
            var (script, scriptBody) = await GetAsync(client, "/web/main.jellyfin.bundle.js");
            Assert.Equal("console.log('jellyfin');", Encoding.UTF8.GetString(scriptBody));
            Assert.NotNull(script.Headers.ETag);

            var (head, headBody) = await GetAsync(client, "/web/", HttpMethod.Head);
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
            Assert.Empty(headBody);
            Assert.Equal(new FileInfo(IndexPath).Length, head.Content.Headers.ContentLength);
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task WritableFolderAlreadyInjected_KeepsOneTagAndTheFileValidators()
    {
        WriteIndex(Page.Replace("</head>", Tag + "</head>", StringComparison.Ordinal));
        var (host, client) = await StartAsync();
        using (host)
        using (client)
        {
            var (response, body) = await GetAsync(client, "/web/");
            Assert.Equal(1, Tags(Encoding.UTF8.GetString(body)));
            Assert.Equal(File.ReadAllBytes(IndexPath), body);
            Assert.NotNull(response.Headers.ETag);
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task TagOfAnEarlierRelease_IsReplacedInThePage()
    {
        var old = "<script src=\"configurationpage?name=UPSCALERPlayerIntegration&release=1.8.3.20\"></script>";
        WriteIndex(Page.Replace("</head>", old + "</head>", StringComparison.Ordinal));
        var (host, client) = await StartAsync();
        using (host)
        using (client)
        {
            var (_, body) = await GetAsync(client, "/web/");
            var html = Encoding.UTF8.GetString(body);
            Assert.Equal(1, Tags(html));
            Assert.Contains(Tag, html, StringComparison.Ordinal);
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task WithoutThePlugin_ThePageHasNoTag()
    {
        // Control: the host alone serves the file as it is.
        var (host, client) = await StartAsync(plugin: false);
        using (host)
        using (client)
        {
            var (_, body) = await GetAsync(client, "/web/");
            Assert.Equal(0, Tags(Encoding.UTF8.GetString(body)));
            await host.StopAsync();
        }
    }

    [Fact]
    public void Registrator_AddsTheStartupFilter()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, Mock.Of<IServerApplicationHost>());
        var filter = Assert.Single(services, d => d.ServiceType == typeof(IStartupFilter));
        Assert.Equal(typeof(PlayerScriptStartupFilter), filter.ImplementationType);
    }

    [Theory]
    [InlineData("<html><head></head></html>", true)]
    [InlineData("<html><HEAD><title>x</title></HEAD></html>", true)]
    [InlineData("<html><body>no head</body></html>", false)]
    public void TryInject_NeedsAHeadEnd(string html, bool injected)
    {
        Assert.Equal(injected, PlayerScriptTag.TryInject(html, Tag, out var result));
        Assert.Equal(injected ? 1 : 0, Tags(result));
    }

    [Fact]
    public void TryInject_LeavesACurrentPageAloneAndCollapsesDuplicates()
    {
        var current = "<html><head>" + Tag + "</head></html>";
        Assert.True(PlayerScriptTag.TryInject(current, Tag, out var same));
        Assert.Same(current, same);

        var twice = "<html><head>" + Tag + Tag + "</head></html>";
        Assert.True(PlayerScriptTag.TryInject(twice, Tag, out var once));
        Assert.Equal(1, Tags(once));
    }
}
