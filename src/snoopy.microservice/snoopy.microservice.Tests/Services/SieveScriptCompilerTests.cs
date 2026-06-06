using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services
{
    public class SieveScriptCompilerTests
    {
        private readonly SieveScriptCompiler _sut = new();

        // ----- Compile: emission shape -----

        [Fact]
        public void Compile_SingleConditionFileInto_EmitsExpectedScript()
        {
            var rule = new SieveRule
            {
                Id = Guid.NewGuid(),
                Name = "Alerts",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "[ALERT]") },
                Actions = { Act(SieveActionType.FileInto, "Alerts") }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsSuccess);
            var script = result.Value;
            Assert.StartsWith("# WEESKY-RULES-V1:", script);
            Assert.Contains("require [\"fileinto\"];", script);
            Assert.Contains("# Rule: Alerts", script);
            Assert.Contains("if header :contains \"Subject\" \"[ALERT]\" {", script);
            Assert.Contains("    fileinto \"Alerts\";", script);
            Assert.DoesNotContain("stop;", script);
        }

        [Fact]
        public void Compile_MultipleConditionsAllOf_EmitsAllof()
        {
            var rule = new SieveRule
            {
                Name = "From boss with urgent",
                MatchAll = true,
                Conditions =
                {
                    Cond(SieveConditionField.From, SieveConditionOperator.Contains, "boss@"),
                    Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "urgent"),
                },
                Actions = { Act(SieveActionType.FileInto, "Urgent") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("if allof (header :contains \"From\" \"boss@\", header :contains \"Subject\" \"urgent\") {", script);
        }

        [Fact]
        public void Compile_MultipleConditionsAnyOf_EmitsAnyof()
        {
            var rule = new SieveRule
            {
                Name = "Marketing",
                MatchAll = false,
                Conditions =
                {
                    Cond(SieveConditionField.From, SieveConditionOperator.Contains, "noreply"),
                    Cond(SieveConditionField.From, SieveConditionOperator.Contains, "newsletter"),
                },
                Actions = { Act(SieveActionType.FileInto, "Junk") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("if anyof (", script);
        }

        [Fact]
        public void Compile_StopAfter_EmitsStopCommand()
        {
            var rule = new SieveRule
            {
                Name = "Important",
                StopAfter = true,
                Conditions = { Cond(SieveConditionField.From, SieveConditionOperator.Equals, "ceo@weesky.be") },
                Actions = { Act(SieveActionType.FileInto, "VIP") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("    fileinto \"VIP\";", script);
            Assert.Contains("    stop;", script);
        }

        [Fact]
        public void Compile_SizeCondition_EmitsSizeOver()
        {
            var rule = new SieveRule
            {
                Name = "Big",
                Conditions = { Cond(SieveConditionField.Size, SieveConditionOperator.Larger, "1M") },
                Actions = { Act(SieveActionType.FileInto, "Large") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("if size :over 1M {", script);
        }

        [Fact]
        public void Compile_CustomHeader_EmitsHeaderName()
        {
            var rule = new SieveRule
            {
                Name = "X-Spam",
                Conditions = { new SieveCondition { Field = SieveConditionField.Header, HeaderName = "X-Spam-Flag", Operator = SieveConditionOperator.Equals, Value = "YES" } },
                Actions = { Act(SieveActionType.Discard) }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("if header :is \"X-Spam-Flag\" \"YES\" {", script);
            Assert.Contains("    discard;", script);
        }

        [Fact]
        public void Compile_AllActionKinds_EmitCorrectly()
        {
            var rule = new SieveRule
            {
                Name = "Combo",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Matches, "*") },
                Actions =
                {
                    Act(SieveActionType.FileInto, "Folder"),
                    Act(SieveActionType.Redirect, "elsewhere@example.com"),
                    Act(SieveActionType.Reject, "rejected"),
                    Act(SieveActionType.SetFlag, "\\Seen"),
                    Act(SieveActionType.Discard),
                    Act(SieveActionType.Keep),
                }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("    fileinto \"Folder\";", script);
            Assert.Contains("    redirect \"elsewhere@example.com\";", script);
            Assert.Contains("    reject \"rejected\";", script);
            Assert.Contains("    setflag \"\\\\Seen\";", script);
            Assert.Contains("    discard;", script);
            Assert.Contains("    keep;", script);
        }

        [Fact]
        public void Compile_OnlyActionsUsed_AreInRequireList()
        {
            var rule = new SieveRule
            {
                Name = "Only fileinto",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions = { Act(SieveActionType.FileInto, "X") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("require [\"fileinto\"];", script);
            Assert.DoesNotContain("imap4flags", script);
            Assert.DoesNotContain("reject", script);
        }

        [Fact]
        public void Compile_AllRequiringActions_ListExtensionsAlphabetically()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions = { Act(SieveActionType.SetFlag, "\\Seen"), Act(SieveActionType.Reject, "no"), Act(SieveActionType.FileInto, "F") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("require [\"fileinto\", \"imap4flags\", \"reject\"];", script);
        }

        [Fact]
        public void Compile_DisabledRule_NotEmittedButPreservedInMarker()
        {
            var enabled = new SieveRule
            {
                Name = "Enabled",
                Enabled = true,
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions = { Act(SieveActionType.Keep) }
            };
            var disabled = new SieveRule
            {
                Name = "Disabled",
                Enabled = false,
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "y") },
                Actions = { Act(SieveActionType.FileInto, "Bin") }
            };

            var script = _sut.Compile(new[] { enabled, disabled }).Value;

            Assert.Contains("# Rule: Enabled", script);
            Assert.DoesNotContain("# Rule: Disabled", script);
            // Disabled rule's FileInto must not appear in the require[] list — and since
            // the only enabled rule uses Keep (no extension required), there should be no require[] line at all.
            Assert.DoesNotContain("require", script);

            // But the round-trip should still return both rules.
            var parsed = _sut.Parse(script);
            Assert.Equal(2, parsed.Rules.Count);
        }

        // ----- Round-trip -----

        [Fact]
        public void RoundTrip_PreservesAllFields()
        {
            var id = Guid.NewGuid();
            var original = new SieveRule
            {
                Id = id,
                Name = "Complex",
                Enabled = true,
                MatchAll = false,
                StopAfter = true,
                Conditions =
                {
                    new SieveCondition { Field = SieveConditionField.From, Operator = SieveConditionOperator.Contains, Value = "boss" },
                    new SieveCondition { Field = SieveConditionField.Header, HeaderName = "X-Project", Operator = SieveConditionOperator.Equals, Value = "Alpha" },
                    new SieveCondition { Field = SieveConditionField.Size, Operator = SieveConditionOperator.Larger, Value = "500K" },
                },
                Actions =
                {
                    new SieveAction { Type = SieveActionType.FileInto, Argument = "VIP" },
                    new SieveAction { Type = SieveActionType.SetFlag, Argument = "\\Flagged" },
                }
            };

            var script = _sut.Compile(new[] { original }).Value;
            var parsed = _sut.Parse(script);

            Assert.Equal(SieveScriptKind.Structured, parsed.Kind);
            var roundTrip = Assert.Single(parsed.Rules);
            Assert.Equal(id, roundTrip.Id);
            Assert.Equal("Complex", roundTrip.Name);
            Assert.True(roundTrip.Enabled);
            Assert.False(roundTrip.MatchAll);
            Assert.True(roundTrip.StopAfter);
            Assert.Equal(3, roundTrip.Conditions.Count);
            Assert.Equal(SieveConditionField.Header, roundTrip.Conditions[1].Field);
            Assert.Equal("X-Project", roundTrip.Conditions[1].HeaderName);
            Assert.Equal(SieveConditionField.Size, roundTrip.Conditions[2].Field);
            Assert.Equal("500K", roundTrip.Conditions[2].Value);
            Assert.Equal(2, roundTrip.Actions.Count);
            Assert.Equal("\\Flagged", roundTrip.Actions[1].Argument);
        }

        [Fact]
        public void RoundTrip_EmptyRules_ProducesParsableScript()
        {
            var script = _sut.Compile(Array.Empty<SieveRule>()).Value;
            var parsed = _sut.Parse(script);

            Assert.Equal(SieveScriptKind.Structured, parsed.Kind);
            Assert.Empty(parsed.Rules);
        }

        // ----- Parse -----

        [Fact]
        public void Parse_EmptyScript_ReturnsStructuredEmpty()
        {
            var parsed = _sut.Parse(string.Empty);

            Assert.Equal(SieveScriptKind.Structured, parsed.Kind);
            Assert.Empty(parsed.Rules);
        }

        [Fact]
        public void Parse_NoMarker_ReturnsAdvanced()
        {
            var parsed = _sut.Parse("require [\"fileinto\"];\nif true { keep; }");

            Assert.Equal(SieveScriptKind.Advanced, parsed.Kind);
            Assert.Empty(parsed.Rules);
        }

        [Fact]
        public void Parse_CorruptedMarker_FallsBackToAdvanced()
        {
            var parsed = _sut.Parse("# WEESKY-RULES-V1:!!!not-base64!!!\nrequire [\"fileinto\"];");

            Assert.Equal(SieveScriptKind.Advanced, parsed.Kind);
        }

        [Fact]
        public void Parse_AcceptsCrLfLineEnding()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions = { Act(SieveActionType.Keep) }
            };
            var script = _sut.Compile(new[] { rule }).Value.Replace("\n", "\r\n");

            var parsed = _sut.Parse(script);

            Assert.Equal(SieveScriptKind.Structured, parsed.Kind);
            Assert.Single(parsed.Rules);
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
        public void Compile_RuleWithoutConditions_Fails()
        {
            var rule = new SieveRule { Name = "x", Actions = { Act(SieveActionType.Keep) } };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("condition", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Compile_RuleWithoutActions_Fails()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("action", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Compile_FileIntoWithoutArgument_Fails()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions = { new SieveAction { Type = SieveActionType.FileInto } }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("FileInto", result.Error);
        }

        [Fact]
        public void Compile_HeaderConditionWithoutHeaderName_Fails()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { new SieveCondition { Field = SieveConditionField.Header, Operator = SieveConditionOperator.Contains, Value = "y" } },
                Actions = { Act(SieveActionType.Keep) }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("Header name", result.Error);
        }

        [Fact]
        public void Compile_SizeWithContainsOperator_Fails()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { new SieveCondition { Field = SieveConditionField.Size, Operator = SieveConditionOperator.Contains, Value = "1M" } },
                Actions = { Act(SieveActionType.Keep) }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("Size", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Compile_TextFieldWithLargerOperator_Fails()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Larger, Value = "1" } },
                Actions = { Act(SieveActionType.Keep) }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void Compile_InvalidSizeValue_Fails()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { new SieveCondition { Field = SieveConditionField.Size, Operator = SieveConditionOperator.Larger, Value = "huge" } },
                Actions = { Act(SieveActionType.Keep) }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
        }

        // ----- Quoting -----

        [Fact]
        public void Compile_ValueWithQuoteAndBackslash_IsEscaped()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "she said \"hi\\there\"") },
                Actions = { Act(SieveActionType.Keep) }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("\"she said \\\"hi\\\\there\\\"\"", script);
        }

        // ----- Helpers -----

        private static SieveCondition Cond(SieveConditionField field, SieveConditionOperator op, string value) =>
            new() { Field = field, Operator = op, Value = value };

        private static SieveAction Act(SieveActionType type, string? arg = null) =>
            new() { Type = type, Argument = arg };
    }
}
