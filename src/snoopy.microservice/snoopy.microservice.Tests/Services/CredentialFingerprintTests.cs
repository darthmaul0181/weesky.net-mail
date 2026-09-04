using MailKit.Security;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The pool indexes by what authenticated, never by the secret itself. These pin the three
/// properties that make that safe: same credential same key, any difference a different key, and
/// nothing that prints the secret.
/// </summary>
public sealed class CredentialFingerprintTests
{
    private readonly CredentialFingerprint _fingerprint = new();

    [Fact]
    public void Of_IsStableForTheSameCredential()
    {
        Assert.Equal(
            _fingerprint.Of(new PasswordCredential("hunter2")),
            _fingerprint.Of(new PasswordCredential("hunter2")));
    }

    [Fact]
    public void Of_DiffersBetweenTwoPasswords()
    {
        Assert.NotEqual(
            _fingerprint.Of(new PasswordCredential("hunter2")),
            _fingerprint.Of(new PasswordCredential("hunter3")));
    }

    // A password and an OAuth token of the same text are not the same credential.
    [Fact]
    public void Of_DiffersBetweenCredentialKindsOfTheSameText()
    {
        Assert.NotEqual(
            _fingerprint.Of(new PasswordCredential("token")),
            _fingerprint.Of(new OAuthCredential("token")));
    }

    // Another process draws another key: its fingerprints mean nothing here.
    [Fact]
    public void Of_DiffersBetweenTwoProcesses()
    {
        Assert.NotEqual(
            new CredentialFingerprint().Of(new PasswordCredential("hunter2")),
            new CredentialFingerprint().Of(new PasswordCredential("hunter2")));
    }

    [Fact]
    public void Of_NeverContainsTheSecret()
    {
        var value = _fingerprint.Of(new PasswordCredential("hunter2"));

        Assert.DoesNotContain("hunter2", value);
        Assert.Equal(44, value.Length); // Base64 of 32 bytes, fixed whatever the input
    }

    [Fact]
    public void PoolKey_ToString_NamesTheEndpointButNotTheFingerprint()
    {
        var connection = TestConnections.Primary("alice@weesky.be", "hunter2");
        var key = PoolKey.From(connection, _fingerprint);

        Assert.Contains("alice@weesky.be", key.ToString());
        Assert.DoesNotContain(key.Fingerprint, key.ToString());
    }

    [Fact]
    public void PoolKey_From_DiffersWhenTransportSecurityDiffers()
    {
        var connection = TestConnections.Primary("alice@weesky.be", "hunter2");

        Assert.NotEqual(
            PoolKey.From(connection, _fingerprint),
            PoolKey.From(connection with { ImapSecurity = SecureSocketOptions.SslOnConnect }, _fingerprint));
    }
}
