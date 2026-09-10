using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>The closed set of one resource against a request — a protocol's tables, handed to
/// the socle where it resolves on the protocol's behalf.</summary>
internal delegate (List<XElement> Found, List<XName> Missing) DavPropertyResolver(
    DavPropertyRequest request, DavResourceContext resource);
