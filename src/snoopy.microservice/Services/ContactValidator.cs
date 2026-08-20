using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Contacts;

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

    private static readonly string[] KnownSources = ["manual", "captured", "imported"];

    // Since params no longer enters the API (decision: it never did, and never will), TYPE is the
    // one parameter fragment still reaching the card. Letters, digits, dash and comma, bounded —
    // a ';', ':' or CR is the same injection vector params being closed already shut. \A/\z, not
    // ^/$: in .NET, $ also matches immediately before a trailing '\n', so "HOME\n" would pass a
    // ^...$ gate — and this method is a named part of the validator's surface precisely so later
    // callers can run it on a value they never trimmed themselves.
    private static readonly Regex TypeToken = new($@"\A[A-Za-z0-9,-]{{0,{MaxTypeLength}}}\z", RegexOptions.Compiled);

    internal static Result<ContactWrite> Validate(ContactRequest request)
    {
        if (request == null) return Result.Failure<ContactWrite>("Request body is required");

        var first = Blank(request.FirstName);
        var last = Blank(request.LastName);
        var nick = Blank(request.Nickname);
        var displayName = Blank(request.DisplayName);
        var middleName = Blank(request.MiddleName);
        var namePrefix = Blank(request.NamePrefix);
        var nameSuffix = Blank(request.NameSuffix);
        var organization = Blank(request.Organization);
        var department = Blank(request.Department);
        var jobTitle = Blank(request.JobTitle);
        var birthday = Blank(request.Birthday);
        var website = Blank(request.Website);
        var notes = Blank(request.Notes);

        var addresses = (request.Addresses ?? [])
            .Where(a => a != null)
            .Select(a => new ContactWriteEmail(
                a.Position, (a.Address ?? string.Empty).Trim(), (a.Type ?? string.Empty).Trim()))
            .Where(IsMeaningful)
            .ToList();

        var phones = (request.Phones ?? [])
            .Where(p => p != null)
            .Select(p => new ContactWritePhone(
                p.Position, (p.Number ?? string.Empty).Trim(), (p.Type ?? string.Empty).Trim()))
            .Where(IsMeaningful)
            .ToList();

        var postalAddresses = (request.PostalAddresses ?? [])
            .Where(a => a != null)
            .Select(a => new ContactWriteAddress(
                a.Position, (a.Type ?? string.Empty).Trim(),
                Blank(a.PoBox), Blank(a.Extended), Blank(a.Street),
                Blank(a.Locality), Blank(a.Region), Blank(a.PostalCode), Blank(a.Country)))
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
        if (phones.Count > MaxPhonesPerContact)
            return Result.Failure<ContactWrite>(
                $"A contact cannot carry more than {MaxPhonesPerContact} phone numbers");
        if (postalAddresses.Count > MaxPostalAddressesPerContact)
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
        }

        foreach (var phone in phones)
        {
            if (phone.Number.Length > MaxPhoneNumberLength)
                return Result.Failure<ContactWrite>($"A phone number must be at most {MaxPhoneNumberLength} characters");
            if (!IsValidTypeToken(phone.Type))
                return Result.Failure<ContactWrite>($"'{phone.Type}' is not a valid type");
        }

        foreach (var postal in postalAddresses)
        {
            if (!IsValidTypeToken(postal.Type))
                return Result.Failure<ContactWrite>($"'{postal.Type}' is not a valid type");
        }

        return Result.Success(new ContactWrite(
            first, last, nick, displayName, middleName, namePrefix, nameSuffix,
            organization, department, jobTitle, birthday, website, notes,
            request.IsFavorite, addresses, phones, postalAddresses, Source(request.Source)));
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
}
