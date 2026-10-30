namespace weesky.Scotty.Microservice.Services;

internal static class HttpContentExtensions
{
    // The declared length is checked first, the stream still counted: a server may omit or lie about it.
    public static async Task<byte[]?> ReadCappedAsync(this HttpContent content, int max, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > max) return null;

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
