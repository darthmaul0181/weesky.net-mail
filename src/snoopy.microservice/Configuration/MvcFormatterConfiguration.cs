using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;

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
}
