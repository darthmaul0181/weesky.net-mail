using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using weesky.Snoopy.Microservice.Configuration;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration
{
    // Controller tests invoke actions directly and never run an output formatter, so nothing in
    // the suite noticed that CreateFolder answered text/plain. These run the production
    // configuration itself.
    public class MvcFormatterConfigurationTests
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
}
