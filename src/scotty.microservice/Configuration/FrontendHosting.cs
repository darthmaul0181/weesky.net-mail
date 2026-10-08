using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace weesky.Scotty.Microservice.Configuration;

/// <summary>
/// Serves the web pages and their <c>/config.js</c> beside the API, in the Docker image only. With
/// two addresses, each answers only its own part: the pages on theirs, the API on its own.
/// </summary>
public static class FrontendHosting
{
    private const string NoCache = "no-cache";
    private const string Immutable = "public, max-age=31536000, immutable";
    private static readonly PathString[] ApiPrefixes = ["/api", "/dav", "/.well-known", "/swagger"];
    private static readonly PathString Health = "/health";
    private static readonly PathString Assets = "/assets";
    private static readonly PathString ConfigJs = "/config.js";

    /// <summary>True for an address the pages answer: neither the API's nor <c>/health</c>.</summary>
    public static bool IsPagePath(PathString path) =>
        !path.StartsWithSegments(Health) && !ApiPrefixes.Any(prefix => path.StartsWithSegments(prefix));

    /// <summary>With two addresses, a page asked of the API's host or the API asked of another.</summary>
    internal static bool Misrouted(HttpRequest request, FrontendSettings frontend) =>
        frontend.ApiHost is not null
        && !request.Path.StartsWithSegments(Health)
        && IsPagePath(request.Path) == string.Equals(request.Host.Host, frontend.ApiHost, StringComparison.OrdinalIgnoreCase);

    public static IApplicationBuilder UseFrontendHosting(this IApplicationBuilder app, FrontendSettings? frontend)
    {
        if (frontend is null) return app;

        var files = new PhysicalFileProvider(frontend.Root);
        var index = files.GetFileInfo("index.html");
        var configJs = Encoding.UTF8.GetBytes(
            $"window.SCOTTY_CONFIG = {JsonSerializer.Serialize(new { apiBase = frontend.ApiBase })};\n");

        app.Use((context, next) =>
        {
            if (!Misrouted(context.Request, frontend)) return next(context);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        return app.MapWhen(context => IsPagePath(context.Request.Path), pages => pages
            .Use((context, next) => context.Request.Path == ConfigJs && IsRead(context.Request)
                ? SendAsync(context, configJs)
                : next(context))
            .UseStaticFiles(new StaticFileOptions
            {
                FileProvider = files,
                OnPrepareResponse = file => file.Context.Response.Headers.CacheControl =
                    file.Context.Request.Path.StartsWithSegments(Assets) ? Immutable : NoCache,
            })
            .Run(context =>
            {
                if (context.Request.Path.StartsWithSegments(Assets) || !IsRead(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = NoCache;
                return context.Response.SendFileAsync(index);
            }));
    }

    private static bool IsRead(HttpRequest request) => HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);

    private static Task SendAsync(HttpContext context, byte[] configJs)
    {
        context.Response.ContentType = "text/javascript; charset=utf-8";
        context.Response.Headers.CacheControl = NoCache;
        return context.Response.Body.WriteAsync(configJs).AsTask();
    }
}
