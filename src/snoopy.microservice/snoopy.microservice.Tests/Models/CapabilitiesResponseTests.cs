using System.Text.Json;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class CapabilitiesResponseTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static CapabilitiesResponse Response() => new(
        "weesky", Admin: true, Aliases: true, PasswordChange: true, ProfileEditing: true,
        StrictIdentities: true, Quota: true, Rules: true, Dav: true);

    /// <summary>
    /// Pins the camelCase names the frontend gates on. A drift here — a rename, a reorder that
    /// somehow changes casing — would silently disable a whole feature area on the client, which
    /// reads every one of these keys by exact name.
    /// </summary>
    [Theory]
    [InlineData("platform")]
    [InlineData("admin")]
    [InlineData("aliases")]
    [InlineData("passwordChange")]
    [InlineData("profileEditing")]
    [InlineData("strictIdentities")]
    [InlineData("quota")]
    [InlineData("rules")]
    [InlineData("dav")]
    public void Serialize_NamesThePropertiesAsTheFrontendReadsThem(string property)
    {
        var json = JsonSerializer.Serialize(Response(), Web);

        Assert.Contains($"\"{property}\":", json, StringComparison.Ordinal);
    }
}
