using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

// The factory is fetched through the same composition Program.cs runs (options before
// AddControllers), because AddControllers' own setup overwrites the factory it finds:
// a delegate tested in isolation could pass while the deployed pipeline still answered
// ProblemDetails. A full WebApplicationFactory host is not used — Program refuses to
// start without connection strings and a key ring, none of which this behaviour needs.
public sealed class ApiBehaviorConfigurationTests
{
    private static Func<ActionContext, IActionResult> ComposedFactory()
    {
        var services = new ServiceCollection();
        services.AddSnoopyOptions(new ConfigurationBuilder().Build());
        services.AddControllers();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value.InvalidModelStateResponseFactory;
    }

    private static ResultEnveloppe Invoke(ModelStateDictionary modelState)
    {
        var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), modelState);
        var result = ComposedFactory()(context);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var enveloppe = Assert.IsType<ResultEnveloppe>(badRequest.Value);
        Assert.Equal(ResultState.Error, enveloppe.State);
        return enveloppe;
    }

    [Fact]
    public void TwoMissingFields_AnswerOneEnveloppeMessageInFieldOrder()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Password", "The Password field is required.");
        modelState.AddModelError("Email", "The Email field is required.");

        var enveloppe = Invoke(modelState);

        Assert.Equal("The Email field is required; The Password field is required", enveloppe.Message);
    }

    [Fact]
    public void SingleAttributeMessage_PassesThroughVerbatim()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("FolderPath", "A folder is required");

        var enveloppe = Invoke(modelState);

        Assert.Equal("A folder is required", enveloppe.Message);
    }

    [Fact]
    public void ExceptionBackedEntry_NamesTheFieldNotTheException()
    {
        var modelState = new ModelStateDictionary();
        modelState.TryAddModelException("Uids", new FormatException("System.FormatException: parser internals"));

        var enveloppe = Invoke(modelState);

        Assert.Equal("The Uids field is invalid", enveloppe.Message);
    }

    [Fact]
    public void JsonBodyEntries_CollapseToOneReadableMessage()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.imapPort", "The JSON value could not be converted to System.Int32.");
        modelState.AddModelError("$.smtpPort", "The JSON value could not be converted to System.Int32.");

        var enveloppe = Invoke(modelState);

        Assert.Equal("The request body is not valid JSON", enveloppe.Message);
    }

    [Fact]
    public void EmptyModelState_StillAnswersAnEnveloppe()
    {
        var enveloppe = Invoke(new ModelStateDictionary());

        Assert.Equal("The request is invalid", enveloppe.Message);
    }
}
