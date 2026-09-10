using System.Text;
using weesky.Snoopy.Microservice.Services.Dav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Dav;

/// <summary>The one door every DAV body comes through: strict UTF-8, and a leading signature that
/// is not content.</summary>
public sealed class DavBodyTests
{
    [Fact]
    public void AUtf8Signature_IsNotContent()
    {
        // RFC 3629 section 6: EF BB BF ahead of BEGIN: is a signature. Windows exporters write
        // it, and no iCalendar or vCard parser recognises the first line behind it.
        var body = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("BEGIN:VCALENDAR")).ToArray();

        Assert.True(DavBody.TryDecode(body, out var text));
        Assert.Equal("BEGIN:VCALENDAR", text);
    }

    [Fact]
    public void ASignatureAnywhereButTheHead_IsLeftAlone()
    {
        // U+FEFF is a zero-width no-break space anywhere else, and the ETag must describe what the
        // client sent.
        var body = Encoding.UTF8.GetBytes("SUMMARY:a\uFEFFb");

        Assert.True(DavBody.TryDecode(body, out var text));
        Assert.Equal("SUMMARY:a\uFEFFb", text);
    }

    [Fact]
    public void ASignatureAlone_DecodesToNothing()
    {
        Assert.True(DavBody.TryDecode(Encoding.UTF8.GetPreamble(), out var text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ABodyThatIsNotUtf8_IsStillRefused()
    {
        // The signature is stripped BEFORE the strict decode, never instead of it.
        Assert.False(DavBody.TryDecode(Encoding.Latin1.GetBytes("SUMMARY:caf\u00e9"), out _));
    }
}
