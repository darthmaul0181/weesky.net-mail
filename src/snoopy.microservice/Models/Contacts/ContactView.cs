namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// One contact as the client reads it. Carries neither <c>Uid</c> nor <c>VCardRaw</c>: no screen
/// reads either, and the raw card would multiply the payload of a list already fetched whole.
/// </summary>
/// <param name="Id">The contact's identifier.</param>
/// <param name="FirstName">The contact's first name, if any.</param>
/// <param name="LastName">The contact's last name, if any.</param>
/// <param name="Nickname">The contact's nickname, if any.</param>
/// <param name="IsFavorite">Whether the contact is starred.</param>
/// <param name="Addresses">Ordered; the first entry is the primary address.</param>
/// <param name="DisplayName">The card's FN, if the projection has set one yet.</param>
/// <param name="HasPhoto">Whether GET /api/Contacts/{id}/Photo answers a picture.</param>
public sealed record ContactView(
    Guid Id,
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses,
    // Trailing and defaulted, not inserted alongside the fields above: this keeps every existing
    // positional construction of a ContactView (controller, store, and their tests) compiling
    // unchanged while the projection that actually fills them lands in a later task.
    string? DisplayName = null,
    bool HasPhoto = false);
