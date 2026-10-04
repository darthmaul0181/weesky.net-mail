using System.Buffers.Binary;
using weesky.Scotty.Microservice.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Models;

public sealed class AppLogoTests
{
    /// <summary>A PNG signature and an IHDR chunk header: all the validator ever reads.</summary>
    internal static byte[] Png(int width, int height, int length = 64)
    {
        var bytes = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        return bytes;
    }

    [Theory]
    [InlineData(32)]
    [InlineData(192)]
    [InlineData(512)]
    public void Check_AcceptsASquarePngOfTheAnnouncedSize(int size) =>
        Assert.True(AppLogo.Check(size, Png(size, size)).IsSuccess);

    [Fact]
    public void Check_RefusesAnUnknownSize() => Assert.True(AppLogo.Check(64, Png(64, 64)).IsFailure);

    [Fact]
    public void Check_RefusesAJpeg()
    {
        var jpeg = Png(192, 192);
        jpeg[0] = 0xFF; jpeg[1] = 0xD8;
        Assert.True(AppLogo.Check(192, jpeg).IsFailure);
    }

    [Fact]
    public void Check_RefusesATruncatedFile() => Assert.True(AppLogo.Check(32, Png(32, 32)[..20]).IsFailure);

    [Fact]
    public void Check_RefusesAPngWhoseFirstChunkIsNotIhdr()
    {
        var png = Png(32, 32);
        "tEXt"u8.CopyTo(png.AsSpan(12));
        Assert.True(AppLogo.Check(32, png).IsFailure);
    }

    [Theory]
    [InlineData(192, 191)]
    [InlineData(512, 192)]
    [InlineData(1000, 1000)]
    public void Check_RefusesAWrongOrNonSquareDimension(int width, int height) =>
        Assert.True(AppLogo.Check(192, Png(width, height)).IsFailure);

    [Theory]
    [InlineData(32, 16 * 1024)]
    [InlineData(192, 256 * 1024)]
    [InlineData(512, 1024 * 1024)]
    public void Check_AcceptsTheCapAndRefusesOneByteMore(int size, int cap)
    {
        Assert.True(AppLogo.Check(size, Png(size, size, cap)).IsSuccess);
        Assert.True(AppLogo.Check(size, Png(size, size, cap + 1)).IsFailure);
    }

    [Fact]
    public void Version_IsCompactUtcToTheMillisecond() =>
        Assert.Equal("20261004T101500123",
            AppLogo.Version(new DateTime(2026, 10, 4, 10, 15, 0, 123, DateTimeKind.Utc)));
}
