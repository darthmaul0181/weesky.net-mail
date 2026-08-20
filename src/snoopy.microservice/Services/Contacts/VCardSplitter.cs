namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// One card of a <c>.vcf</c> file: <see cref="Line"/> is the 1-based line of its
/// <c>BEGIN:VCARD</c>, what the import report cites; <see cref="Text"/> its own bytes.
/// </summary>
public sealed record VCardChunk(int Line, string Text);

/// <summary>
/// A file cut into cards, on the <c>BEGIN</c>/<c>END:VCARD</c> boundaries and nothing else. It
/// parses none of it: <c>Vcf.Parse</c> gives no access to a card's source text, so a file read in
/// one go could only be stored re-serialised — an ETag false from the first sync (décision 1).
/// The cut is made on the input's own offsets, which is what makes every chunk verbatim, its
/// weight measurable against the 1 MB ceiling and its line citable in the report.
/// </summary>
internal static class VCardSplitter
{
    private const string Begin = "BEGIN:VCARD";
    private const string End = "END:VCARD";

    /// <summary>
    /// One card per chunk, guaranteed: a <c>BEGIN</c> inside an unterminated card closes it rather
    /// than nesting. The projector and the composer both stop at the first <c>END:VCARD</c>, so a
    /// chunk carrying two cards would silently merge them.
    /// </summary>
    internal static IReadOnlyList<VCardChunk> Split(string fileText)
    {
        var chunks = new List<VCardChunk>();
        var line = 1;
        var start = -1;
        var startLine = 0;

        for (var i = 0; i < fileText.Length; line++)
        {
            var (end, next) = LineAt(fileText, i);
            var content = fileText.AsSpan(i, end - i);
            if (Is(content, Begin))
            {
                if (start >= 0) chunks.Add(new VCardChunk(startLine, fileText[start..i]));
                (start, startLine) = (i, line);
            }
            else if (Is(content, End) && start >= 0)
            {
                chunks.Add(new VCardChunk(startLine, fileText[start..next]));
                start = -1;
            }

            i = next;
        }

        // Tolerance, not silence: a card with no END is still a chunk, and the reader decides.
        if (start >= 0) chunks.Add(new VCardChunk(startLine, fileText[start..]));
        return chunks;
    }

    /// <summary>
    /// Whether the chunk closes on its own <c>END:VCARD</c>. A fragment is still a chunk — it is
    /// the reader's to refuse, with the line the user can go and read — but it is never a card:
    /// stored, it would be an invalid vCard served on the CardDAV route of 4c.
    /// </summary>
    internal static bool IsComplete(VCardChunk chunk)
    {
        for (var i = 0; i < chunk.Text.Length;)
        {
            var (end, next) = LineAt(chunk.Text, i);
            if (Is(chunk.Text.AsSpan(i, end - i), End)) return true;
            i = next;
        }

        return false;
    }

    // A leading space or tab is a folded continuation, never a boundary — a wrapped value may
    // spell one at the margin. Trailing blanks are tolerated, as a hand-edited file carries them.
    private static bool Is(ReadOnlySpan<char> content, string boundary) =>
        content.TrimEnd().Equals(boundary, StringComparison.OrdinalIgnoreCase);

    /// <summary>Where the line starting at <paramref name="from"/> ends, and where the next begins.</summary>
    private static (int End, int Next) LineAt(string text, int from)
    {
        var end = from;
        while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
        var next = end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n'
            ? end + 2 : Math.Min(end + 1, text.Length);
        return (end, next);
    }
}
