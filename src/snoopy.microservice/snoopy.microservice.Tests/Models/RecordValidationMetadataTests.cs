using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

/// <summary>
/// MVC refuses to bind a record whose validation metadata landed on a primary-constructor
/// property instead of the parameter — an unhandled 500 at request time that no unit test
/// on the model itself can see. This runs the framework's own rule over every request model.
/// </summary>
public sealed class RecordValidationMetadataTests
{
    [Fact]
    public void NoModel_CarriesValidationOnAPrimaryConstructorProperty()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        var provider = services.BuildServiceProvider().GetRequiredService<IModelMetadataProvider>();

        var offenders = typeof(OpenDraftRequest).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                && type.Namespace?.StartsWith("weesky.Snoopy.Microservice.Models", StringComparison.Ordinal) == true)
            .SelectMany(type => Offenders(provider.GetMetadataForType(type)))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Validation metadata must sit on the constructor parameter: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// A declared attribute, not <see cref="ModelMetadata.ValidatorMetadata"/>: the latter also
    /// carries the Required MVC synthesises for every non-nullable reference type, which is not
    /// what the framework refuses.
    /// </summary>
    private static IEnumerable<string> Offenders(ModelMetadata metadata)
    {
        if (metadata.BoundConstructor?.BoundConstructorParameters is not { } parameters) yield break;

        foreach (var parameter in parameters)
        {
            var property = metadata.ModelType.GetProperty(parameter.ParameterName!);
            if (property?.GetCustomAttributes(typeof(ValidationAttribute), inherit: true).Length > 0)
                yield return $"{metadata.ModelType.Name}.{property.Name}";
        }
    }
}
