using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The one place the identity list is derived from its three sources — stored rows, the primary
/// address, the live alias list. GET /api/Identities and MailSender both call it, so the rule
/// cannot drift between display and send.
/// </summary>
internal static class IdentityResolver
{
    internal const int MaxDisplayNameLength = 100;

    internal static string Canonical(string address) => address.Trim().ToLowerInvariant();

    /// <summary>Canonicalises an alias list once, for callers that test many addresses against it.</summary>
    internal static IReadOnlySet<string> CanonicalSet(IEnumerable<string> addresses) =>
        addresses.Select(Canonical).ToHashSet();

    /// <summary>
    /// Whether the account may send as this address: its primary, or one of its live aliases.
    /// The stored identity rows are deliberately not consulted — a stale row owns nothing.
    /// Canonicalises the alias list itself; the name differs from <see cref="OwnsCanonical"/> so no
    /// call site can skip that by handing over a set.
    /// </summary>
    internal static bool Owns(IEnumerable<string> aliasAddresses, string primaryAddress, string address) =>
        OwnsCanonical(CanonicalSet(aliasAddresses), primaryAddress, address);

    /// <summary>The same rule over an already-canonical set; accounts carry 100+ aliases, so a
    /// caller testing many addresses builds the set once with <see cref="CanonicalSet"/>.</summary>
    internal static bool OwnsCanonical(IReadOnlySet<string> canonicalAliases, string primaryAddress, string address)
    {
        var canonical = Canonical(address);
        return canonical == Canonical(primaryAddress) || canonicalAliases.Contains(canonical);
    }

    internal static IReadOnlyList<SendingIdentityInfo> Resolve(
        IReadOnlyList<SendingIdentity> stored, string primaryAddress, string? fullName,
        IReadOnlyCollection<string> aliasAddresses)
    {
        var primary = Canonical(primaryAddress);
        var owned = CanonicalSet(aliasAddresses);

        var primaryRow = stored.FirstOrDefault(r => Canonical(r.Address) == primary);
        var list = new List<SendingIdentityInfo>
        {
            new(primary, LabelFor(stored, primary, fullName), IsDefault: false,
                IsPrimary: true, Stale: false, LabelIsCustom: primaryRow != null),
        };

        foreach (var row in stored)
        {
            var address = Canonical(row.Address);
            if (address == primary) continue;
            list.Add(new SendingIdentityInfo(address, row.DisplayName, IsDefault: false,
                IsPrimary: false, Stale: !OwnsCanonical(owned, primary, address), LabelIsCustom: true));
        }

        // A stale row cannot hold the default; with no live marked row it falls back to the primary.
        var marked = stored.FirstOrDefault(r => r.IsDefault && OwnsCanonical(owned, primary, r.Address));
        var defaultAddress = marked == null ? primary : Canonical(marked.Address);

        return list
            .Select(i => i with { IsDefault = i.Address == defaultAddress })
            .OrderByDescending(i => i.IsDefault)
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Label precedence: the stored row, then the account's FullName, then the address.</summary>
    internal static string LabelFor(IReadOnlyList<SendingIdentity> stored, string address, string? fullName)
    {
        var canonical = Canonical(address);
        var row = stored.FirstOrDefault(r => Canonical(r.Address) == canonical);
        if (row != null) return row.DisplayName;
        return string.IsNullOrWhiteSpace(fullName) ? canonical : fullName;
    }

    internal static Result<IReadOnlyList<SendingIdentity>> Validate(
        IReadOnlyList<IdentityEntry> entries, string primaryAddress,
        IReadOnlyCollection<string> aliasAddresses, IReadOnlyCollection<string> storedAddresses)
    {
        // Stored addresses stay acceptable so a stale identity survives a save — the "never
        // silently deleted" rule — while a NEW unknown address still cannot enter.
        var allowed = aliasAddresses.Select(Canonical)
            .Concat(storedAddresses.Select(Canonical))
            .Append(Canonical(primaryAddress))
            .ToHashSet();

        var seen = new HashSet<string>();
        var defaults = 0;
        var rows = new List<SendingIdentity>();
        foreach (var entry in entries)
        {
            // The field is a bare address, DisplayName is separate — a decorated "Name <a@b.c>"
            // is a format error here, not silently unwrapped the way Send's fromAddress is.
            if (string.IsNullOrWhiteSpace(entry.Address) ||
                !MailboxAddress.TryParse(RecipientAddressParser.Options, entry.Address, out var mailbox) ||
                !string.Equals(mailbox.Address, entry.Address.Trim(), StringComparison.OrdinalIgnoreCase))
                return Fail($"\"{entry.Address}\" is not a valid email address");

            var address = Canonical(mailbox.Address);
            if (!allowed.Contains(address)) return Fail($"\"{entry.Address}\" is not one of your addresses");
            if (!seen.Add(address)) return Fail($"\"{entry.Address}\" appears twice");

            var name = entry.DisplayName?.Trim() ?? string.Empty;
            if (name.Length is < 1 or > MaxDisplayNameLength)
                return Fail($"The display name for \"{entry.Address}\" must be 1 to {MaxDisplayNameLength} characters");
            if (name.Contains('\r') || name.Contains('\n'))
                return Fail($"The display name for \"{entry.Address}\" must not contain line breaks");

            if (entry.IsDefault && ++defaults > 1) return Fail("Only one identity can be the default");

            rows.Add(new SendingIdentity { Address = address, DisplayName = name, IsDefault = entry.IsDefault });
        }
        return Result.Success<IReadOnlyList<SendingIdentity>>(rows);

        static Result<IReadOnlyList<SendingIdentity>> Fail(string error) =>
            Result.Failure<IReadOnlyList<SendingIdentity>>(error);
    }
}
