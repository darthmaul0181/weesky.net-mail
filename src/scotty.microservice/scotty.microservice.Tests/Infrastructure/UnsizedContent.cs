using System.Net;

namespace weesky.Scotty.Microservice.Tests.Infrastructure;

/// <summary>A content whose length is unknown up front, the case where only counting the stream protects us.</summary>
internal sealed class UnsizedContent(byte[] bytes) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        stream.WriteAsync(bytes).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
