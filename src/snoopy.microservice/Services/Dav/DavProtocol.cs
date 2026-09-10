namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// One of the two synchronisation services behind the shared secret. Public because the credential
/// store and its controller take it, and both are.
/// </summary>
public enum DavProtocol
{
    CardDav,
    CalDav
}
