namespace weesky.Snoopy.Microservice.Models;

public sealed class SieveCondition
{
    public SieveConditionField Field { get; set; }

    /// <summary>Required when <see cref="Field"/> is <see cref="SieveConditionField.Header"/>.</summary>
    public string? HeaderName { get; set; }

    public SieveConditionOperator Operator { get; set; }

    /// <summary>For text fields, the substring/value/pattern. For Size, an octet count optionally suffixed with K/M/G.</summary>
    public string Value { get; set; } = string.Empty;
}
