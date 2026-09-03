using System.Buffers.Text;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Contacts;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The single place the contact rules are written — the role <see cref="IdentityResolver"/> plays
/// for sending identities. Pure, no external call, so POST and PUT read the same rule instead of
/// two that could drift apart.
/// </summary>
internal static class ContactValidator
{
    /// <summary>
    /// What bounds one contact's address list. The whole book is fetched into the browser, so a
    /// contact carrying thousands of addresses is a payload problem, not just an odd fixture.
    /// </summary>
    internal const int MaxAddressesPerContact = 50;

    internal const int MaxPhonesPerContact = 10;

    internal const int MaxPostalAddressesPerContact = 10;

    /// <summary>
    /// The column widths, mirrored: <c>contacts.first_name</c>, <c>last_name</c> and
    /// <c>nickname</c> are VARCHAR(100), <c>contact_emails.address</c> VARCHAR(320). Unbounded
    /// here, an over-long value reaches a strict-mode MariaDB and comes back as a 500.
    /// </summary>
    internal const int MaxNameLength = 100;

    internal const int MaxAddressLength = 320;

    internal const int MaxMiddleNameLength = 100;

    internal const int MaxNamePartLength = 50;

    internal const int MaxOrganizationLength = 255;

    internal const int MaxBirthdayLength = 64;

    internal const int MaxPhoneNumberLength = 64;

    internal const int MaxTypeLength = 64;

    internal const int MaxWebsiteLength = 512;

    /// <summary>
    /// <c>contacts.notes</c> is TEXT — 65 535 <em>bytes</em>. The ceiling is counted in characters,
    /// so it is set low enough that even a card entirely made of 4-byte code points still fits.
    /// </summary>
    internal const int MaxNotesLength = 16000;

    /// <summary><c>contacts.display_name</c> is VARCHAR(255) — a group is its FN, and nothing else.</summary>
    internal const int MaxGroupNameLength = 255;

    /// <summary>
    /// The photo's ceiling, in decoded bytes — what the browser's reducer produces at worst
    /// (décision 8). <see cref="Repositories.ContactStore.MaxCardBytes"/> stays sovereign above it.
    /// </summary>
    internal const int MaxPhotoBytes = 512 * 1024;

    internal const string PhotoNotBase64 = "The photo is not valid base64";

    internal const string PhotoNotRaster = "The photo is not a JPEG, PNG, GIF or WebP image";

    internal static readonly string PhotoTooLarge = $"The photo exceeds {MaxPhotoBytes / 1024} KB";

    private const string PrefOutOfRangeMessage = "A preference must be between 1 and 101";

    private static readonly string[] KnownSources = ["manual", "captured", "imported"];

    // Since params no longer enters the API (decision: it never did, and never will), TYPE is the
    // one parameter fragment still reaching the card. Letters, digits, dash and comma, bounded —
    // a ';', ':' or CR is the same injection vector params being closed already shut. \A/\z, not
    // ^/$: in .NET, $ also matches immediately before a trailing '\n', so "HOME\n" would pass a
    // ^...$ gate — and this method is a named part of the validator's surface precisely so later
    // callers can run it on a value they never trimmed themselves.
    private static readonly Regex TypeToken = new($@"\A[A-Za-z0-9,-]{{0,{MaxTypeLength}}}\z", RegexOptions.Compiled);

