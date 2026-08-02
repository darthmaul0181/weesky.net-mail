using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Tests.Models;

/// <summary>
/// Runs the same DataAnnotations pass the model binder runs, so a test asserts what the
/// pipeline will actually refuse — not what a controller's manual guard would say.
/// </summary>
internal static class RequestValidation
{
    public static List<string?> Messages(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return [.. results.Select(result => result.ErrorMessage)];
    }
}
