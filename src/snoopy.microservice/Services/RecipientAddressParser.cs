using MimeKit;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The address-parsing policy every literal address field on the API shares — recipients,
/// fromAddress, and the identities payload — so the three cannot silently drift apart.
/// MimeKit accepts a bare local part by default, so "not-an-address" would parse.
/// </summary>
internal static class RecipientAddressParser
{
    public static readonly ParserOptions Options = Create();

    private static ParserOptions Create()
    {
        var options = ParserOptions.Default.Clone();
        options.AllowAddressesWithoutDomain = false;
        return options;
    }
}
