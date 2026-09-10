namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// The shapes of path the /dav surface answers on, in two trees hanging off one principal. The
/// three collection shapes carrying no user segment — <c>/dav/principals/</c>,
/// <c>/dav/addressbooks/</c> and <c>/dav/calendars/</c> — are intermediate: they contain one child
/// each, this account's, because the membership is the identity of whoever holds the secret. The
/// first of them is the URL <c>principal-collection-set</c> itself publishes.
/// </summary>
internal enum DavResourceKind
{
    ServiceRoot,
    PrincipalCollection,
    Principal,
    AddressBookCollection,
    AddressBookHome,
    AddressBook,
    Card,
    CalendarCollection,
    CalendarHome,
    Calendar,
    Event
}
