using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.RuleProviders;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.RuleProviders
{
    public class WeeskyRuleProviderTests
    {
        private readonly WeeskyRuleProvider _sut = new();

        // ----- Identity -----

        [Fact]
        public void Metadata_ReportsExpectedValues()
        {
            Assert.Equal("weesky", _sut.Id);
            Assert.Equal("weesky-rules", _sut.DefaultScriptName);
            Assert.False(string.IsNullOrWhiteSpace(_sut.DisplayName));
        }

        // ----- CanHandle -----

        [Fact]
        public void CanHandle_WithMarker_ReturnsTrue()
        {
            Assert.True(_sut.CanHandle("# WEESKY-RULES-V1:abc\nrequire [\"fileinto\"];"));
        }

        [Fact]
        public void CanHandle_WithoutMarker_ReturnsFalse()
        {
            Assert.False(_sut.CanHandle("require [\"fileinto\"];\nif true { keep; }"));
        }

        [Fact]
        public void CanHandle_WithEmptyString_ReturnsFalse()
        {
            Assert.False(_sut.CanHandle(string.Empty));
        }

        // ----- Compile emission -----

        [Fact]
        public void Compile_SingleConditionFileInto_EmitsExpectedScript()
        {
            var rule = MakeRule("Alerts",
                Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "[ALERT]"),
                Act(SieveActionType.FileInto, "Alerts"));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.StartsWith("# WEESKY-RULES-V1:", script);
            Assert.Contains("require [\"fileinto\"];", script);
            Assert.Contains("if header :contains \"Subject\" \"[ALERT]\" {", script);
            Assert.Contains("    fileinto \"Alerts\";", script);
        }

        [Fact]
        public void Compile_RecipientField_EmitsToAndCcHeaderList()
        {
            var rule = MakeRule("Shopping",
                Cond(SieveConditionField.Recipient, SieveConditionOperator.Contains, "darth_amazon"),
                Act(SieveActionType.FileInto, "Shop"));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("if header :contains [\"To\", \"Cc\"] \"darth_amazon\" {", script);
        }

        [Fact]
        public void Compile_AllOf_EmitsAllofKeyword()
        {
            var rule = MakeRule("Boss",
                new[] { Cond(SieveConditionField.From, SieveConditionOperator.Contains, "boss@"),
                        Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "urgent") },
                new[] { Act(SieveActionType.FileInto, "Urgent") },
                matchAll: true);

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("if allof (", script);
        }

        [Fact]
        public void Compile_DisabledRule_NotEmittedButPreservedInMarker()
        {
            var enabled = MakeRule("E",
                Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x"),
                Act(SieveActionType.Keep));
            var disabled = MakeRule("D",
                Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "y"),
                Act(SieveActionType.FileInto, "Bin"));
            disabled.Enabled = false;

            var script = _sut.Compile(new[] { enabled, disabled }).Value;
            Assert.Contains("# Rule: E", script);
            Assert.DoesNotContain("# Rule: D", script);

            var parsed = _sut.Parse(script).Value;
            Assert.Equal(2, parsed.Count);
        }

        // ----- Validation -----

        [Fact]
        public void Compile_RuleWithoutName_Fails()
        {
            var rule = new SieveRule
            {
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions = { Act(SieveActionType.Keep) }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("Name", result.Error);
        }

        [Fact]
        public void Compile_FileIntoWithoutArgument_Fails()
        {
            var rule = MakeRule("x",
                Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x"),
                new SieveAction { Type = SieveActionType.FileInto });

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void Compile_HeaderConditionWithoutHeaderName_Fails()
        {
            var rule = MakeRule("x",
                new SieveCondition { Field = SieveConditionField.Header, Operator = SieveConditionOperator.Contains, Value = "y" },
                Act(SieveActionType.Keep));

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("Header name", result.Error);
        }

        // ----- Round trip -----

        [Fact]
        public void RoundTrip_PreservesAllFields()
        {
            var original = new SieveRule
            {
                Id = Guid.NewGuid(),
                Name = "Complex",
                Enabled = true,
                MatchAll = false,
                StopAfter = true,
                Conditions =
                {
                    new SieveCondition { Field = SieveConditionField.Recipient, Operator = SieveConditionOperator.Contains, Value = "x" },
                    new SieveCondition { Field = SieveConditionField.Header, HeaderName = "X-Tag", Operator = SieveConditionOperator.Equals, Value = "Y" },
                },
                Actions =
                {
                    new SieveAction { Type = SieveActionType.SetFlag, Argument = @"\Seen" },
                    new SieveAction { Type = SieveActionType.FileInto, Argument = "VIP" },
                }
            };

            var script = _sut.Compile(new[] { original }).Value;
            var rules = _sut.Parse(script).Value;
            var rt = Assert.Single(rules);

            Assert.Equal(original.Id, rt.Id);
            Assert.Equal(original.Name, rt.Name);
            Assert.Equal(original.MatchAll, rt.MatchAll);
            Assert.Equal(original.StopAfter, rt.StopAfter);
            Assert.Equal(original.Conditions.Count, rt.Conditions.Count);
            Assert.Equal(SieveConditionField.Recipient, rt.Conditions[0].Field);
            Assert.Equal("X-Tag", rt.Conditions[1].HeaderName);
            Assert.Equal(original.Actions.Count, rt.Actions.Count);
        }

        // ----- Parse -----

        [Fact]
        public void Parse_WithoutMarker_ReturnsFailure()
        {
            var result = _sut.Parse("require [\"fileinto\"];\nif true { keep; }");
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void Parse_CorruptedMarker_ReturnsFailure()
        {
            var result = _sut.Parse("# WEESKY-RULES-V1:!!!not-base64!!!\n");
            Assert.True(result.IsFailure);
        }

        // ----- Helpers -----

        private static SieveRule MakeRule(string name, SieveCondition cond, SieveAction act, bool matchAll = true) =>
            new() { Name = name, MatchAll = matchAll, Conditions = { cond }, Actions = { act } };

        private static SieveRule MakeRule(string name, IEnumerable<SieveCondition> conds, IEnumerable<SieveAction> acts, bool matchAll = true)
        {
            var r = new SieveRule { Name = name, MatchAll = matchAll };
            r.Conditions.AddRange(conds);
            r.Actions.AddRange(acts);
            return r;
        }

        private static SieveCondition Cond(SieveConditionField f, SieveConditionOperator o, string v) =>
            new() { Field = f, Operator = o, Value = v };

        private static SieveAction Act(SieveActionType t, string? arg = null) =>
            new() { Type = t, Argument = arg };
    }
}
