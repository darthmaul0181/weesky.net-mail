using System.Buffers.Binary;
using System.Globalization;
using CSharpFunctionalExtensions;

namespace weesky.Scotty.Microservice.Models;

/// <summary>
/// The instance logo's three renditions. The admin's browser draws them; the server reads the
/// PNG signature and the IHDR header and never decodes a pixel.
/// </summary>
public static class AppLogo
{
    public const string VersionKey = "app.logo";
    public const int MaxRequestBytes = 2 * 1024 * 1024;

    public static IReadOnlyList<int> Sizes { get; } = [32, 192, 512];

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static int MaxBytes(int size) => size switch
    {
        32 => 16 * 1024,
        192 => 256 * 1024,
        512 => 1024 * 1024,
        _ => 0,
    };

    public static Result Check(int size, byte[] png)
    {
        if (!Sizes.Contains(size)) return Result.Failure($"{size} px is not a logo size");
        if (png.Length > MaxBytes(size)) return Result.Failure($"The {size} px logo is over {MaxBytes(size) / 1024} KB");
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(Signature) || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return Result.Failure($"The {size} px logo is not a PNG");

        var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        return width == size && height == size
            ? Result.Success()
            : Result.Failure($"The {size} px logo is {width}x{height}");
    }

    public static string Version(DateTime updatedAt) =>
        updatedAt.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture);
}
