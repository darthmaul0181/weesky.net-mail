using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// The HTTP surface a set of assemblies would publish, read the way MVC reads it: through
/// <see cref="ApplicationPartManager"/>, so a route the host does not load is a route this says
/// does not exist.
/// </summary>
internal static class ControllerRouteSurface
{
    internal readonly record struct Action(Type Controller, MethodInfo Method, string Verb, string Prefix, string Route);

    public static IReadOnlyList<Type> ControllersOf(params Assembly[] assemblies)
    {
        var manager = new ApplicationPartManager();
        foreach (var assembly in assemblies) manager.ApplicationParts.Add(new AssemblyPart(assembly));
        manager.FeatureProviders.Add(new ControllerFeatureProvider());

        var feature = new ControllerFeature();
        manager.PopulateFeature(feature);

        return [.. feature.Controllers.Select(c => c.AsType())];
    }

    public static IReadOnlyList<Action> Of(IEnumerable<Type> controllers)
    {
        var surface = new List<Action>();
        foreach (var type in controllers)
        {
            var prefix = type.GetCustomAttribute<RouteAttribute>()?.Template?
                .Replace("[controller]", type.Name[..^"Controller".Length]);
            if (prefix is null) continue;

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                foreach (var http in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var route = string.IsNullOrEmpty(http.Template) ? prefix : $"{prefix}/{http.Template}";
                    surface.Add(new Action(type, method, http.HttpMethods.Single(), prefix, route));
                }
            }
        }

        return surface;
    }
}
