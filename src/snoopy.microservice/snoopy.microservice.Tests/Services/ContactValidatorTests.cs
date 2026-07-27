using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactValidatorTests
{
    private static ContactRequest Request(
        string? first = null, string? last = null, string? nick = null, params string[] addresses) =>
        new() { FirstName = first, LastName = last, Nickname = nick, Addresses = [.. addresses] };

    [Fact]
    public void Validate_WithANameOnly_Succeeds()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Bruno", result.Value.FirstName);
        Assert.Empty(result.Value.Addresses);
    }

    [Fact]
    public void Validate_WithAnAddressOnly_Succeeds()
    {
        var result = ContactValidator.Validate(Request(addresses: "bruno@example.com"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.FirstName);
        Assert.Equal("bruno@example.com", Assert.Single(result.Value.Addresses));
    }

    [Fact]
    public void Validate_WithANicknameOnly_Succeeds()
    {
        Assert.True(ContactValidator.Validate(Request(nick: "bru")).IsSuccess);
    }

    // The gate the spec sets: a contact must carry at least one human identifier or one address.
    // Blank strings are not identifiers — they would produce a tile with no label at all.
    [Fact]
    public void Validate_WithNothing_Fails()
    {
        var result = ContactValidator.Validate(Request(first: "   ", last: ""));

        Assert.True(result.IsFailure);
        Assert.Contains("name", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_TrimsNamesAndNullsTheEmptyOnes()
    {
        var result = ContactValidator.Validate(Request(first: "  Bruno  ", last: "   ", nick: ""));

        Assert.Equal("Bruno", result.Value.FirstName);
        Assert.Null(result.Value.LastName);
        Assert.Null(result.Value.Nickname);
    }

    [Fact]
    public void Validate_WithAnUnparsableAddress_FailsNamingIt()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno", addresses: "not-an-address"));

        Assert.True(result.IsFailure);
        Assert.Contains("not-an-address", result.Error);
    }

    // Blank entries come from an editor row the user opened and left empty; dropping them is what
    // the user meant, and refusing the save would be unexplainable next to an empty box.
    [Fact]
    public void Validate_DropsBlankAddressRows()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno", addresses: ["bruno@example.com", "  ", ""]));

        Assert.Equal("bruno@example.com", Assert.Single(result.Value.Addresses));
    }

    [Fact]
    public void Validate_PastTheAddressCap_Fails()
    {
        var many = Enumerable.Range(0, ContactValidator.MaxAddressesPerContact + 1)
            .Select(i => $"a{i}@example.com").ToArray();

        var result = ContactValidator.Validate(Request(first: "Bruno", addresses: many));

        Assert.True(result.IsFailure);
        Assert.Contains(ContactValidator.MaxAddressesPerContact.ToString(), result.Error);
    }

    [Fact]
    public void Validate_KeepsTheAddressOrderGiven()
    {
        var result = ContactValidator.Validate(
            Request(addresses: ["second@example.com", "first@example.com"]));

        Assert.Equal(["second@example.com", "first@example.com"], result.Value.Addresses);
    }

    // The column widths, not a taste: unbounded, a 150-character name reaches a strict-mode
    // MariaDB and the write throws, so the user reads "Internal Server Error" instead of a 400.
    [Theory]
    [InlineData("first name")]
    [InlineData("last name")]
    [InlineData("nickname")]
    public void Validate_WithANameOverTheColumnWidth_FailsNamingTheField(string field)
    {
        var tooLong = new string('a', ContactValidator.MaxNameLength + 1);
        var request = field switch
        {
            "first name" => Request(first: tooLong),
            "last name" => Request(last: tooLong),
            _ => Request(nick: tooLong)
        };

        var result = ContactValidator.Validate(request);

        Assert.True(result.IsFailure);
        Assert.Contains(field, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ContactValidator.MaxNameLength.ToString(), result.Error);
    }

    [Fact]
    public void Validate_AtTheNameColumnWidth_Succeeds()
    {
        var exactly = new string('a', ContactValidator.MaxNameLength);

        Assert.True(ContactValidator.Validate(Request(first: exactly)).IsSuccess);
    }

    [Fact]
    public void Validate_WithAnAddressOverTheColumnWidth_Fails()
    {
        // Parsable but too wide for the column: the length is its own rule, not a by-product of
        // the address being malformed.
        var local = new string('a', ContactValidator.MaxAddressLength);
        var result = ContactValidator.Validate(Request(addresses: $"{local}@example.com"));

        Assert.True(result.IsFailure);
        Assert.Contains(ContactValidator.MaxAddressLength.ToString(), result.Error);
    }

    [Fact]
    public void Validate_WithANullRequest_Fails()
    {
        Assert.True(ContactValidator.Validate(null!).IsFailure);
    }
}
