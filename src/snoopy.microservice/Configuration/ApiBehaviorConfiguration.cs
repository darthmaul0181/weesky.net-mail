using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Configuration;

/// <summary>
/// Makes a model-binding refusal answer the same 400 + <see cref="ResultEnveloppe"/> every
/// hand-written refusal produces, instead of the framework's ValidationProblemDetails —
/// so a client parses one failure shape, not two.
/// </summary>
internal static class ApiBehaviorConfiguration
{
    public static IServiceCollection AddEnveloppeModelStateResponse(this IServiceCollection services)
    {
        // PostConfigure, not Configure: this runs before AddControllers in Program.cs, and
        // AddControllers' own ApiBehaviorOptionsSetup overwrites the factory unconditionally.
        services.PostConfigure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = CreateEnveloppeResponse);
        return services;
    }

    private static IActionResult CreateEnveloppeResponse(ActionContext context)
    {
        var messages = context.ModelState
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .SelectMany(entry => entry.Value?.Errors ?? [], (entry, error) => Describe(entry.Key, error))
            .Distinct()
            .ToList();

        var message = messages.Count > 0 ? string.Join("; ", messages) : "The request is invalid";
        return new BadRequestObjectResult(ResultEnveloppe.CreateErrorEnveloppe(message));
    }

    /// <summary>
    /// A '$'-keyed entry or an exception-backed one carries deserialiser text; name the
    /// field, never the parser.
    /// </summary>
    private static string Describe(string key, ModelError error)
    {
        if (key.StartsWith('$')) return "The request body is not valid JSON";
        if (error.Exception is not null || string.IsNullOrWhiteSpace(error.ErrorMessage))
            return key.Length == 0 ? "The request is invalid" : $"The {key} field is invalid";
        return error.ErrorMessage.TrimEnd('.');
    }
}
