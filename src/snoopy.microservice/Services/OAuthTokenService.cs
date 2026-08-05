using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Caching.Memory;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class OAuthTokenService(
    HttpClient http,
    IMemoryCache cache,
    IConnectedAccountStore accounts,
    IClientSecretProtector protector,
    ILogger<OAuthTokenService> logger) : IOAuthTokenService
{
    /// <summary>Refreshed this far before expiry, so a token cannot die inside a long IMAP session.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    /// <summary>One gate per account: a burst of parallel mail requests must exchange once, not
    /// once each. Static because the service is registered as a typed client.</summary>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    public async Task<Result<string>> GetAccessTokenAsync(
        ConnectedAccount row, OAuthProviderConfig provider, byte[] kek, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(provider);

        var key = CacheKey(row);
        if (cache.TryGetValue<string>(key, out var cached) && cached is not null)
            return Result.Success(cached);

        var gate = Gates.GetOrAdd(row.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out cached) && cached is not null)
                return Result.Success(cached);

            var context = ConnectedAccountCipher.Context(row);

            // A rotated cipher whose write failed earlier is the live secret; the row's is
            // consumed — but only while the row still holds the exact cipher it superseded.
            var pending = PendingCipher(row);
            var refreshToken = pending is not null
                ? ConnectedAccountCipher.Decrypt(kek, pending, context)
                : ConnectedAccountCipher.Decrypt(kek, row.Cipher, context);
            if (pending is not null && refreshToken.IsFailure)
            {
                cache.Remove(PendingKey(row));
                pending = null;
                refreshToken = ConnectedAccountCipher.Decrypt(kek, row.Cipher, context);
            }
            if (refreshToken.IsFailure)
                return Result.Failure<string>(ConnectedAccountErrors.CredentialsInvalid);

            // From here the provider may consume the stored refresh token, so the caller's
            // disconnect must not abandon the exchange or the write that keeps the rotated
            // token: both run to completion under the client's own 10 s timeout.
            var exchanged = await PostAsync(provider, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken.Value,
                ["scope"] = provider.Scopes
            }, CancellationToken.None);
            if (exchanged.IsFailure) return Result.Failure<string>(exchanged.Error);

            var token = exchanged.Value;
            var owed = token.RefreshToken is { Length: > 0 } rotated && rotated != refreshToken.Value
                ? ConnectedAccountCipher.Encrypt(kek, rotated, context)
                : pending;
            // row.Cipher is captured here because the store assigns it before a save that can
            // still throw — read inside the catch it would name the new bytes, not the row's.
            if (owed is not null) await PersistRotationAsync(row, row.Cipher, owed);

            cache.Set(key, token.AccessToken, Lifetime(token.ExpiresInSeconds));
            return Result.Success(token.AccessToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<Result<OAuthTokenResponse>> ExchangeCodeAsync(
        OAuthProviderConfig provider, string code, string codeVerifier, string redirectUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return PostAsync(provider, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
            ["scope"] = provider.Scopes
        }, cancellationToken);
    }

    private static string CacheKey(ConnectedAccount row) => $"oauth:{row.UserId:N}:{row.Id:N}";

    private static string PendingKey(ConnectedAccount row) => $"oauth-pending:{row.Id:N}";

    /// <summary>
    /// The stashed rotation, honored only while the row still holds the cipher it superseded.
    /// A row rewritten since — re-consent, a password write, a re-key — is newer than the stash,
    /// which self-evicts; no writer of the row needs to know the stash exists.
    /// </summary>
    private byte[]? PendingCipher(ConnectedAccount row)
    {
        if (!cache.TryGetValue<(byte[] Supersedes, byte[] Cipher)>(PendingKey(row), out var stashed))
            return null;
        if (stashed.Supersedes.AsSpan().SequenceEqual(row.Cipher)) return stashed.Cipher;
        cache.Remove(PendingKey(row));
        return null;
    }

    /// <summary>
    /// The one write that must never be lost: the provider has consumed the old refresh token.
    /// On failure the rotated cipher stays in memory, paired with the row cipher it supersedes,
    /// and the next refresh exchanges with it and retries — only a process exit loses it.
    /// </summary>
    private async Task PersistRotationAsync(ConnectedAccount row, byte[] supersedes, byte[] cipher)
    {
        try
        {
            await accounts.UpdateCipherAsync(row, cipher, CancellationToken.None);
            cache.Remove(PendingKey(row));
        }
        catch
        {
            cache.Set(PendingKey(row), (supersedes, cipher));
            throw;
        }
    }

    /// <summary>Never negative, and never longer than the provider promised.</summary>
    private static TimeSpan Lifetime(int expiresInSeconds)
    {
        var life = TimeSpan.FromSeconds(expiresInSeconds) - Margin;
        return life > TimeSpan.Zero ? life : TimeSpan.FromSeconds(30);
    }

    private async Task<Result<OAuthTokenResponse>> PostAsync(
        OAuthProviderConfig provider, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        if (protector.Unprotect(provider.ClientSecret) is not { Length: > 0 } clientSecret)
        {
            logger.LogError(
                "The OAuth client secret for {TokenUrl} does not open — the key ring was rotated or the row is corrupt",
                provider.TokenUrl);
            return Result.Failure<OAuthTokenResponse>(ConnectedAccountErrors.ProviderUnavailable);
        }

        form["client_id"] = provider.ClientId;
        form["client_secret"] = clientSecret;

        try
        {
            using var response = await http.PostAsync(
                provider.TokenUrl, new FormUrlEncodedContent(form), cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<OAuthTokenResponse>(
                    await DescribeFailureAsync(response, provider, cancellationToken));

            var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken);
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                logger.LogError("The token endpoint {TokenUrl} answered no access token", provider.TokenUrl);
                return Result.Failure<OAuthTokenResponse>(ConnectedAccountErrors.ProviderUnavailable);
            }

            return Result.Success(token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogError(ex, "Could not reach the token endpoint {TokenUrl}", provider.TokenUrl);
            return Result.Failure<OAuthTokenResponse>(ConnectedAccountErrors.ProviderUnavailable);
        }
    }

    /// <summary>
    /// invalid_grant is the one refusal the user can act on: the consent is gone and only a new
    /// one brings the mailbox back. Everything else is the provider's problem, not theirs.
    /// </summary>
    private async Task<string> DescribeFailureAsync(
        HttpResponseMessage response, OAuthProviderConfig provider, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var (error, code) = ReadRefusal(body);
        var invalidGrant = error is not null
            ? error == "invalid_grant"
            : body.Contains("\"invalid_grant\"", StringComparison.Ordinal);

        // Without these two a wrong client secret and a revoked consent read identically, and only
        // the second is the user's to fix. Both are the provider's own vocabulary, never the user's.
        logger.LogWarning(
            "Token endpoint {TokenUrl} refused with {Status}; error={ProviderError}, code={ProviderCode}, invalid_grant={InvalidGrant}",
            provider.TokenUrl, (int)response.StatusCode, error ?? "unknown", code ?? "none", invalidGrant);

        return invalidGrant
            ? ConnectedAccountErrors.CredentialsInvalid
            : ConnectedAccountErrors.ProviderUnavailable;
    }

    /// <summary>
    /// RFC 6749's error code, and the identifier the description leads with — Microsoft's
    /// `AADSTS7000215` names a bad secret where the bare `invalid_client` only says which side
    /// failed. The description itself is left unread: a provider may echo what was sent to it.
    /// </summary>
    private static (string? Error, string? Code) ReadRefusal(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var error = ReadString(json.RootElement, "error");
            return (error, ReadIdentifier(ReadString(json.RootElement, "error_description")));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The leading letters-then-digits token of a description, or nothing.</summary>
    private static string? ReadIdentifier(string? description)
    {
        if (string.IsNullOrEmpty(description)) return null;

        var letters = 0;
        while (letters < description.Length && char.IsAsciiLetterUpper(description[letters])) letters++;

        var digits = letters;
        while (digits < description.Length && char.IsAsciiDigit(description[digits])) digits++;

        return letters > 0 && digits > letters ? description[..digits] : null;
    }
}
