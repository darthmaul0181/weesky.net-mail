using System.ComponentModel.DataAnnotations;
using weesky.Scotty.Microservice.Services;

namespace weesky.Scotty.Microservice.Models;

/// <summary>The sender domain and optional BIMI selector whose logo is asked for.</summary>
public sealed record BimiRequest([Required, StringLength(253)] string? Domain, [StringLength(63)] string? Selector)
{
    public const string DefaultSelector = "default";

    public string DomainName => Domain?.Trim().TrimEnd('.').ToLowerInvariant() ?? "";

    public string SelectorName => string.IsNullOrEmpty(Selector) ? DefaultSelector : Selector.ToLowerInvariant();

    public bool IsValid => IsDomainName(DomainName) && MailBimiHint.IsSelector(SelectorName);

    /// <summary>A lower-case DNS name with at least two labels and a non-numeric last one, so an IP literal never passes.</summary>
    public static bool IsDomainName(string name) =>
        name.Length <= 253 && name.Contains('.')
        && name.Split('.') is var labels && labels.All(IsLabel) && !labels[^1].All(char.IsAsciiDigit);

    private static bool IsLabel(string label) =>
        label.Length is >= 1 and <= 63
        && label[0] != '-' && label[^1] != '-'
        && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}
