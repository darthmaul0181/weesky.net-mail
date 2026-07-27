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

    /// <summary>
    /// The column widths, mirrored: <c>contacts.first_name</c>, <c>last_name</c> and
    /// <c>nickname</c> are VARCHAR(100), <c>contact_emails.address</c> VARCHAR(320). Unbounded
    /// here, an over-long value reaches a strict-mode MariaDB and comes back as a 500.
    /// </summary>
    internal const int MaxNameLength = 100;

    internal const int MaxAddressLength = 320;

    private static readonly string[] KnownSources = ["manual", "captured", "imported"];

    internal static Result<ContactWrite> Validate(ContactRequest request)
    {
        if (request == null) return Result.Failure<ContactWrite>("Request body is required");

        var first = Blank(request.FirstName);
        var last = Blank(request.LastName);
        var nick = Blank(request.Nickname);

        // Blank rows are what an editor leaves behind when the user opens an address line and
        // changes their mind; they are dropped, never refused.
        var addresses = (request.Addresses ?? [])
            .Select(a => a?.Trim() ?? string.Empty)
            .Where(a => a.Length > 0)
            .ToList();

        if (first == null && last == null && nick == null && addresses.Count == 0)
            return Result.Failure<ContactWrite>(
                "A contact needs a first name, last name or nickname, or at least one address");

        var overLong = TooLong(first, "first name") ?? TooLong(last, "last name") ?? TooLong(nick, "nickname");
        if (overLong != null) return Result.Failure<ContactWrite>(overLong);

        if (addresses.Count > MaxAddressesPerContact)
            return Result.Failure<ContactWrite>(
                $"A contact cannot carry more than {MaxAddressesPerContact} addresses");

        foreach (var address in addresses)
        {
            if (address.Length > MaxAddressLength)
                return Result.Failure<ContactWrite>(
                    $"An address must be at most {MaxAddressLength} characters");
            if (!Parses(address))
                return Result.Failure<ContactWrite>($"'{address}' is not a valid email address");
        }

        return Result.Success(new ContactWrite(first, last, nick, request.IsFavorite, addresses, Source(request.Source)));
    }

    private static string? TooLong(string? value, string field) =>
        value?.Length > MaxNameLength ? $"The {field} must be at most {MaxNameLength} characters" : null;

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
    private static bool Parses(string address) =>
        MailboxAddress.TryParse(RecipientAddressParser.Options, address, out var parsed) &&
        parsed.Address == address;
}
