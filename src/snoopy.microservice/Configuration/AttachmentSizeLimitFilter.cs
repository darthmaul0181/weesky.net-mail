using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Configuration;

/// <summary>
/// Caps the request body of the attachment upload at the configured message size.
///
/// The store enforces <see cref="MailOptions.MaxMessageSizeMb"/> while streaming, but that check
/// runs inside the action — and <c>IFormFile</c> model binding has already buffered the whole body
/// (to disk past 64 KB) by then. The effective ceiling was therefore the framework's 128 MB
/// multipart default, not the configured 25 MB, and a rejected upload still cost the disk write.
/// A resource filter runs before model binding, so the cap applies to the read itself.
///
/// It is a filter rather than <c>[RequestSizeLimit]</c> because the limit is configuration, and an
/// attribute argument must be a constant.
/// </summary>
internal sealed class AttachmentSizeLimitFilter(IOptionsMonitor<MailOptions> options) : IResourceFilter
{
    /// <summary>Multipart headers and boundaries ride along with the file's own bytes.</summary>
    private const long EnvelopeOverheadBytes = 1024 * 1024;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is not { IsReadOnly: false }) return;

        feature.MaxRequestBodySize =
            (long)options.CurrentValue.MaxMessageSizeMb * 1024 * 1024 + EnvelopeOverheadBytes;
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
