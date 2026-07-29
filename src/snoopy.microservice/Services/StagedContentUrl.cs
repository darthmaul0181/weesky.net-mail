using System.Text.RegularExpressions;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The relative URL a staged inline image is served from: written by <see cref="QuotePreparer"/>
/// when it rewrites a cid for the composer, read back by <see cref="OutgoingMessageFactory"/> when
/// it turns it into a cid again. One place, so producer and consumer cannot drift apart.
/// </summary>
internal static class StagedContentUrl
{
    internal static string For(Guid id) => $"/api/Mail/Attachments/{id}/content";

    /// <summary>
    /// Rewrites every reference to <paramref name="id"/>'s content URL, with or without a query
    /// string, and reports whether the body referenced it at all.
    ///
    /// The query has to be tolerated: an &lt;img&gt; subresource cannot carry the X-Account-Id
    /// header, so the composer names the active account in "?account=..." instead, and matching the
    /// bare form alone would drop every inline image of a connected account from the sent message.
    /// It is consumed only up to the attribute delimiter, and the URL must end there — a match is
    /// never a prefix of a longer path.
    /// </summary>
    internal static bool TryRewrite(string html, Guid id, string replacement, out string rewritten)
    {
        var pattern = new Regex(
            $"""{Regex.Escape(For(id))}(\?[^"'\s<>]*)?(?=$|["'\s<>])""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!pattern.IsMatch(html))
        {
            rewritten = html;
            return false;
        }

        // A MatchEvaluator, not a replacement string: a Content-ID may legally carry '$'.
        rewritten = pattern.Replace(html, _ => replacement);
        return true;
    }
}
