using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class DavCredentialsViewTests
{
    /// <summary>The very policy Program.cs hands to <c>AddJsonOptions</c>, not a copy of it.</summary>
    private static readonly JsonSerializerOptions Api = ApiJson();

    private static JsonSerializerOptions ApiJson()
    {
        var options = new JsonOptions();
        MvcFormatterConfiguration.ConfigureJson(options);
        return options.JsonSerializerOptions;
    }

    private static DavCredentialsView View(string? password) => new(
        "https://api.mail.weesky.net", "alice@weesky.be", true, true,
        new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc), password);

    /// <summary>
    /// Pins the camelCase names the Sync screen reads by exact name. A rename here disables a
    /// field on the client with nothing failing on either side.
    /// </summary>
    [Theory]
    [InlineData("serverUrl")]
    [InlineData("username")]
    [InlineData("configured")]
    [InlineData("cardDavEnabled")]
    [InlineData("lastUsedAt")]
    [InlineData("password")]
    public void Serialize_NamesThePropertiesAsTheFrontendReadsThem(string property)
    {
        var json = JsonSerializer.Serialize(View("ABCDEFGHIJKLMNOPQRST"), Api);

        Assert.Contains($"\"{property}\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_WithNoSecretDrawn_OmitsThePasswordRatherThanWritingItNull()
    {
        // The whole "there is nothing to reveal" story rests on WhenWritingNull, configured one
        // assembly away: a "password": null would make the field a permanent part of the payload
        // and the client's `password?: string` a lie about a key that is always there.
        var json = JsonSerializer.Serialize(View(null), Api);

        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_MarksTheLastUseAsUtcOnTheWire()
    {
        // The Kind is what the serialiser reads; the "Z" is what the browser parses. Without it
        // the relative "last used" is wrong by the viewer's offset — the one reading that field
        // exists to give.
        var json = JsonSerializer.Serialize(View(null), Api);

        Assert.Contains("\"lastUsedAt\":\"2026-08-23T08:00:00Z\"", json, StringComparison.Ordinal);
    }
}
