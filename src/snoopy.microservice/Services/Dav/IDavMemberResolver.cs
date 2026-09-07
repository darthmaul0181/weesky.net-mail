using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>What <see cref="IDavMemberSource{TMember}.Prepare"/> read of the body, ready to be
/// applied to each member — it never sees the body again.</summary>
internal interface IDavMemberResolver<TMember>
{
    (List<XElement> Found, List<XName> Missing) Resolve(DavPropertyRequest request, TMember member);
}
