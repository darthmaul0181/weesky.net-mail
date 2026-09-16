using weesky.Scotty.Microservice.Models.Calendar;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Scotty.Microservice.Services.Calendar.Scheduling;

internal sealed record ScheduledMail(MailKind Kind, IReadOnlyList<string> Recipients);

/// <summary>The stored row before the write: its file (null on a creation) and its two
/// scheduling columns.</summary>
internal sealed record SchedulingBefore(string? Ics, string? Owner, string? Hash);

internal sealed record SchedulingInput(SchedulingBefore Before, string? After, WriteOrigin Origin, IReadOnlySet<string> OwnAddresses);

/// <summary>Who receives what, and the columns to write afterwards: Owner and Hash are always the
/// values to store, even with an empty Mails — a silent takeover changes them without mailing
/// anyone.</summary>
internal sealed record SchedulingDecision(IReadOnlyList<ScheduledMail> Mails, string? Owner, string? Hash);

/// <summary>The table of décision 9, and nothing else: no I/O, no clock, no mail. <c>Before.Ics</c>
/// and <c>After</c> are each parsed once here, through the <see cref="SchedulingShape"/>
/// overloads that take an <see cref="IcsCalendar"/> rather than text.</summary>
internal static class SchedulingDecider
{
    internal const string WebmailOwner = "webmail";

    internal static SchedulingDecision Decide(SchedulingInput input)
    {
        var before = input.Before;
        var keep = new SchedulingDecision([], before.Owner, before.Hash);
        var owned = before.Owner == WebmailOwner;
        var beforeParsed = before.Ics is null ? null : IcsDocument.TryLoad(before.Ics);
        var wasInvited = beforeParsed is null ? new HashSet<string>() : Guests(beforeParsed, input.OwnAddresses);
        var actualBeforeHash = beforeParsed is { } bp ? SchedulingShape.HashOf(SchedulingShape.Of(bp)) : null;

        if (input.After is null)
            return owned && wasInvited.Count > 0 ? Cancel(wasInvited) : keep;

        var afterParsed = IcsDocument.TryLoad(input.After);
        if (afterParsed is null) return keep;
        var shape = SchedulingShape.Of(afterParsed);
        var guests = Guests(afterParsed, input.OwnAddresses);
        var hash = SchedulingShape.HashOf(shape);

        if (!owned)
        {
            if (input.Origin != WriteOrigin.Webmail || guests.Count == 0) return keep;
            var organizer = SchedulingShape.OrganizerOf(afterParsed);
            if (organizer is null || !input.OwnAddresses.Contains(organizer)) return keep;
            if (wasInvited.Count == 0)
                return new SchedulingDecision([new ScheduledMail(MailKind.Invitation, Sorted(guests))], WebmailOwner, hash);

            // A silent takeover: the stored file stands in for the last version guests were told
            // about, so only what moves against it is mailed — never a blanket "Mise à jour".
            return Owned(beforeParsed, afterParsed, wasInvited, guests, hash, actualBeforeHash, actualBeforeHash, input.OwnAddresses);
        }

        return Owned(beforeParsed, afterParsed, wasInvited, guests, hash, before.Hash, actualBeforeHash, input.OwnAddresses);
    }

    private static SchedulingDecision Owned(
        IcsCalendar? beforeCalendar, IcsCalendar afterCalendar, IReadOnlySet<string> wasInvited, HashSet<string> guests,
        string hash, string? beforeHash, string? actualBeforeHash, IReadOnlySet<string> ownAddresses)
    {
        if (guests.Count == 0) return Cancel(wasInvited);
        if (hash == beforeHash) return new SchedulingDecision([], WebmailOwner, hash);

        var added = guests.Except(wasInvited).ToList();
        var removed = wasInvited.Except(guests).ToList();
        var kept = guests.Intersect(wasInvited).ToList();

        // Drift, a dated/moved/retitled component, or an override's own guest list moving while
        // the file-level set does not (added/removed are excluded: they already get their own
        // mail, and a plain addition must not also update everyone else). For a takeover,
        // actualBeforeHash IS beforeHash, so this first term is always false there.
        var otherChanged = true;
        if (beforeCalendar is { } beforeCal)
        {
            var unaffected = ownAddresses.Union(added).Union(removed).ToHashSet(StringComparer.Ordinal);
            otherChanged = actualBeforeHash != beforeHash
                || SchedulingShape.Of(beforeCal, withAttendees: false) != SchedulingShape.Of(afterCalendar, withAttendees: false)
                || GuestsMovedPerComponent(beforeCal, afterCalendar, unaffected);
        }

        var mails = new List<ScheduledMail>();
        if (added.Count > 0) mails.Add(new ScheduledMail(MailKind.Invitation, Sorted(added)));
        if (removed.Count > 0) mails.Add(new ScheduledMail(MailKind.Cancellation, Sorted(removed)));
        if (kept.Count > 0 && otherChanged) mails.Add(new ScheduledMail(MailKind.Update, Sorted(kept)));
        return new SchedulingDecision(mails, WebmailOwner, hash);
    }

    private static SchedulingDecision Cancel(IReadOnlySet<string> recipients) =>
        new(recipients.Count == 0 ? [] : [new ScheduledMail(MailKind.Cancellation, Sorted(recipients))], null, null);

    private static HashSet<string> Guests(IcsCalendar parsed, IReadOnlySet<string> own) =>
        SchedulingShape.Addresses(parsed).Where(a => !own.Contains(a)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether any component's own guest list moved, excluding <paramref name="own"/>, a
    /// component matched by its instance key — one present on only one side counts as moved.</summary>
    private static bool GuestsMovedPerComponent(IcsCalendar before, IcsCalendar after, IReadOnlySet<string> own)
    {
        var earlier = SchedulingShape.AddressesByComponent(before);
        var later = SchedulingShape.AddressesByComponent(after);
        foreach (var key in earlier.Keys.Union(later.Keys, StringComparer.Ordinal))
        {
            if (!earlier.TryGetValue(key, out var was) || !later.TryGetValue(key, out var now)) return true;
            if (!Excluding(was, own).SetEquals(Excluding(now, own))) return true;
        }
        return false;
    }

    private static HashSet<string> Excluding(IReadOnlySet<string> addresses, IReadOnlySet<string> own) =>
        addresses.Where(a => !own.Contains(a)).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<string> Sorted(IEnumerable<string> addresses) => [.. addresses.Order(StringComparer.Ordinal)];
}
