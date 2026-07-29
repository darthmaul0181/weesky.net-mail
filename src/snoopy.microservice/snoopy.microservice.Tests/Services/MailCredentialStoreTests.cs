using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailCredentialStoreTests
{
    private static MailCredentialStore CreateSut(IDataProtectionProvider? provider = null)
        => new(provider ?? new EphemeralDataProtectionProvider());

    private static MailCredentialPayload V1(string password) => new(password, null);

    [Fact]
    public void Store_WritesAnHttpOnlySecureStrictCookie()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();

        sut.Store(context.Response, V1("hunter2"), TimeSpan.FromMinutes(30));

        var setCookie = string.Join(";", context.Response.Headers["Set-Cookie"].ToArray());
        Assert.Contains("MailCredentials=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Store_DoesNotWriteThePasswordInClear()
    {
        var sut = CreateSut();
        var context = new DefaultHttpContext();

        sut.Store(context.Response, V1("hunter2"), TimeSpan.FromMinutes(30));

        var setCookie = string.Join(";", context.Response.Headers["Set-Cookie"].ToArray());
        Assert.DoesNotContain("hunter2", setCookie);
    }

    [Fact]
    public void Retrieve_ReturnsThePasswordStoredByTheSameProvider()
    {
        var provider = new EphemeralDataProtectionProvider();
        var response = new DefaultHttpContext().Response;
        CreateSut(provider).Store(response, V1("hunter2"), TimeSpan.FromMinutes(30));

        var request = new DefaultHttpContext().Request;
        request.Headers["Cookie"] = $"MailCredentials={ExtractCookieValue(response)}";

        var result = CreateSut(provider).Retrieve(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("hunter2", result.Value.Password);
    }

    [Fact]
    public void Retrieve_ReadsBackTheV2Payload()
    {
        var provider = new EphemeralDataProtectionProvider();
        var kek = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var response = new DefaultHttpContext().Response;
        CreateSut(provider).Store(response, new MailCredentialPayload("hunter2", kek), TimeSpan.FromMinutes(30));

        var request = new DefaultHttpContext().Request;
        request.Headers["Cookie"] = $"MailCredentials={ExtractCookieValue(response)}";

        var result = CreateSut(provider).Retrieve(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("hunter2", result.Value.Password);
        Assert.Equal<byte[]>(kek, result.Value.Kek!);
    }

    // A cookie issued before the KEK existed carries the bare password and must keep working:
    // signing every open session out on deploy is exactly what the v1 branch exists to avoid.
    [Fact]
    public void Retrieve_TreatsALegacyValueAsV1()
    {
        var provider = new EphemeralDataProtectionProvider();
        var legacy = provider.CreateProtector("weesky.imap.credentials").Protect("hunter2");

        var request = new DefaultHttpContext().Request;
        request.Headers["Cookie"] = $"MailCredentials={legacy}";

        var result = CreateSut(provider).Retrieve(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("hunter2", result.Value.Password);
        Assert.Null(result.Value.Kek);
    }

    // The marker is not a reserved prefix: a password that opens with it but whose parts are not
    // base64 is still a v1 value, and its whole text is the password.
    [Fact]
    public void Retrieve_TreatsAPasswordStartingLikeTheMarkerAsV1()
    {
        const string password = "wm2|not|base64!";
        var provider = new EphemeralDataProtectionProvider();
        var response = new DefaultHttpContext().Response;
        CreateSut(provider).Store(response, V1(password), TimeSpan.FromMinutes(30));

        var request = new DefaultHttpContext().Request;
        request.Headers["Cookie"] = $"MailCredentials={ExtractCookieValue(response)}";

        var result = CreateSut(provider).Retrieve(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(password, result.Value.Password);
        Assert.Null(result.Value.Kek);
    }

    // Empty base64 parses to an empty array instead of throwing. Accepted as a KEK, this password
    // would come back empty and the sliding renewal would re-issue the loss as a genuine v2 cookie.
    [Theory]
    [InlineData("wm2||")]
    [InlineData("wm2|abcd|efgh")]
    public void Retrieve_TreatsAWrongLengthKekAsV1(string password)
    {
        var provider = new EphemeralDataProtectionProvider();
        var response = new DefaultHttpContext().Response;
        CreateSut(provider).Store(response, V1(password), TimeSpan.FromMinutes(30));

        var request = new DefaultHttpContext().Request;
        request.Headers["Cookie"] = $"MailCredentials={ExtractCookieValue(response)}";

        var result = CreateSut(provider).Retrieve(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(password, result.Value.Password);
        Assert.Null(result.Value.Kek);
    }

    [Fact]
    public void Retrieve_TreatsAHalfLengthKekAsV1()
    {
        var provider = new EphemeralDataProtectionProvider();
        var password = "wm2|" + Convert.ToBase64String("hunter2"u8.ToArray())
                              + "|" + Convert.ToBase64String(new byte[16]);
        var response = new DefaultHttpContext().Response;
        CreateSut(provider).Store(response, V1(password), TimeSpan.FromMinutes(30));

        var request = new DefaultHttpContext().Request;
        request.Headers["Cookie"] = $"MailCredentials={ExtractCookieValue(response)}";

        var result = CreateSut(provider).Retrieve(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(password, result.Value.Password);
        Assert.Null(result.Value.Kek);
    }

    [Fact]
    public void Store_DoesNotWriteTheKekInClear()
    {
        var kek = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var context = new DefaultHttpContext();

        CreateSut().Store(context.Response, new MailCredentialPayload("hunter2", kek), TimeSpan.FromMinutes(30));

        var setCookie = string.Join(";", context.Response.Headers["Set-Cookie"].ToArray());
        Assert.DoesNotContain(Convert.ToBase64String(kek), setCookie);
    }

    [Fact]
    public void Retrieve_FailsWhenCookieIsAbsent()
    {
        var result = CreateSut().Retrieve(new DefaultHttpContext().Request);

        Assert.True(result.IsFailure);
        Assert.Equal("credentials_unavailable", result.Error);
    }

    [Fact]
    public void Retrieve_FailsWhenTheKeyRingChanged()
    {
        var response = new DefaultHttpContext().Response;
        CreateSut(new EphemeralDataProtectionProvider()).Store(response, V1("hunter2"), TimeSpan.FromMinutes(30));

        var request = new DefaultHttpContext().Request;
        request.Headers["Cookie"] = $"MailCredentials={ExtractCookieValue(response)}";

        // A different provider stands in for a lost or rotated-away key ring.
        var result = CreateSut(new EphemeralDataProtectionProvider()).Retrieve(request);

        Assert.True(result.IsFailure);
        Assert.Equal("credentials_unavailable", result.Error);
    }

    [Fact]
    public void Clear_ExpiresTheCookie()
    {
        var context = new DefaultHttpContext();

        CreateSut().Clear(context.Response);

        var setCookie = string.Join(";", context.Response.Headers["Set-Cookie"].ToArray());
        Assert.Contains("MailCredentials=", setCookie);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Store_ThrowsWhenResponseIsNull()
        => Assert.Throws<ArgumentNullException>(() => CreateSut().Store(null!, V1("x"), TimeSpan.FromMinutes(1)));

    [Fact]
    public void Retrieve_ThrowsWhenRequestIsNull()
        => Assert.Throws<ArgumentNullException>(() => CreateSut().Retrieve(null!));

    private static string ExtractCookieValue(HttpResponse response)
    {
        var header = string.Join(";", response.Headers["Set-Cookie"].ToArray());
        const string name = "MailCredentials=";
        var start = header.IndexOf(name, StringComparison.Ordinal) + name.Length;
        var end = header.IndexOf(';', start);
        return end < 0 ? header[start..] : header[start..end];
    }
}
