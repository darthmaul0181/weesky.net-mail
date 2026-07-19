using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services
{
    public class MailCredentialStoreTests
    {
        private static MailCredentialStore CreateSut(IDataProtectionProvider? provider = null)
            => new(provider ?? new EphemeralDataProtectionProvider());

        [Fact]
        public void Store_WritesAnHttpOnlySecureStrictCookie()
        {
            var sut = CreateSut();
            var context = new DefaultHttpContext();

            sut.Store(context.Response, "hunter2", TimeSpan.FromMinutes(30));

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

            sut.Store(context.Response, "hunter2", TimeSpan.FromMinutes(30));

            var setCookie = string.Join(";", context.Response.Headers["Set-Cookie"].ToArray());
            Assert.DoesNotContain("hunter2", setCookie);
        }

        [Fact]
        public void Retrieve_ReturnsThePasswordStoredByTheSameProvider()
        {
            var provider = new EphemeralDataProtectionProvider();
            var response = new DefaultHttpContext().Response;
            CreateSut(provider).Store(response, "hunter2", TimeSpan.FromMinutes(30));

            var request = new DefaultHttpContext().Request;
            request.Headers["Cookie"] = $"MailCredentials={ExtractCookieValue(response)}";

            var result = CreateSut(provider).Retrieve(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("hunter2", result.Value);
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
            CreateSut(new EphemeralDataProtectionProvider()).Store(response, "hunter2", TimeSpan.FromMinutes(30));

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
            => Assert.Throws<ArgumentNullException>(() => CreateSut().Store(null!, "x", TimeSpan.FromMinutes(1)));

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
}
