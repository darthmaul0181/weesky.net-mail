using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text.Json.Serialization;

namespace weesky.Snoopy.Microservice.Configuration;

/// <summary>
/// Output-formatter policy. Lives here rather than inline in Program.cs so it can be tested:
/// controller tests invoke actions directly and never run a formatter.
/// </summary>
internal static class MvcFormatterConfiguration
{
    /// <summary>
    /// Drops <see cref="StringOutputFormatter"/>: MVC writes a bare string as
    /// <c>text/plain</c>, which the client's <c>res.json()</c> cannot parse.
    /// </summary>
    public static void ConfigureFormatters(MvcOptions options)
    {
        options.OutputFormatters.RemoveType<StringOutputFormatter>();
    }

    /// <summary>
    /// The serialisation policy every response is written with. <see
    /// cref="JsonIgnoreCondition.WhenWritingNull"/> is part of the client contract, not a tidiness
    /// choice: a null property is absent from the payload, which is what lets the TypeScript
    /// declare it optional — and what keeps <c>DavCredentialsView.Password</c> from travelling as
    /// <c>"password": null</c> on every response that draws no secret.
    /// </summary>
    public static void ConfigureJson(JsonOptions options)
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }
}
