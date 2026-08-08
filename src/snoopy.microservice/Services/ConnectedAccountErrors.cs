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

    /// <summary>Connect's own address already names the caller's own mailbox. Mapped to 400.</summary>
    public const string AlreadySignedIn = "already_signed_in";

    /// <summary>Connect's address does not parse. Mapped to 400.</summary>
    public const string InvalidEmailAddress = "invalid_email_address";

    /// <summary>A domain id naming no row, or one whose stored transport security no longer parses
    /// — deliberately indistinguishable, the same choice <see cref="AccountNotFound"/> makes.
    /// Mapped to 400.</summary>
    public const string UnknownDomain = "unknown_domain";

    /// <summary>Connect was asked to probe a password against a domain that signs in with a
    /// provider instead — a password entered here would have nowhere valid to be stored. Mapped
    /// to 400.</summary>
    public const string ProviderDomain = "provider_domain";

    /// <summary>The domain advertises OAuth2 but its provider configuration is incomplete — an
    /// administrator error, so the cause stays in the log and this stays generic. Not
    /// <see cref="ProviderUnavailable"/>: that one is the provider itself refusing to answer:
    /// this one is a row this server was never able to talk to in the first place. Mapped to 400.</summary>
    public const string ProviderConfigIncomplete = "provider_not_available";

    /// <summary>The domain is not OAuth2 at all, asked for by OAuthStart's non-reconnect branch.
    /// Mapped to 400.</summary>
    public const string NotAProviderDomain = "not_a_provider_domain";

    /// <summary>Connect's or OAuthComplete's address exceeds connected_accounts.email's width.
    /// Mapped to 400.</summary>
    public const string AddressTooLong = "address_too_long";

    /// <summary>Connect or UpdatePassword was given an empty password. Mapped to 400.</summary>
    public const string PasswordRequired = "password_required";

    /// <summary>Connect's or UpdatePassword's password exceeds the cipher column's bound. Mapped
    /// to 400.</summary>
    public const string PasswordTooLong = "password_too_long";

    /// <summary>OAuthStart's reconnect branch was pointed at a row that signs in with a password —
    /// a password row cannot be reconnected. Mapped to 400.</summary>
    public const string AccountUsesPassword = "account_uses_password";

    /// <summary>UpdatePassword was pointed at a row that signs in with a provider — an OAuth2 row
    /// cannot have its password replaced. The mirror of <see cref="AccountUsesPassword"/>, in the
    /// other direction. Mapped to 400.</summary>
    public const string AccountUsesProvider = "account_uses_provider";

    /// <summary>A reconnect's consent came back for a different mailbox than the row being
    /// reconnected — the cipher context is bound to the address, so the token could never be
    /// stored against this row. Mapped to 400.</summary>
    public const string ReconnectMismatch = "reconnect_mismatch";

    /// <summary>The handshake was consumed but never carries a refresh token and an email — the
    /// provider answered something OAuthCallback could not use. Mapped to 400.</summary>
    public const string HandshakeIncomplete = "oauth_handshake_incomplete";

    /// <summary>OAuthStart named neither a domain to connect from nor an account to reconnect, or
    /// named both. Mapped to 400.</summary>
    public const string OAuthStartTargetRequired = "oauth_start_target_required";
}
