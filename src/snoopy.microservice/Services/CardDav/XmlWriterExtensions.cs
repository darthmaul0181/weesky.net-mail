using System.Xml;
using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>An <see cref="XElement"/> through an async <see cref="XmlWriter"/>, attributes and
/// children included — the one recursion every DAV writer of this surface shares.</summary>
internal static class XmlWriterExtensions
{
    internal static async Task WriteElementAsync(this XmlWriter writer, XElement element)
    {
        await writer.WriteStartElementAsync(null, element.Name.LocalName, element.Name.NamespaceName)
            .ConfigureAwait(false);

        foreach (var attribute in element.Attributes())
            await writer.WriteAttributeStringAsync(null, attribute.Name.LocalName,
                attribute.Name.NamespaceName, attribute.Value).ConfigureAwait(false);

        if (element.HasElements)
            foreach (var child in element.Elements())
                await writer.WriteElementAsync(child).ConfigureAwait(false);
        else if (!string.IsNullOrEmpty(element.Value))
            await writer.WriteStringAsync(element.Value).ConfigureAwait(false);

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }
}
