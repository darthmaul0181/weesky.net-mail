using System.Text;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// The collations the two protocols announce. They are three comparisons, not one:
/// i;ascii-casemap folds only A–Z (RFC 4790 § 9.2.1), so « É » and « é » differ under it, while
/// i;unicode-casemap folds and decomposes all of Unicode (RFC 5051), and i;octet folds nothing at
/// all. A single case-insensitive comparison would lie for one of them on every accented letter.
/// </summary>
internal static class DavCollation
{
    internal const string AsciiCasemap = "i;ascii-casemap";
    internal const string UnicodeCasemap = "i;unicode-casemap";

    /// <summary>The byte-for-byte comparison RFC 4791 § 7.5.1 makes mandatory of a calendar
    /// collection, where RFC 6352 § 8.3 asks a book for i;unicode-casemap instead.</summary>
    internal const string Octet = "i;octet";

    /// <summary>One comparer per collation, cited by the sets — never a second instance.</summary>
    internal static readonly DavCollationComparer Ascii = new(AsciiFolded);

    internal static readonly DavCollationComparer Unicode = new(UnicodeFolded);
    internal static readonly DavCollationComparer Ordinal = new(static value => value);

    /// <summary>The book's resolution, unchanged: <c>Resolve(attribute, DavCollationSet.CardDav)</c>.</summary>
    internal static DavCollationComparer Resolve(string? attribute) =>
        Resolve(attribute, DavCollationSet.CardDav);

    /// <summary>
    /// The comparison an attribute names within one protocol's set — names compare
    /// case-insensitively (RFC 4790 § 3.1). An absent attribute and the literal <c>default</c>
    /// both answer the set's default (i;unicode-casemap on a book, RFC 6352 § 8.3; i;ascii-casemap
    /// on a calendar, RFC 4791 § 9.7.5): <c>default</c> fallen into « unknown collation » would be
    /// a guaranteed wrongful refusal on a conforming attribute. Throws
    /// <see cref="DavPreconditionException"/> with the set's own <c>supported-collation</c> —
    /// never <c>supported-filter</c> — on anything else: the client must know whether its filter
    /// or its collation is at fault.
    /// </summary>
    internal static DavCollationComparer Resolve(string? attribute, DavCollationSet set)
    {
        if (attribute is null || attribute.Equals("default", StringComparison.OrdinalIgnoreCase))
            return set.Default;
        return set.Accepted.TryGetValue(attribute, out var comparer)
            ? comparer
            : throw new DavPreconditionException(set.Refusal);
    }

    private static string AsciiFolded(string value) =>
        string.Create(value.Length, value, static (folded, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                folded[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
        });

    // What sabre (mb_strtoupper) and Radicale (lower) do too. RFC 5051's own fold is titlecase
    // plus recursive decomposition (≈ NFKD), under which "jose" would match "josé" in contains
    // and starts-with; this fold does not, and that is where it diverges from the letter.
    private static string UnicodeFolded(string value) =>
        value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
}
