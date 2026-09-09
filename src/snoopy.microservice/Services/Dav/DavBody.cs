using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// Decodes a request body as strict UTF-8, refusing rather than replacing. The storage is text:
/// an ISO-8859-1 body — which old 3.0 exports still produce under a <c>CHARSET</c> parameter —
/// would decode to <c>U+FFFD</c> under the default fallback, and the ETag would then lie, what is
/// stored no longer being what was sent. A leading UTF-8 signature is stripped before the decode:
/// RFC 3629 § 6 makes it a marker, not content.
/// </summary>
internal static class DavBody
{
    // DecoderExceptionFallback, never the silent replacement fallback.
    private static readonly UTF8Encoding Strict = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static bool TryDecode(ReadOnlySpan<byte> body, [NotNullWhen(true)] out string? text)
    {
        // Windows exporters write the signature ahead of BEGIN:, and every parser downstream then
        // fails on the first line. Before the decode, never instead of it: the rest is still strict.
        if (body.StartsWith(FileText.Utf8Signature)) body = body[FileText.Utf8Signature.Length..];

        try
        {
            text = Strict.GetString(body);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = null;
            return false;
        }
    }
}
