using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace weesky.Snoopy.Microservice.Configuration
{
    /// <summary>
    /// Output-formatter policy for the API. Lives here rather than inline in Program.cs so the
    /// rule can be tested: startup configuration is invisible to controller tests, which invoke
    /// actions directly and never run a formatter.
    /// </summary>
    public static class MvcFormatterConfiguration
    {
        /// <summary>
        /// Drops <see cref="StringOutputFormatter"/>. MVC otherwise writes an action that returns
        /// a bare string as <c>text/plain</c>, and the browser client calls <c>res.json()</c> on
        /// every response — so creating a folder named "toto" answered with the unquoted five
        /// bytes <c>toto</c> and the client threw "toto is not valid JSON" over a request the
        /// server had in fact carried out.
        /// </summary>
        public static void ConfigureFormatters(MvcOptions options)
        {
            options.OutputFormatters.RemoveType<StringOutputFormatter>();
        }
    }
}
