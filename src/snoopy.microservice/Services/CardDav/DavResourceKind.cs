namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The seven shapes of path the CardDAV surface answers on. The two collection shapes carrying no
/// user segment — <c>/dav/principals/</c> and <c>/dav/addressbooks/</c> — are intermediate: they
/// contain one child each, this account's, because the membership is the identity of whoever holds
/// the secret. The first of them is the URL <c>principal-collection-set</c> itself publishes.
/// </summary>
internal enum DavResourceKind
{
    ServiceRoot,
    PrincipalCollection,
    Principal,
    BookCollection,
    Home,
    Collection,
    Card
}
