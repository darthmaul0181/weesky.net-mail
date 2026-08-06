namespace weesky.Snoopy.Microservice.Services;

/// <summary>Stable error codes for the connected-accounts feature.</summary>
public static class ConnectedAccountErrors
{
    /// <summary>The stored cipher no longer decrypts under the current KEK — the main password
    /// changed outside the app. Mapped to 409 so the 401 handler never signs the user out over it.</summary>
    public const string CredentialsInvalid = "connected_credentials_invalid";

    /// <summary>Unparseable id, unknown id, another user's id, or an unusable domain row —
    /// deliberately indistinguishable. Mapped to 404.</summary>
    public const string AccountNotFound = "account_not_found";

    /// <summary>The identity provider would not answer, or answered something unusable. Mapped to
    /// 502, like anything else refused by a server we merely talk to.</summary>
    public const string ProviderUnavailable = "oauth_provider_unavailable";
}
