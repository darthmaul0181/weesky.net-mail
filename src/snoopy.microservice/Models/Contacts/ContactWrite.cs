namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>A validated write-side e-mail line. <c>Position</c> null = a new line (decision 4).</summary>
public sealed record ContactWriteEmail(int? Position, string Address, string Type, int? Pref = null);

/// <summary>A validated write-side phone line.</summary>
public sealed record ContactWritePhone(int? Position, string Number, string Type, int? Pref = null);

/// <summary>A validated write-side postal address line.</summary>
public sealed record ContactWriteAddress(
    int? Position,
    string Type,
    string? PoBox,
    string? Extended,
    string? Street,
    string? Locality,
    string? Region,
    string? PostalCode,
    string? Country,
    int? Pref = null);

/// <summary>
/// What a validated request says about the photo. Two cases only: null is the third, and it is the
/// "the request did not name this field, the card keeps its own" that <see cref="ContactWrite"/>
/// documents for every field the editor does not own. A <c>Keep</c> case could not be an optional
/// argument's default anyway — that must be a compile-time constant, which no record instance is.
/// </summary>
public abstract record PhotoPayload
{
    private PhotoPayload() { }

    /// <summary>Every PHOTO line leaves the card, not just the first (decision 5).</summary>
    public sealed record Remove : PhotoPayload;

    /// <summary>Every PHOTO line leaves the card and this one takes their place.</summary>
    /// <param name="Bytes">The decoded image.</param>
    /// <param name="MediaType">What the sniff read of those bytes, never what the client claimed.</param>
    public sealed record Replace(byte[] Bytes, string MediaType) : PhotoPayload;
}

/// <summary>
/// A validated, normalised contact on its way to the store: names trimmed and nulled when blank,
/// child lines non-blank and in the order they must be stored. Only
/// <see cref="Services.ContactValidator"/> produces one, so the store never re-checks the rules.
/// <para>
/// Two conventions for <c>null</c> live here, and they are opposites. On the fields the editor
/// owns — the names, the display name, the addresses — <c>null</c> is the user emptying the box,
/// and the card follows. On everything else, <c>null</c> means the request did not name the field
/// at all and the card keeps its own; an empty string, or an empty list, is what clears it.
/// </para>
/// </summary>
/// <param name="FirstName">The contact's first name, if any.</param>
/// <param name="LastName">The contact's last name, if any.</param>
/// <param name="Nickname">The contact's nickname, if any.</param>
/// <param name="DisplayName">The contact's display name, if any.</param>
/// <param name="MiddleName">The contact's middle name, if any.</param>
/// <param name="NamePrefix">The contact's name prefix, if any.</param>
/// <param name="NameSuffix">The contact's name suffix, if any.</param>
/// <param name="Organization">The contact's organization, if any.</param>
/// <param name="Department">The contact's department, if any.</param>
/// <param name="JobTitle">The contact's job title, if any.</param>
/// <param name="Birthday">The contact's birthday, if any.</param>
/// <param name="Website">The contact's website, if any.</param>
/// <param name="Notes">The contact's notes, if any.</param>
/// <param name="IsFavorite">Whether the contact is starred.</param>
/// <param name="Addresses">Non-blank, in the order they must be stored.</param>
/// <param name="Phones">Null = the request did not name them, so the card keeps its own; empty = cleared.</param>
/// <param name="PostalAddresses">Null = the request did not name them, so the card keeps its own; empty = cleared.</param>
/// <param name="Source">Where the card came from.</param>
/// <param name="CardHash">
/// The hash the caller read the card at, if any. Null writes as every caller did before this
/// field existed — the import, scripts, and any caller that never read the card first. Non-null
/// and different from the stored hash refuses the write with <see cref="Repositories.ContactStore.CardMoved"/>.
/// </param>
/// <param name="Photo">
/// Null = the request did not name the photo, so the card keeps its own. The validator is the only
/// producer that ever poses it; the import and <c>WriteOf</c> keep the default.
/// </param>
public sealed record ContactWrite(
    string? FirstName,
    string? LastName,
    string? Nickname,
    string? DisplayName,
    string? MiddleName,
    string? NamePrefix,
    string? NameSuffix,
    string? Organization,
    string? Department,
    string? JobTitle,
    string? Birthday,
    string? Website,
    string? Notes,
    bool IsFavorite,
    IReadOnlyList<ContactWriteEmail> Addresses,
    IReadOnlyList<ContactWritePhone>? Phones,
    IReadOnlyList<ContactWriteAddress>? PostalAddresses,
    string Source,
    string? CardHash = null,
    PhotoPayload? Photo = null);
