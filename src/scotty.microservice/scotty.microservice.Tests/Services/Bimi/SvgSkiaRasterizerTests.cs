using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using weesky.Scotty.Microservice.Services.Bimi;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Bimi;

public sealed class SvgSkiaRasterizerTests
{
    private const string Open = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 100 100\">";
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(3);
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] Square = Encoding.UTF8.GetBytes(Open + "<rect width=\"100\" height=\"100\"/></svg>");
    private readonly SvgSkiaRasterizer rasterizer = new();

    [Fact]
    public async Task A_logo_becomes_an_80_pixel_png()
    {
        var png = await ToPngAsync(Open + "<rect width=\"100\" height=\"100\" fill=\"#c00\"/></svg>");

        AssertPng(png);
    }

    [Fact]
    public async Task A_wide_drawing_still_gives_a_square()
    {
        var png = await ToPngAsync("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\"><circle cx=\"100\" cy=\"50\" r=\"40\"/></svg>");

        AssertPng(png);
    }

    [Fact]
    public async Task A_logo_sharing_gradients_and_shapes_still_renders()
    {
        var shapes = string.Concat(Enumerable.Range(0, 200).Select(i => $"<use href=\"#dot\" x=\"{i % 20 * 5}\" y=\"{i / 20 * 10}\"/>"));

        var png = await ToPngAsync(Open + "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#c00\"/><stop offset=\"1\" stop-color=\"#00c\"/></linearGradient>"
            + "<circle id=\"dot\" r=\"2\" fill=\"url(#g)\"/></defs><style>.a{fill:url(#g)}</style><rect class=\"a\" width=\"100\" height=\"100\"/>"
            + shapes + "</svg>");

        AssertPng(png);
    }

    [Theory]
    [InlineData("not an svg at all")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><path></svg>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY x \"y\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>")]
    [InlineData("")]
    public async Task Garbage_gives_null(string svg) => Assert.Null(await ToPngAsync(svg));

    [Theory]
    [InlineData("<image href=\"{0}\" width=\"100\" height=\"100\"/>")]
    [InlineData("<image xlink:href=\"{0}\" width=\"100\" height=\"100\"/>")]
    [InlineData("<use href=\"{0}#x\"/>")]
    [InlineData("<rect width=\"100\" height=\"100\" fill=\"url({0}#p)\"/>")]
    [InlineData("<rect width=\"100\" height=\"100\" style=\"fill:url('{0}')\"/>")]
    [InlineData("<style>@import url('{0}');</style><rect width=\"100\" height=\"100\"/>")]
    [InlineData("<style>rect{{fill:image-set('{0}' 1x)}}</style><rect width=\"100\" height=\"100\"/>")]
    [InlineData("<style>rect{{fill:image('{0}')}}</style><rect width=\"100\" height=\"100\"/>")]
    [InlineData("<style>rect{{background-image:image-set('{0}' 1x)}}</style><rect width=\"100\" height=\"100\"/>")]
    [InlineData("<rect width=\"100\" height=\"100\" style=\"fill:image-set('{0}' 1x)\"/>")]
    [InlineData("<script href=\"{0}\"/><rect width=\"100\" height=\"100\" onload=\"fetch('{0}')\"/>")]
    public async Task External_references_are_never_fetched(string body)
    {
        using var trap = new ConnectionTrap();

        await ToPngAsync(Open + string.Format(body, trap.Url("x")) + "</svg>");

        Assert.False(trap.WasDialled);
    }

    [Fact]
    public async Task A_stylesheet_processing_instruction_is_never_fetched()
    {
        using var trap = new ConnectionTrap();

        await ToPngAsync($"<?xml-stylesheet href=\"{trap.Url("x.css")}\"?>{Open}<rect width=\"9\" height=\"9\"/></svg>");

        Assert.False(trap.WasDialled);
    }

    [Fact]
    public async Task Exponential_use_expansion_is_refused_quickly()
    {
        var svg = new StringBuilder(Open).Append("<defs><path id=\"l0\" d=\"M0 0L9 9\"/>");
        for (var level = 1; level <= 12; level++)
            svg.Append($"<g id=\"l{level}\">").Append(string.Concat(Enumerable.Repeat($"<use href=\"#l{level - 1}\"/>", 10))).Append("</g>");
        svg.Append("</defs><use href=\"#l12\"/></svg>");

        await AssertRefusedQuicklyAsync(svg.ToString());
    }

    [Fact]
    public Task A_use_cycle_is_refused_quickly() =>
        AssertRefusedQuicklyAsync(Open + "<g id=\"a\"><use href=\"#b\"/></g><g id=\"b\"><use xlink:href=\"#a\"/></g></svg>");

    [Fact]
    public Task Deep_nesting_is_refused_quickly() =>
        AssertRefusedQuicklyAsync(Open + string.Concat(Enumerable.Repeat("<g>", 5000)) + string.Concat(Enumerable.Repeat("</g>", 5000)) + "</svg>");

    [Fact]
    public async Task A_pattern_referenced_by_many_shapes_is_refused_quickly()
    {
        var shapes = string.Concat(Enumerable.Repeat("<rect width=\"100\" height=\"100\" fill=\"url(#p)\"/>", 400));
        var tile = string.Concat(Enumerable.Repeat("<circle r=\"1\"/>", 400));

        await AssertRefusedQuicklyAsync(Open + $"<defs><pattern id=\"p\" width=\"0.01\" height=\"0.01\">{tile}</pattern></defs>{shapes}</svg>");
    }

    [Fact]
    public async Task A_huge_blur_and_a_huge_canvas_still_render_quickly()
    {
        var watch = Stopwatch.StartNew();

        var png = await ToPngAsync("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1e9\" height=\"1e9\"><filter id=\"f\"><feGaussianBlur stdDeviation=\"1e6\"/></filter>"
            + "<rect width=\"1e9\" height=\"1e9\" filter=\"url(#f)\" stroke=\"#000\" stroke-width=\"1e6\" stroke-dasharray=\"0.001\"/></svg>");

        Assert.True(watch.Elapsed < Quick, $"took {watch.Elapsed}");
        Assert.True(png is null || png.AsSpan(0, 4).SequenceEqual(PngMagic));
    }

    [Fact]
    public Task Running_over_the_budget_is_a_timeout_not_a_miss() =>
        Assert.ThrowsAsync<TimeoutException>(() => new SvgSkiaRasterizer(TimeSpan.Zero, new SemaphoreSlim(1)).ToPngAsync(Square, 80, CancellationToken.None));

    [Fact]
    public async Task No_render_slot_within_the_budget_is_a_timeout()
    {
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(() => new SvgSkiaRasterizer(TimeSpan.FromMilliseconds(50), new SemaphoreSlim(0)).ToPngAsync(Square, 80, CancellationToken.None));
        Assert.True(watch.Elapsed < Quick, $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task An_abandoned_render_gives_its_slot_back_when_it_ends()
    {
        var gate = new SemaphoreSlim(1);

        await Assert.ThrowsAsync<TimeoutException>(() => new SvgSkiaRasterizer(TimeSpan.Zero, gate).ToPngAsync(Square, 80, CancellationToken.None));

        Assert.True(await gate.WaitAsync(Quick));
    }

    [Fact]
    public async Task Waiting_for_a_render_slot_blocks_no_thread()
    {
        var gate = new SemaphoreSlim(0);
        var renderer = new SvgSkiaRasterizer(TimeSpan.FromSeconds(10), gate);
        var watch = Stopwatch.StartNew();

        var renders = Enumerable.Range(0, 30).Select(_ => renderer.ToPngAsync(Square, 80, CancellationToken.None)).ToList();

        Assert.True(watch.Elapsed < Quick, $"took {watch.Elapsed}");
        Assert.All(renders, render => Assert.False(render.IsCompleted));
        gate.Release();
        Assert.All(await Task.WhenAll(renders), AssertPng);
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_a_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        var gate = new SemaphoreSlim(0);
        var render = new SvgSkiaRasterizer(TimeSpan.FromMinutes(1), gate).ToPngAsync(Square, 80, cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render);
        gate.Release();
        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task Reusing_a_heavy_path_is_charged_by_its_geometry()
    {
        var zigzag = "M0 0" + string.Concat(Enumerable.Range(0, 2600).Select(i => $"L{i % 97} {i % 2 * 9}"));
        var svg = Open + $"<defs><path id=\"z\" d=\"{zigzag}\" fill=\"none\" stroke=\"#000\" stroke-width=\"1e6\" stroke-linejoin=\"round\"/>"
            + "<g id=\"y\">" + string.Concat(Enumerable.Repeat("<use href=\"#z\"/>", 50)) + "</g></defs>" + string.Concat(Enumerable.Repeat("<use href=\"#y\"/>", 60)) + "</svg>";

        Assert.True(SvgGuard.IsSafe(Encoding.UTF8.GetBytes(svg)));
        await AssertRefusedQuicklyAsync(svg);
    }

    [Fact]
    public async Task A_detailed_logo_drawn_once_stays_within_the_cost()
    {
        var outline = "M0 0" + string.Concat(Enumerable.Range(0, 3000).Select(i => $"L{i % 97} {i % 89}")) + "Z";

        AssertPng(await ToPngAsync(Open + $"<path d=\"{outline}\" fill=\"#c00\"/></svg>"));
    }

    [Fact]
    public async Task Nested_use_amplification_passes_the_guard_but_is_refused()
    {
        var svg = new StringBuilder(Open).Append("<defs><path id=\"l0\" d=\"M0 0L9 9\"/>");
        for (var level = 1; level <= 3; level++)
            svg.Append($"<g id=\"l{level}\">").Append(string.Concat(Enumerable.Repeat($"<use href=\"#l{level - 1}\"/>", 30))).Append("</g>");
        svg.Append("</defs><use href=\"#l3\"/></svg>");

        Assert.True(SvgGuard.IsSafe(Encoding.UTF8.GetBytes(svg.ToString())));
        await AssertRefusedQuicklyAsync(svg.ToString());
    }

    private Task<byte[]?> ToPngAsync(string svg) => rasterizer.ToPngAsync(Encoding.UTF8.GetBytes(svg), 80, CancellationToken.None);

    private async Task AssertRefusedQuicklyAsync(string svg)
    {
        var watch = Stopwatch.StartNew();

        Assert.False(SvgComplexity.IsBounded(Encoding.UTF8.GetBytes(svg)));
        Assert.Null(await ToPngAsync(svg));
        Assert.True(watch.Elapsed < Quick, $"took {watch.Elapsed}");
    }

    private static void AssertPng(byte[]? png)
    {
        Assert.NotNull(png);
        Assert.True(png.AsSpan(0, 4).SequenceEqual(PngMagic));
        Assert.Equal(80, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(80, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }
}
