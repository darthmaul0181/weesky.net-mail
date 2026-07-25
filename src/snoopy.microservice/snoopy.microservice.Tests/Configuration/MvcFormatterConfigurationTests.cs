using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using weesky.Snoopy.Microservice.Configuration;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

// Controller tests never run an output formatter, so nothing caught CreateFolder answering
// text/plain. This runs the production configuration itself.
public sealed class MvcFormatterConfigurationTests
{
    [Fact]
    public void ConfigureFormatters_DropsTheStringFormatterSoStringsSerialiseAsJson()
    {
        var options = new MvcOptions();
        options.OutputFormatters.Add(new StringOutputFormatter());
        options.OutputFormatters.Add(new HttpNoContentOutputFormatter());

        MvcFormatterConfiguration.ConfigureFormatters(options);

        Assert.DoesNotContain(options.OutputFormatters, f => f is StringOutputFormatter);
        Assert.Contains(options.OutputFormatters, f => f is HttpNoContentOutputFormatter);
    }
}
