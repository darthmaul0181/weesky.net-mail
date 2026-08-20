using System.Text.Json;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactValidatorTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // A local stand-in for the wire payload: the phone list needs no legacy string shape, so a
    // plain (Number, Type) pair is all a test needs to build one.
    private sealed record PhonePayload(string Number, string Type);

    private static ContactRequest Request(
        string? first = null, string? last = null, string? nick = null,
        string? birthday = null, string? notes = null,
        IReadOnlyList<PhonePayload>? phones = null,
        params string[] addresses) =>
        new()
        {
            FirstName = first,
            LastName = last,
            Nickname = nick,
            Birthday = birthday,
            Notes = notes,
            Addresses = [.. addresses],
            Phones = phones is null
                ? null
                : [.. phones.Select(p => new ContactPhonePayload { Number = p.Number, Type = p.Type })],
        };

    private static ContactRequest FromJson(string json) =>
        JsonSerializer.Deserialize<ContactRequest>(json, Web)!;

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
        Assert.Equal("bruno@example.com", Assert.Single(result.Value.Addresses).Address);
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

        Assert.Equal("bruno@example.com", Assert.Single(result.Value.Addresses).Address);
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

        Assert.Equal(["second@example.com", "first@example.com"],
            result.Value.Addresses.Select(a => a.Address));
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

    [Fact]
    public void Validate_DefaultsSourceToManual()
    {
        var result = ContactValidator.Validate(new ContactRequest { FirstName = "Alice" });

        Assert.True(result.IsSuccess);
        Assert.Equal("manual", result.Value.Source);
    }

    [Theory]
    [InlineData("captured")]
    [InlineData("imported")]
    [InlineData("manual")]
    public void Validate_KeepsAKnownSource(string source)
    {
        var result = ContactValidator.Validate(new ContactRequest { FirstName = "Alice", Source = source });

        Assert.True(result.IsSuccess);
        Assert.Equal(source, result.Value.Source);
    }

    // An unknown value is filed as manual rather than refused: the field is a hint about origin,
    // and losing the contact over it would be a worse answer than mis-filing it.
    [Theory]
    [InlineData("CAPTURED")]
    [InlineData("nonsense")]
    [InlineData("")]
    public void Validate_FallsBackToManualOnAnUnknownSource(string source)
    {
        var result = ContactValidator.Validate(new ContactRequest { FirstName = "Alice", Source = source });

        Assert.True(result.IsSuccess);
        Assert.Equal("manual", result.Value.Source);
    }

    [Fact]
    public void Validate_AcceptsTheLegacyStringAddressShape()
    {
        // The frontend currently sends ["a@b.c"]; no screen changes in 4a.
        var result = ContactValidator.Validate(FromJson("""{"firstName":"Ana","addresses":["a@b.c"]}"""));
        Assert.True(result.IsSuccess);
        var line = Assert.Single(result.Value.Addresses);
        Assert.Null(line.Position);
        Assert.Equal("a@b.c", line.Address);
    }

    [Fact]
    public void Validate_RefusesATypeThatIsNotAToken()
    {
        // Token: ASCII letters, digits, dash, comma, <= 64 (spec, § Limites). A ';' or a CR is
        // the same injection vector a closed params has already shut.
        var result = ContactValidator.Validate(Request(first: "Ana", phones: [new("+322", "WORK;PREF=1")]));
        Assert.True(result.IsFailure);
        Assert.Contains("type", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_CapsPhonesAtTen()
    {
        var result = ContactValidator.Validate(Request(first: "Ana", phones: [.. Enumerable.Range(0, 11)
            .Select(i => new PhonePayload($"+32{i}", "CELL"))]));
        Assert.True(result.IsFailure);
        Assert.Contains(ContactValidator.MaxPhonesPerContact.ToString(), result.Error);
    }

    [Fact]
    public void Validate_MirrorsTheNewColumnWidths()
    {
        // birthday VARCHAR(64), website VARCHAR(512), organization 255, number 64… — unbounded,
        // an over-long value reaches MariaDB in strict mode and comes back as a 500.
        var result = ContactValidator.Validate(Request(first: "Ana", birthday: new string('x', 65)));
        Assert.True(result.IsFailure);
        Assert.Contains("birthday", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // Blank rows are what an editor leaves behind when the user opens a line and changes their
    // mind — the same rule Validate_DropsBlankAddressRows pins for e-mail, now for every child
    // collection: ContactWrite's doc promises the store never has to re-check this.
    [Fact]
    public void Validate_DropsABlankPhoneRow()
    {
        var result = ContactValidator.Validate(FromJson("""{"firstName":"Ana","phones":[{}]}"""));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Phones);
    }

    [Fact]
    public void Validate_DropsABlankPostalAddressRow()
    {
        var result = ContactValidator.Validate(FromJson("""{"firstName":"Ana","postalAddresses":[{}]}"""));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.PostalAddresses);
    }

    // notes is TEXT (65,535 bytes) — the one column left unmirrored, and the editor gate a user
    // can act on: unbounded, an over-long note came back as a 500 from a strict-mode MariaDB.
    [Fact]
    public void Validate_MirrorsTheNotesColumnWidth()
    {
        var result = ContactValidator.Validate(
            Request(first: "Ana", notes: new string('x', ContactValidator.MaxNotesLength + 1)));

        Assert.True(result.IsFailure);
        Assert.Contains("notes", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsValidTypeToken_RefusesATrailingNewline()
    {
        // .NET's $ also matches immediately before a trailing '\n'; \z does not. This method is
        // called directly by later tasks on values they may not have trimmed themselves.
        Assert.False(ContactValidator.IsValidTypeToken("HOME\n"));
    }
}
