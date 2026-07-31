using System.ComponentModel.DataAnnotations;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

/// <summary>
/// The script name travels from the request body into a line-oriented ManageSieve command,
/// so the model-binding boundary must refuse what the protocol layer would have to reject.
/// </summary>
public sealed class SieveScriptNameValidationTests
{
    private static bool IsValid(object model, out List<ValidationResult> errors)
    {
        errors = [];
        return Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true);
    }

    public static TheoryData<string> InjectingNames => new()
    {
        "weesky-rules\r\nDELETESCRIPT \"rainloop.user\"",
        "weesky-rules\nSETACTIVE \"\"",
        "weesky-rules\rX",
        "weesky-rules\0",
        "weesky\u007Frules",
        "weesky\trules",
    };

    public static TheoryData<string> LegitimateNames => new()
    {
        "weesky-rules",
        "rainloop.user",
        "rainloop.sieve.0",
        "Filtres perso",
    };

    [Theory]
    [MemberData(nameof(InjectingNames))]
    public void SaveRulesRequest_WithControlCharacterInScriptName_IsInvalid(string name)
    {
        var valid = IsValid(new SaveRulesRequest { ScriptName = name }, out var errors);

        Assert.False(valid);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(SaveRulesRequest.ScriptName)));
    }

    [Theory]
    [MemberData(nameof(InjectingNames))]
    public void SieveRawScript_WithControlCharacterInScriptName_IsInvalid(string name)
    {
        var valid = IsValid(new SieveRawScript { Content = "stop;", ScriptName = name }, out var errors);

        Assert.False(valid);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(SieveRawScript.ScriptName)));
    }

    [Theory]
    [MemberData(nameof(LegitimateNames))]
    public void SaveRulesRequest_WithLegitimateScriptName_IsValid(string name)
        => Assert.True(IsValid(new SaveRulesRequest { ScriptName = name }, out _));

    [Theory]
    [MemberData(nameof(LegitimateNames))]
    public void SieveRawScript_WithLegitimateScriptName_IsValid(string name)
        => Assert.True(IsValid(new SieveRawScript { Content = "stop;", ScriptName = name }, out _));

    [Fact]
    public void SaveRulesRequest_WithNoScriptName_IsValid()
        => Assert.True(IsValid(new SaveRulesRequest(), out _));

    [Fact]
    public void SieveRawScript_WithNoScriptName_IsValid()
        => Assert.True(IsValid(new SieveRawScript { Content = "stop;" }, out _));

    [Fact]
    public void SaveRulesRequest_WithOverlongScriptName_IsInvalid()
        => Assert.False(IsValid(new SaveRulesRequest { ScriptName = new string('a', 129) }, out _));

    [Fact]
    public void SieveRawScript_WithOverlongScriptName_IsInvalid()
        => Assert.False(IsValid(new SieveRawScript { Content = "stop;", ScriptName = new string('a', 129) }, out _));
}
