using System.Text;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The two collations supported-collation-set announces. They are two comparisons, not one:
/// i;ascii-casemap folds only A–Z (RFC 4790 § 9.2.1), so « É » and « é » differ under it, while
/// i;unicode-casemap folds and decomposes all of Unicode (RFC 5051). A single case-insensitive
/// comparison would lie for one of the two on every accented letter.
/// </summary>
internal static class DavCollation
{
    internal const string AsciiCasemap = "i;ascii-casemap";
    internal const string UnicodeCasemap = "i;unicode-casemap";

    private static readonly DavCollationComparer Ascii = new(AsciiFolded);
    private static readonly DavCollationComparer Unicode = new(UnicodeFolded);

    /// <summary>
    /// The comparison an attribute names — names compare case-insensitively (RFC 4790 § 3.1). An
    /// absent attribute and the literal <c>default</c> both mean i;unicode-casemap (RFC 6352
    /// § 8.3, a MUST): <c>default</c> fallen into « unknown collation » would be a guaranteed
    /// wrongful refusal on a conforming attribute. Throws <see cref="DavPreconditionException"/>
    /// (<c>supported-collation</c>, never <c>supported-filter</c>) on anything else — the client
    /// must know whether its filter or its collation is at fault.
    /// </summary>
    internal static DavCollationComparer Resolve(string? attribute)
    {
        if (attribute is null
            || attribute.Equals("default", StringComparison.OrdinalIgnoreCase)
            || attribute.Equals(UnicodeCasemap, StringComparison.OrdinalIgnoreCase))
            return Unicode;
        if (attribute.Equals(AsciiCasemap, StringComparison.OrdinalIgnoreCase)) return Ascii;
        throw new DavPreconditionException(DavXml.CardDav + "supported-collation");
    }

    private static string AsciiFolded(string value) =>
        string.Create(value.Length, value, static (folded, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                folded[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
        });

    // FormC, not FormD: composed first so the fold never scatters an ASCII base letter out of an
    // accented one, which is what keeps a needle's ASCII spelling comparable to a card's.
    private static string UnicodeFolded(string value) =>
        value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
}
