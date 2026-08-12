using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// Pins the public HTTP surface of api/Mail by reflection. Moving an action between controller
/// classes must not move its URL, verb, declared statuses, [Authorize] or filters — the app
/// still builds and direct-invocation tests still pass when one silently changes, so this is
/// the only net under a controller split.
/// </summary>
public sealed class MailRouteSurfaceTests
{
    private static IReadOnlyList<ControllerRouteSurface.Action> MailSurface() =>
        [.. ControllerRouteSurface
            .Of(ControllerRouteSurface.ControllersOf(typeof(ApiBaseController).Assembly))
            .Where(a => a.Prefix == "api/Mail")];

    /// <summary>The historical surface of MailController, verb by verb. Changing an entry here
    /// is an API break for every deployed client; additions belong at the end.</summary>
    private static readonly Dictionary<string, int[]> ExpectedSurface = new()
    {
        ["GET api/Mail/Folders"] = [200, 401, 404, 409, 502],
        ["POST api/Mail/Folders"] = [200, 400, 401, 404, 409, 502],
        ["PUT api/Mail/Folders"] = [200, 400, 401, 404, 409, 502],
        ["DELETE api/Mail/Folders"] = [204, 400, 401, 404, 409, 502],
        ["PUT api/Mail/Folders/Subscription"] = [204, 400, 401, 404, 409, 502],
        ["POST api/Mail/Folders/Empty"] = [204, 400, 401, 404, 409, 502],
        ["GET api/Mail/FolderRoles"] = [200, 401, 404, 409, 502],
        ["PUT api/Mail/FolderRoles"] = [204, 400, 401, 404, 409, 502],
        ["DELETE api/Mail/FolderRoles"] = [204, 400, 401, 404, 409],
        ["GET api/Mail/Messages"] = [200, 400, 401, 404, 409, 502],
        ["GET api/Mail/Messages/Detail"] = [200, 400, 401, 404, 409, 502],
        ["GET api/Mail/Messages/Source"] = [200, 400, 401, 404, 409, 502],
        ["GET api/Mail/Messages/Attachment"] = [200, 400, 401, 404, 409, 502],
        ["PUT api/Mail/Messages/Flags"] = [204, 400, 401, 404, 409, 502],
        ["POST api/Mail/Messages/Move"] = [204, 400, 401, 404, 409, 502],
        ["POST api/Mail/Messages/Copy"] = [204, 400, 401, 404, 409, 502],
        ["DELETE api/Mail/Messages"] = [204, 400, 401, 404, 409, 502],
        ["POST api/Mail/Messages/Search"] = [200, 400, 401, 404, 409, 502],
        ["POST api/Mail/Messages/PrepareQuote"] = [200, 400, 401, 404, 409, 502],
        ["POST api/Mail/Attachments"] = [200, 400, 401, 404, 409],
        ["DELETE api/Mail/Attachments/{id:guid}"] = [204, 401, 404, 409],
        ["GET api/Mail/Attachments/{id:guid}/content"] = [200, 401, 404, 409],
        ["POST api/Mail/Send"] = [200, 400, 401, 404, 409, 502],
        ["POST api/Mail/Drafts"] = [200, 400, 401, 404, 409, 502],
        ["POST api/Mail/Drafts/Open"] = [200, 400, 401, 404, 409, 502],
    };

    [Fact]
    public void The_surface_is_exactly_the_historical_route_set()
    {
        var actual = MailSurface().Select(a => $"{a.Verb} {a.Route}").OrderBy(r => r, StringComparer.Ordinal).ToList();
        var expected = ExpectedSurface.Keys.OrderBy(r => r, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_action_declares_the_same_statuses_as_before()
    {
        foreach (var (_, action, verb, _, route) in MailSurface())
        {
            var declared = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(a => a.StatusCode).OrderBy(c => c).ToArray();

            Assert.Equal(ExpectedSurface[$"{verb} {route}"], declared);
        }
    }

    [Fact]
    public void Every_mail_controller_keeps_authorize_and_apicontroller()
    {
        var controllers = MailSurface().Select(a => a.Controller).Distinct().ToList();

        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        {
            Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true));
            Assert.NotNull(controller.GetCustomAttribute<ApiControllerAttribute>(inherit: true));
        }
    }

    [Fact]
    public void Only_the_upload_carries_a_service_filter_and_nothing_carries_a_size_limit()
    {
        var filters = MailSurface()
            .SelectMany(a => a.Method.GetCustomAttributes<ServiceFilterAttribute>()
                .Select(f => $"{a.Verb} {a.Route} -> {f.ServiceType.Name}"))
            .ToList();

        Assert.Equal(["POST api/Mail/Attachments -> AttachmentSizeLimitFilter"], filters);
        Assert.All(MailSurface(), a => Assert.Empty(a.Method.GetCustomAttributes<RequestSizeLimitAttribute>()));
    }
}
