using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using weesky.Snoopy.Providers.Weesky;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>
/// The weesky routes exist only where the weesky platform is loaded. The switch that decides it is
/// one <c>AddApplicationPart</c> call in <c>Program.cs</c>; forget it and a generic deployment still
/// publishes <c>api/Admin</c> and <c>api/aliases</c>, answering 500 out of a dovecot database it
/// does not have instead of 404 out of a route it does not serve.
///
/// The parts are read off the real host assembly rather than listed here, because the switch has a
/// second half that a hand-written list cannot see: the Web SDK stamps an
/// <see cref="ApplicationPartAttribute"/> for every MVC-referencing reference unless the csproj
/// turns it off, and MVC loads those without anyone asking.
/// </summary>
public sealed class PlatformRouteSurfaceTests
{
    private static readonly Assembly Host = typeof(Program).Assembly;

    /// <summary>The routes that belong to the platform, not to the webmail. Spelled as the
    /// [controller] token expands them — routing itself is case-insensitive.</summary>
    private static readonly string[] PlatformRoutes =
    [
        "GET api/Admin/users",
        "POST api/Admin/users",
        "PUT api/Admin/users/{id}",
        "DELETE api/Admin/users/{id}",
        "GET api/Admin/domains",
        "GET api/Admin/domains/virtuals",
        "GET api/Aliases",
        "POST api/Aliases",
        "DELETE api/Aliases",
        "PATCH api/Account/ChangeSecret",
        "POST api/Account/FullName",
    ];

    /// <summary>What <c>ApplicationPartManager.PopulateDefaultParts</c> finds on its own: the host
    /// assembly and every assembly its <see cref="ApplicationPartAttribute"/>s name.</summary>
    private static IEnumerable<Assembly> DefaultParts() =>
        [Host, .. Host.GetCustomAttributes<ApplicationPartAttribute>().Select(a => Assembly.Load(a.AssemblyName))];

    private static HashSet<string> Surface(bool weesky)
    {
        // The two calls Program.cs makes, on top of what MVC discovers by itself.
        Assembly[] added = weesky
            ? [typeof(ApiBaseController).Assembly, typeof(WeeskyPlatform).Assembly]
            : [typeof(ApiBaseController).Assembly];

        return [.. ControllerRouteSurface
            .Of(ControllerRouteSurface.ControllersOf([.. DefaultParts().Concat(added).Distinct()]))
            .Select(a => $"{a.Verb} {a.Route}")];
    }

    /// <summary>The host names its parts in <c>Program.cs</c> and nowhere else. An attribute here —
    /// the Web SDK generates one per MVC-referencing project reference by default — makes the
    /// provider a part of every deployment, whatever <c>Platform</c> says.</summary>
    [Fact]
    public void The_host_assembly_carries_no_generated_application_part()
    {
        var parts = Host.GetCustomAttributes<ApplicationPartAttribute>().Select(a => a.AssemblyName);

        Assert.Empty(parts);
    }

    [Fact]
    public void In_generic_mode_no_platform_route_is_published()
    {
        var surface = Surface(weesky: false);

        Assert.All(PlatformRoutes, route => Assert.DoesNotContain(route, surface));
        Assert.DoesNotContain(surface, route => route.Contains("api/Admin", StringComparison.Ordinal));
        Assert.DoesNotContain(surface, route => route.Contains("api/aliases", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void In_weesky_mode_every_platform_route_is_published()
    {
        var surface = Surface(weesky: true);

        Assert.All(PlatformRoutes, route => Assert.Contains(route, surface));
    }

    /// <summary>The webmail's own surface is the same on both platforms — the split moved routes
    /// out of the core, it must not have moved any of the ones that stayed.</summary>
    [Fact]
    public void The_core_surface_is_untouched_by_the_platform()
    {
        var generic = Surface(weesky: false);
        var weesky = Surface(weesky: true);

        Assert.Subset(weesky, generic);
        Assert.Contains("GET api/Account", generic);
        Assert.Contains("GET api/Account/Quota", generic);
        Assert.Contains("GET api/Mail/Folders", generic);
        Assert.Contains("GET api/Capabilities", generic);
    }
}
