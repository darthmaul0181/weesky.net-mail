using System.Diagnostics;
using SkiaSharp;
using Svg;
using Svg.Model;
using Svg.Skia;

namespace weesky.Scotty.Microservice.Services.Bimi;

/// <summary>
/// Draws a logo with Svg.Skia, never fetching: external resources are disabled, filters, masks and
/// markers (none of which SVG Tiny PS allows) are ignored, and <see cref="SvgComplexity"/> bounds the
/// drawing first. The render then runs against its own budget, since it sits outside the lookup's,
/// and holds a slot of a shared gate until it really ends: a render abandoned past its budget cannot
/// be aborted, so the gate is what keeps such leftovers from taking every core.
/// </summary>
internal sealed class SvgSkiaRasterizer(TimeSpan budget, SemaphoreSlim gate) : ISvgRasterizer
{
    private static readonly SemaphoreSlim Renders = new(Math.Max(1, Environment.ProcessorCount / 2));

    private static readonly SvgDocumentLoadOptions LoadOptions = new()
    {
        ProcessingMode = SvgProcessingMode.SecureStatic,
        ExternalResources = SvgExternalResourcePolicy.Disabled,
    };

    public SvgSkiaRasterizer() : this(TimeSpan.FromSeconds(2), Renders) { }

    public async Task<byte[]?> ToPngAsync(byte[] svg, int size, CancellationToken cancellationToken)
    {
        if (!SvgComplexity.IsBounded(svg)) return null;

        var started = Stopwatch.GetTimestamp();
        if (!await gate.WaitAsync(budget, cancellationToken)) throw new TimeoutException("No render slot freed within the budget");

        var render = Task.Run(() => Render(svg, size), CancellationToken.None);
        _ = render.ContinueWith(_ => gate.Release(), TaskScheduler.Default);
        var left = budget - Stopwatch.GetElapsedTime(started);
        return await render.WaitAsync(left > TimeSpan.Zero ? left : TimeSpan.Zero, cancellationToken);
    }

    private static byte[]? Render(byte[] svg, int size)
    {
        try
        {
            using var document = new SKSvg();
            document.Settings.EnableJavaScript = false;
            document.Settings.EnableExternalJavaScript = false;
            document.Settings.EnableSvgFonts = false;
            document.Settings.EnableTextReferences = false;
            document.IgnoreAttributes = DrawAttributes.Filter | DrawAttributes.Mask | DrawAttributes.Markers;
            using var stream = new MemoryStream(svg);
            var picture = document.Load(stream, new SvgParameters(null, null, null, LoadOptions));
            return picture is { CullRect: { Width: > 0, Height: > 0 } } ? Encode(picture, size) : null;
        }
        // The parsers beneath throw whatever they throw; a drawing that cannot be read is no logo.
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] Encode(SKPicture picture, int size)
    {
        var bounds = picture.CullRect;
        var scale = Math.Min(size / bounds.Width, size / bounds.Height);
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate((size - bounds.Width * scale) / 2, (size - bounds.Height * scale) / 2);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
