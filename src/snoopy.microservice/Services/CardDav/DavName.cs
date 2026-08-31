namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// What a client may call one of its cards. The route captures the segment whole — suffix
/// included — and this is the only judge: a route pattern demanding ".vcf" would contradict
/// decision 5 in silence, refusing a name by a routing 404 rather than by a considered answer.
/// </summary>
internal static class DavName
{
    private const int MaxLength = 255;

    internal static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength) return false;
        if (name is "." or "..") return false;
        // Edge spaces, because utf8mb4_bin is PAD SPACE: two names differing only by a trailing
        // space collide on the unique index while being two distinct URLs for every HTTP client.
        if (name[0] == ' ' || name[^1] == ' ') return false;

        foreach (var c in name)
        {
            if (c is '/' or '\\') return false;
            if (c <= '\u001F' || c == '\u007F') return false;
        }

        return true;
    }

    /// <summary>
    /// The name a card born in the webmail carries. A convention, not a constraint: a card a
    /// client PUTs keeps whatever segment it chose, and <see cref="IsValid"/> is the only judge.
    /// </summary>
    internal static string ForContact(Guid contactId) => $"{contactId}.vcf";
}
