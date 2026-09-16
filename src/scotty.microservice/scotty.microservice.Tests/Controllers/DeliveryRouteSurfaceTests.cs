using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using weesky.Scotty.Microservice.Authentication.Authorization;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

/// <summary>Pins the delivery door and its key endpoint: anonymous where it must be, admin-only
/// where it must be, and the three filters the door cannot lose without becoming a hole.</summary>
public sealed class DeliveryRouteSurfaceTests
{
    private static IReadOnlyList<ControllerRouteSurface.Action> Surface(string prefix) =>
        [.. ControllerRouteSurface.Of(ControllerRouteSurface.ControllersOf(typeof(ApiBaseController).Assembly))
            .Where(a => a.Prefix == prefix)];

    [Fact]
    public void The_door_is_one_anonymous_post_with_a_size_limit_and_the_delivery_policy()
    {
        var door = Assert.Single(Surface("api/Delivery"));
        Assert.Equal("POST api/Delivery/CalendarReplies", $"{door.Verb} {door.Route}");
        Assert.NotNull(door.Controller.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(door.Controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true));
        // RequestSizeLimitAttribute.Bytes is not public on this ASP.NET Core version (CS1061):
        // the limit is only reachable through its public IRequestSizeLimitMetadata interface.
        var limit = door.Method.GetCustomAttribute<RequestSizeLimitAttribute>();
        Assert.Equal(5 * 1024 * 1024, ((IRequestSizeLimitMetadata)limit!).MaxRequestBodySize);
        Assert.Equal(5 * 1024 * 1024, DeliveryController.MaxBodyBytes);
        Assert.Equal("delivery", door.Method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal([200, 400, 404, 413], door.Method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(a => a.StatusCode).OrderBy(c => c));
    }

    [Fact]
    public void The_key_endpoint_is_admin_only_with_four_verbs()
    {
        var actions = Surface("api/DeliveryReplyKey");
        Assert.Equal(["DELETE api/DeliveryReplyKey", "GET api/DeliveryReplyKey", "POST api/DeliveryReplyKey", "PUT api/DeliveryReplyKey"],
            actions.Select(a => $"{a.Verb} {a.Route}").OrderBy(r => r, StringComparer.Ordinal).ToList());
        var controller = actions.Select(a => a.Controller).Distinct().Single();
        Assert.Equal(AdminRequirement.PolicyName, controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.All(actions, a => Assert.Empty(a.Method.GetCustomAttributes<AllowAnonymousAttribute>()));
    }
}