    /// <summary>
    /// The group-name rule, one place: trimmed; refused empty or over the column. A name the
    /// projection reads back as the writer's placeholder is refused with the empty one — accepted,
    /// it would answer 200 with the name and list the group without it.
    /// </summary>
    internal static Result<string> ValidateGroupName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || VCardProjector.IsPlaceholder(trimmed))
            return Result.Failure<string>("A group needs a name");
        if (trimmed.Length > MaxGroupNameLength)
            return Result.Failure<string>($"The group name must be at most {MaxGroupNameLength} characters");
        return Result.Success(trimmed);
    }

    internal static Result<ContactWrite> Validate(ContactRequest request)
    {
        if (request == null) return Result.Failure<ContactWrite>("Request body is required");

        var first = Blank(request.FirstName);
        var last = Blank(request.LastName);
        var nick = Blank(request.Nickname);
        var displayName = Blank(request.DisplayName);
        var middleName = Given(request.MiddleName);
        var namePrefix = Given(request.NamePrefix);
        var nameSuffix = Given(request.NameSuffix);
        var organization = Given(request.Organization);
        var department = Given(request.Department);
        var jobTitle = Given(request.JobTitle);
        var birthday = Given(request.Birthday);
        var website = Given(request.Website);
        var notes = Given(request.Notes);

        var addresses = (request.Addresses ?? [])
            .Where(a => a != null)
            .Select(a => new ContactWriteEmail(
                a.Position, (a.Address ?? string.Empty).Trim(), (a.Type ?? string.Empty).Trim(), a.Pref))
            .Where(IsMeaningful)
            .ToList();

        // Null travels through: it is a request that does not name the family, and the composer
        // then keeps the card's own. An empty list is still a clearing, an explicit one.
        var phones = request.Phones?
            .Where(p => p != null)
            .Select(p => new ContactWritePhone(
                p.Position, (p.Number ?? string.Empty).Trim(), (p.Type ?? string.Empty).Trim(), p.Pref))
            .Where(IsMeaningful)
            .ToList();

        var postalAddresses = request.PostalAddresses?
            .Where(a => a != null)
            .Select(a => new ContactWriteAddress(
                a.Position, (a.Type ?? string.Empty).Trim(),
                Blank(a.PoBox), Blank(a.Extended), Blank(a.Street),
                Blank(a.Locality), Blank(a.Region), Blank(a.PostalCode), Blank(a.Country), a.Pref))
            .Where(IsMeaningful)
            .ToList();

        if (first == null && last == null && nick == null && addresses.Count == 0)
            return Result.Failure<ContactWrite>(
                "A contact needs a first name, last name or nickname, or at least one address");

        var overLong = TooLong(first, "first name", MaxNameLength)
            ?? TooLong(last, "last name", MaxNameLength)
            ?? TooLong(nick, "nickname", MaxNameLength)
            ?? TooLong(middleName, "middle name", MaxMiddleNameLength)
            ?? TooLong(namePrefix, "name prefix", MaxNamePartLength)
            ?? TooLong(nameSuffix, "name suffix", MaxNamePartLength)
            ?? TooLong(organization, "organization", MaxOrganizationLength)
            ?? TooLong(department, "department", MaxOrganizationLength)
            ?? TooLong(jobTitle, "job title", MaxOrganizationLength)
            ?? TooLong(birthday, "birthday", MaxBirthdayLength)
            ?? TooLong(website, "website", MaxWebsiteLength)
            ?? TooLong(notes, "notes", MaxNotesLength);
        if (overLong != null) return Result.Failure<ContactWrite>(overLong);

        if (addresses.Count > MaxAddressesPerContact)
            return Result.Failure<ContactWrite>(
                $"A contact cannot carry more than {MaxAddressesPerContact} addresses");
        if (phones?.Count > MaxPhonesPerContact)
            return Result.Failure<ContactWrite>(
                $"A contact cannot carry more than {MaxPhonesPerContact} phone numbers");
        if (postalAddresses?.Count > MaxPostalAddressesPerContact)
            return Result.Failure<ContactWrite>(
                $"A contact cannot carry more than {MaxPostalAddressesPerContact} postal addresses");

        foreach (var address in addresses)
        {
            if (address.Address.Length > MaxAddressLength)
                return Result.Failure<ContactWrite>($"An address must be at most {MaxAddressLength} characters");
            if (!IsValidAddress(address.Address))
                return Result.Failure<ContactWrite>($"'{address.Address}' is not a valid email address");
            if (!IsValidTypeToken(address.Type))
                return Result.Failure<ContactWrite>($"'{address.Type}' is not a valid type");
            if (!IsValidPref(address.Pref))
                return Result.Failure<ContactWrite>(PrefOutOfRangeMessage);
        }

        foreach (var phone in phones ?? [])
        {
            if (phone.Number.Length > MaxPhoneNumberLength)
                return Result.Failure<ContactWrite>($"A phone number must be at most {MaxPhoneNumberLength} characters");
            if (!IsValidTypeToken(phone.Type))
                return Result.Failure<ContactWrite>($"'{phone.Type}' is not a valid type");
            if (!IsValidPref(phone.Pref))
                return Result.Failure<ContactWrite>(PrefOutOfRangeMessage);
        }

        foreach (var postal in postalAddresses ?? [])
        {
            if (!IsValidTypeToken(postal.Type))
                return Result.Failure<ContactWrite>($"'{postal.Type}' is not a valid type");
            if (!IsValidPref(postal.Pref))
                return Result.Failure<ContactWrite>(PrefOutOfRangeMessage);
        }

        // Last, because it is the only check that reads hundreds of kilobytes.
        var photoError = PhotoOf(request.Photo, out var photo);
        if (photoError != null) return Result.Failure<ContactWrite>(photoError);

        return Result.Success(new ContactWrite(
            first, last, nick, displayName, middleName, namePrefix, nameSuffix,
            organization, department, jobTitle, birthday, website, notes,
            request.IsFavorite, addresses, phones, postalAddresses, Source(request.Source),
            request.CardHash, photo));
    }

    /// <summary>
    /// The photo's refusal message, null when the payload is good — <paramref name="photo"/> is
    /// only meaningful then. Validity and decoded size are read by <c>Base64.IsValid</c>, which
    /// allocates nothing and tolerates the whitespace <c>FromBase64String</c> tolerates; the string
    /// it reads is already bounded by the route's request size limit. No length pre-guard: it would
    /// refuse a wrapped base64 the ceiling admits, and the length cannot decide anyway — 512 KB and
    /// 512 KB + 1 encode to the same 699 052 characters (décision 4). The decode comes last.
    /// <para>
    /// Not passed through <see cref="Given"/>: whitespace is legal base64 filler, and only the
    /// exactly empty string means "remove it".
    /// </para>
    /// </summary>
    private static string? PhotoOf(string? value, out PhotoPayload? photo)
    {
        photo = null;
        if (value == null) return null;
        if (value.Length == 0)
        {
            photo = new PhotoPayload.Remove();
            return null;
        }

        if (!Base64.IsValid(value.AsSpan(), out var decodedLength)) return PhotoNotBase64;
        if (decodedLength > MaxPhotoBytes) return PhotoTooLarge;

        var bytes = Convert.FromBase64String(value);
        if (VCardProjector.SniffRasterType(bytes) is not { } mediaType) return PhotoNotRaster;

        photo = new PhotoPayload.Replace(bytes, mediaType);
        return null;
    }

    /// <summary>
    /// Whether a line says anything. Blank rows are what an editor leaves behind when the user
    /// opens a line and changes their mind; they are dropped, never refused. Exposed so that every
    /// producer of a <see cref="ContactWrite"/> — the import reader as much as this validator —
    /// filters by the same rule, and the promise that only validated lines reach the composer
    /// holds line for line.
    /// </summary>
    internal static bool IsMeaningful(ContactWriteEmail line) => line.Address.Length > 0;

    internal static bool IsMeaningful(ContactWritePhone line) => line.Number.Length > 0;

    internal static bool IsMeaningful(ContactWriteAddress line) =>
        line.Type.Length > 0 || line.PoBox != null || line.Extended != null || line.Street != null
        || line.Locality != null || line.Region != null || line.PostalCode != null
        || line.Country != null;

    private static string? TooLong(string? value, string field, int limit) =>
        value?.Length > limit ? $"The {field} must be at most {limit} characters" : null;

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// <see cref="Blank"/>, except that absent and empty stop being the same thing: null when the
    /// request does not carry the field — the composer then keeps the card's own — and the empty
    /// string when it carries it empty, which is a clearing the caller asked for. The distinction
    /// only means something on the fields no screen writes yet; for the first name / last name /
    /// nickname trio, null is the user who emptied the box, and <see cref="Blank"/> reads it right.
    /// </summary>
    private static string? Given(string? value) => value?.Trim();

    private static string Source(string? raw) =>
        raw != null && KnownSources.Contains(raw, StringComparer.Ordinal) ? raw : "manual";

    // MimeKit is the authority here as it is on the send path: a hand-rolled regex accepts and
    // rejects a different set than the library that will actually address the mail. Parsed with
    // RecipientAddressParser.Options — the shared policy every address field uses — because the
    // default options accept a bare local part with no domain (see its own doc comment).
    internal static bool IsValidAddress(string address) =>
        MailboxAddress.TryParse(RecipientAddressParser.Options, address, out var parsed) &&
        parsed.Address == address;

    /// <summary>
    /// The gabarit a TYPE fragment must fit: ASCII letters, digits, dash and comma, at most
    /// <see cref="MaxTypeLength"/> characters. Empty is accepted — no type at all.
    /// </summary>
    internal static bool IsValidTypeToken(string value) => TypeToken.IsMatch(value);

    /// <summary>Null (the write does not name it) or 1–101 — 101 being the composer's erasure.</summary>
    internal static bool IsValidPref(int? value) => value is null or (>= 1 and <= 101);
}
