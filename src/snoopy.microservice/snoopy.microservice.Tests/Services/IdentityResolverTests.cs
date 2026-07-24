using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class IdentityResolverTests
{
    private static SendingIdentity Row(string address, string name, bool isDefault = false) =>
        new() { Address = address, DisplayName = name, IsDefault = isDefault };

    private static IdentityEntry Entry(string address, string name = "Someone", bool isDefault = false) =>
        new() { Address = address, DisplayName = name, IsDefault = isDefault };

    // ── Owns ─────────────────────────────────────────────────────────────────

    private static readonly List<string> Aliases = ["ALIAS@weesky.be", "second@Weesky.be"];

    [Theory]
    [InlineData("mick@weesky.be", true)]
    [InlineData(" MICK@Weesky.BE ", true)]
    [InlineData("alias@weesky.be", true)]
    [InlineData("Alias@WEESKY.be", true)]
    [InlineData("intruder@evil.com", false)]
    public void Owns_AcceptsThePrimaryAndTheLiveAliasesOnly(string address, bool expected)
    {
        Assert.Equal(expected, IdentityResolver.Owns(Aliases, " Mick@Weesky.BE ", address));
    }

    /// <summary>Owns canonicalises whatever collection it is handed — a caller switching to a set
    /// must not silently get the trusting rule; only OwnsCanonical assumes its input is canonical.</summary>
    [Fact]
    public void Owns_CanonicalisesEvenWhenHandedASet()
    {
        Assert.True(IdentityResolver.Owns(Aliases.ToHashSet(), " Mick@Weesky.BE ", "alias@weesky.be"));
        Assert.True(IdentityResolver.Owns(Aliases.ToArray(), " Mick@Weesky.BE ", "SECOND@weesky.be"));
        Assert.False(IdentityResolver.Owns(Aliases.ToHashSet(), " Mick@Weesky.BE ", "intruder@evil.com"));
    }

    [Theory]
    [InlineData("mick@weesky.be", true)]
    [InlineData(" MICK@Weesky.BE ", true)]
    [InlineData("Second@WEESKY.be", true)]
    [InlineData("intruder@evil.com", false)]
    public void OwnsCanonical_IsTheSameRuleOverAPreCanonicalisedSet(string address, bool expected)
    {
        var set = IdentityResolver.CanonicalSet(Aliases);

        Assert.Equal(expected, IdentityResolver.OwnsCanonical(set, " Mick@Weesky.BE ", address));
    }

    // ── Resolve ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_AlwaysProducesThePrimary_LabelledByFullName()
    {
        var list = IdentityResolver.Resolve([], "mick@weesky.be", "Mick Dubois", []);

        var identity = Assert.Single(list);
        Assert.Equal("mick@weesky.be", identity.Address);
        Assert.Equal("Mick Dubois", identity.DisplayName);
        Assert.True(identity.IsPrimary);
        Assert.True(identity.IsDefault);
        Assert.False(identity.Stale);
        Assert.False(identity.LabelIsCustom);
    }

    [Fact]
    public void Resolve_FallsBackToTheAddressWhenThereIsNoFullName()
    {
        var list = IdentityResolver.Resolve([], "mick@weesky.be", null, []);
        Assert.Equal("mick@weesky.be", Assert.Single(list).DisplayName);
    }

    [Fact]
    public void Resolve_APrimaryRowOverridesTheFullName()
    {
        var list = IdentityResolver.Resolve(
            [Row("mick@weesky.be", "Le Boss")], "mick@weesky.be", "Mick Dubois", []);

        var identity = Assert.Single(list);
        Assert.Equal("Le Boss", identity.DisplayName);
        Assert.True(identity.LabelIsCustom);
    }

    [Fact]
    public void Resolve_AnAliasRowThatIsNoLongerOwnedIsStale()
    {
        var list = IdentityResolver.Resolve(
            [Row("gone@weesky.be", "Ancien")], "mick@weesky.be", "Mick", ["kept@weesky.be"]);

        var stale = Assert.Single(list, i => i.Address == "gone@weesky.be");
        Assert.True(stale.Stale);
        Assert.False(stale.IsDefault);
    }

    [Fact]
    public void Resolve_TheDefaultFallsBackToThePrimaryWhenTheMarkedRowIsStale()
    {
        var list = IdentityResolver.Resolve(
            [Row("gone@weesky.be", "Ancien", isDefault: true)], "mick@weesky.be", "Mick", []);

        Assert.True(Assert.Single(list, i => i.IsPrimary).IsDefault);
    }

    [Fact]
    public void Resolve_SortsDefaultFirstThenByLabel()
    {
        var list = IdentityResolver.Resolve(
            [Row("zeta@weesky.be", "Zeta", isDefault: true), Row("beta@weesky.be", "beta")],
            "mick@weesky.be", "Mick", ["zeta@weesky.be", "beta@weesky.be"]);

        Assert.Equal(["Zeta", "beta", "Mick"], list.Select(i => i.DisplayName).ToArray());
    }

    [Fact]
    public void Resolve_ComparesAddressesCanonically()
    {
        var list = IdentityResolver.Resolve(
            [Row("MICK@weesky.be", "Custom")], " Mick@Weesky.BE ", "Mick", []);

        var identity = Assert.Single(list);
        Assert.Equal("mick@weesky.be", identity.Address);
        Assert.Equal("Custom", identity.DisplayName);
    }

    // ── LabelFor ─────────────────────────────────────────────────────────────

    [Fact]
    public void LabelFor_PrefersTheRowThenTheFullNameThenTheAddress()
    {
        var stored = new[] { Row("michel@weesky.be", "Michel D.") };
        Assert.Equal("Michel D.", IdentityResolver.LabelFor(stored, "michel@weesky.be", "Mick"));
        Assert.Equal("Mick", IdentityResolver.LabelFor(stored, "other@weesky.be", "Mick"));
        Assert.Equal("other@weesky.be", IdentityResolver.LabelFor(stored, "other@weesky.be", null));
    }

    /// <summary>
    /// The frontend mirrors this one branch: <c>identityRows.applyLabel</c> is handed
    /// <c>accountIdentity.deriveIdentity</c>'s <c>labelFallback</c> — full name, else the
    /// canonical (trimmed, lower-cased) address, never the stored casing — as its
    /// <c>fallbackName</c>, so clearing the primary's override shows, before the refetch lands,
    /// what this resolver will send back. If the precedence below changes, change
    /// <c>src/frontend/src/lib/accountIdentity.ts</c> with it — the mirror is a duplicated rule,
    /// not a derived one.
    /// </summary>
    [Fact]
    public void LabelFor_LabelsAnUnstoredPrimaryTheWayTheFrontendPredicts()
    {
        Assert.Equal("Mick Dubois", IdentityResolver.LabelFor([], "mick@weesky.be", "Mick Dubois"));
        Assert.Equal("mick@weesky.be", IdentityResolver.LabelFor([], "mick@weesky.be", null));
        Assert.Equal("mick@weesky.be", IdentityResolver.LabelFor([], "mick@weesky.be", "   "));
        // Stored userName casing must not leak through the fallback: the frontend's optimistic
        // row falls back to the canonical address too, never the casing it has on hand.
        Assert.Equal("mick@weesky.be", IdentityResolver.LabelFor([], "Mick@Weesky.be", "   "));
    }

    /// <summary>
    /// <c>Resolve</c> orders by <see cref="StringComparer.OrdinalIgnoreCase"/>, which folds
    /// upward and simply. The frontend's <c>sortIdentities</c> reimplements exactly that so the
    /// optimistic list does not reshuffle when the refetch lands; these are the pairs where a
    /// naive <c>toLowerCase</c> or <c>toUpperCase</c> would disagree with it.
    /// </summary>
    [Theory]
    // Folding upward puts '_' (U+005F) after the letters, where lower-casing would put it first.
    [InlineData("_perso", "Anne", 1)]
    // Simple folding leaves U+00DF and the ligatures alone, where upper-casing expands them.
    [InlineData("ß", "ss", 1)]
    [InlineData("SS", "ß", -1)]
    [InlineData("fix", "ﬁx", -1)]
    [InlineData("İ", "i", 1)]
    [InlineData("anne", "Anne", 0)]
    public void OrdinalIgnoreCase_OrdersTheWayTheFrontendSortDoes(string left, string right, int expected)
        => Assert.Equal(expected, Math.Sign(StringComparer.OrdinalIgnoreCase.Compare(left, right)));

    // ── Validate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AcceptsPrimaryAliasesAndAlreadyStoredAddresses()
    {
        var result = IdentityResolver.Validate(
            [Entry("mick@weesky.be", "Me"), Entry("alias@weesky.be"), Entry("stale@weesky.be", "Old")],
            "mick@weesky.be", ["alias@weesky.be"], ["stale@weesky.be"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
    }

    [Fact]
    public void Validate_NamesAForeignAddress()
    {
        var result = IdentityResolver.Validate(
            [Entry("intruder@evil.com")], "mick@weesky.be", [], []);

        Assert.True(result.IsFailure);
        Assert.Contains("intruder@evil.com", result.Error);
    }

    [Fact]
    public void Validate_CanonicalisesAndRefusesADuplicate()
    {
        var result = IdentityResolver.Validate(
            [Entry("Alias@weesky.be"), Entry("alias@WEESKY.be")],
            "mick@weesky.be", ["alias@weesky.be"], []);

        Assert.True(result.IsFailure);
        Assert.Contains("twice", result.Error);
    }

    [Fact]
    public void Validate_RefusesAnUnparsableAddress()
    {
        var result = IdentityResolver.Validate(
            [Entry("not an address")], "mick@weesky.be", [], []);

        Assert.True(result.IsFailure);
        Assert.Contains("not an address", result.Error);
        Assert.Contains("valid email address", result.Error);
    }

    /// <summary>A well-formed address that is simply foreign keeps the ownership wording, kept
    /// distinct from the format error above so the two failures are never confused.</summary>
    [Fact]
    public void Validate_RefusesAWellFormedForeignAddressWithTheOwnershipError()
    {
        var result = IdentityResolver.Validate(
            [Entry("intruder@evil.com")], "mick@weesky.be", [], []);

        Assert.True(result.IsFailure);
        Assert.Contains("intruder@evil.com", result.Error);
        Assert.Contains("one of your addresses", result.Error);
    }

    /// <summary>A decorated "Name &lt;a@b.c&gt;" is a format error even when the bare address
    /// underneath is owned — DisplayName is a separate field, so Address must already be bare.</summary>
    [Fact]
    public void Validate_RefusesADecoratedAddressEvenWhenTheBareAddressIsOwned()
    {
        var result = IdentityResolver.Validate(
            [Entry("Mick Dubois <mick@weesky.be>")], "mick@weesky.be", [], []);

        Assert.True(result.IsFailure);
        Assert.Contains("Mick Dubois <mick@weesky.be>", result.Error);
        Assert.Contains("valid email address", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RefusesAnEmptyDisplayName(string name)
    {
        var result = IdentityResolver.Validate(
            [Entry("mick@weesky.be", name)], "mick@weesky.be", [], []);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_RefusesADisplayNameOver100CharsOrWithLineBreaks()
    {
        Assert.True(IdentityResolver.Validate(
            [Entry("mick@weesky.be", new string('x', 101))], "mick@weesky.be", [], []).IsFailure);
        Assert.True(IdentityResolver.Validate(
            [Entry("mick@weesky.be", "a\r\nb")], "mick@weesky.be", [], []).IsFailure);
    }

    [Fact]
    public void Validate_RefusesTwoDefaults()
    {
        var result = IdentityResolver.Validate(
            [Entry("mick@weesky.be", "Me", isDefault: true), Entry("a@weesky.be", "A", isDefault: true)],
            "mick@weesky.be", ["a@weesky.be"], []);

        Assert.True(result.IsFailure);
        Assert.Contains("default", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_OutputsCanonicalTrimmedRows()
    {
        var result = IdentityResolver.Validate(
            [Entry("  Alias@Weesky.BE ", "  Michel  ")], "mick@weesky.be", ["alias@weesky.be"], []);

        var row = Assert.Single(result.Value);
        Assert.Equal("alias@weesky.be", row.Address);
        Assert.Equal("Michel", row.DisplayName);
    }
}
