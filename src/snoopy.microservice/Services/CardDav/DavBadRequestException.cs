namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// A REPORT/PROPFIND/PROPPATCH body that <see cref="DavXmlReader"/> refuses to parse — a DTD, an
/// entity, malformed XML, or nesting past <see cref="DavXmlReader.MaxDepth"/>. An empty body is
/// never this; it means allprop. A later layer translates it into a DAV error response, never a
/// 500: a 500 is what a client retries forever, on the same resource, every cycle.
/// </summary>
internal sealed class DavBadRequestException : Exception
{
    internal DavBadRequestException(string message) : base(message)
    {
    }

    internal DavBadRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
