using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.RuleProviders.Rainloop;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.RuleProviders
{
    public class RainloopRuleProviderTests
    {
        private readonly RainloopRuleProvider _sut = new();

        // ----- Identity -----

        [Fact]
        public void Metadata_ReportsExpectedValues()
        {
            Assert.Equal("rainloop", _sut.Id);
            Assert.Equal("rainloop.user", _sut.DefaultScriptName);
        }

        // ----- CanHandle -----

        [Fact]
        public void CanHandle_WithRainloopMarker_ReturnsTrue()
        {
            var script = "require [\"fileinto\"];\n# RAINLOOP:SIEVE\n/*\nBEGIN:FILTER:abc\n";
            Assert.True(_sut.CanHandle(script));
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

        // ----- Round trip via real-world-shaped rules -----

        [Fact]
        public void RoundTrip_MoveToWithMarkAsReadAndStop_PreservesStructure()
        {
            var original = new SieveRule
            {
                Id = Guid.NewGuid(),
                Name = "Zik",
                MatchAll = false,
                StopAfter = true,
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "[ZIK]") },
                Actions =
                {
                    Act(SieveActionType.SetFlag, @"\Seen"),
                    Act(SieveActionType.FileInto, "Zik")
                }
            };

            var script = _sut.Compile(new[] { original }).Value;
            var rules = _sut.Parse(script).Value;
            var rt = Assert.Single(rules);

            Assert.Equal("Zik", rt.Name);
            Assert.True(rt.StopAfter);
            Assert.Single(rt.Conditions);
            Assert.Equal(SieveConditionField.Subject, rt.Conditions[0].Field);
            Assert.Equal("[ZIK]", rt.Conditions[0].Value);
            Assert.Equal(2, rt.Actions.Count);
            Assert.Equal(SieveActionType.SetFlag, rt.Actions[0].Type);
            Assert.Equal(@"\Seen", rt.Actions[0].Argument);
            Assert.Equal(SieveActionType.FileInto, rt.Actions[1].Type);
            Assert.Equal("Zik", rt.Actions[1].Argument);
        }

        [Fact]
        public void RoundTrip_MultiConditionAnyofRecipient_PreservesAll()
        {
            var original = new SieveRule
            {
                Id = Guid.NewGuid(),
                Name = "e-commerce",
                MatchAll = false,
                StopAfter = true,
                Conditions =
                {
                    Cond(SieveConditionField.Recipient, SieveConditionOperator.Contains, "darth_amazon"),
                    Cond(SieveConditionField.Recipient, SieveConditionOperator.Contains, "darth_ebay"),
                    Cond(SieveConditionField.From, SieveConditionOperator.Contains, "labelleiloise.fr"),
                },
                Actions = { Act(SieveActionType.FileInto, "e-commerce") }
            };

            var script = _sut.Compile(new[] { original }).Value;
            var rules = _sut.Parse(script).Value;
            var rt = Assert.Single(rules);

            Assert.Equal(3, rt.Conditions.Count);
            Assert.Equal(SieveConditionField.Recipient, rt.Conditions[0].Field);
            Assert.Equal(SieveConditionField.From, rt.Conditions[2].Field);
            Assert.True(rt.StopAfter);
        }

        [Fact]
        public void RoundTrip_ForwardWithKeep_AddsInboxFileintoAndRecoversAfterParse()
        {
            var original = new SieveRule
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "[TEST]") },
                Actions =
                {
                    Act(SieveActionType.FileInto, "INBOX"),
                    Act(SieveActionType.Redirect, "darth@skynet.be")
                },
                StopAfter = true
            };

            var script = _sut.Compile(new[] { original }).Value;
            Assert.Contains("fileinto \"INBOX\";", script);
            Assert.Contains("redirect \"darth@skynet.be\";", script);

            var rules = _sut.Parse(script).Value;
            var rt = Assert.Single(rules);

            // The fileinto INBOX companion is preserved in the action list (Rainloop reconstructs it).
            Assert.Contains(rt.Actions, a => a.Type == SieveActionType.FileInto && a.Argument == "INBOX");
            Assert.Contains(rt.Actions, a => a.Type == SieveActionType.Redirect && a.Argument == "darth@skynet.be");
        }

        [Fact]
        public void Compile_RuleWithMultipleFileIntoActions_Rejected()
        {
            var rule = new SieveRule
            {
                Name = "Bad",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions =
                {
                    Act(SieveActionType.FileInto, "A"),
                    Act(SieveActionType.FileInto, "B")
                }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("only supports one primary action", result.Error);
        }

        [Fact]
        public void Compile_RuleWithFileIntoAndRedirect_Rejected()
        {
            var rule = new SieveRule
            {
                Name = "Bad",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions =
                {
                    Act(SieveActionType.FileInto, "X"),       // not the INBOX companion
                    Act(SieveActionType.Redirect, "y@z.com")
                }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void Compile_RuleWithoutPrimaryAction_Rejected()
        {
            var rule = new SieveRule
            {
                Name = "Bad",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions = { Act(SieveActionType.SetFlag, @"\Seen"), Act(SieveActionType.Keep) }
            };

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("exactly one", result.Error);
        }

        // ----- Compile output shape -----

        [Fact]
        public void Compile_EmitsRainloopHeaderComments()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions = { Act(SieveActionType.FileInto, "X") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("# This is RainLoop Webmail sieve script.", script);
            Assert.Contains("# RAINLOOP:SIEVE", script);
            Assert.Contains("BEGIN:FILTER:", script);
            Assert.Contains("BEGIN:HEADER", script);
            Assert.Contains("END:HEADER", script);
            Assert.Contains("/* END:FILTER */", script);
        }

        [Fact]
        public void Compile_RequiresImap4flagsWhenMarkAsRead()
        {
            var rule = new SieveRule
            {
                Name = "x",
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                Actions = { Act(SieveActionType.SetFlag, @"\Seen"), Act(SieveActionType.FileInto, "X") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("require [\"fileinto\", \"imap4flags\"];", script);
        }

        // ----- Disabled rules -----

        [Fact]
        public void Compile_DisabledRule_EmitsFilterBlockWithDisabledComment()
        {
            var rule = new SieveRule
            {
                Name = "Off",
                Enabled = false,
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "[Off]") },
                Actions = { Act(SieveActionType.FileInto, "Junk") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("BEGIN:FILTER:", script);
            Assert.Contains("/* @Filter is disabled", script);
            Assert.Contains("fileinto \"Junk\";", script);
            Assert.Contains("/* END:FILTER */", script);
        }

        [Fact]
        public void Compile_DisabledRule_SieveCodeIsInsideComment()
        {
            var rule = new SieveRule
            {
                Name = "Off",
                Enabled = false,
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "[Off]") },
                Actions = { Act(SieveActionType.FileInto, "Junk") }
            };

            var script = _sut.Compile(new[] { rule }).Value;

            var disabledStart = script.IndexOf("/* @Filter is disabled", StringComparison.Ordinal);
            var disabledEnd = script.IndexOf("*/", disabledStart + 1, StringComparison.Ordinal);
            var fileIntoPos = script.IndexOf("fileinto \"Junk\";", StringComparison.Ordinal);

            Assert.True(disabledStart >= 0);
            Assert.True(fileIntoPos > disabledStart && fileIntoPos < disabledEnd,
                "fileinto instruction should be inside the /* @Filter is disabled ... */ block");
        }

        [Fact]
        public void RoundTrip_DisabledRule_PreservesEnabledFalse()
        {
            var rule = new SieveRule
            {
                Id = Guid.NewGuid(),
                Name = "Off",
                Enabled = false,
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "[Off]") },
                Actions = { Act(SieveActionType.FileInto, "Junk") }
            };

            var script = _sut.Compile(new[] { rule }).Value;
            var rules = _sut.Parse(script).Value;
            var rt = Assert.Single(rules);

            Assert.False(rt.Enabled);
            Assert.Equal("Off", rt.Name);
        }

        [Fact]
        public void Compile_MixedEnabledAndDisabled_EmitsBothBlocks()
        {
            var enabled = new SieveRule
            {
                Name = "On",
                Enabled = true,
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "[On]") },
                Actions = { Act(SieveActionType.FileInto, "Inbox") }
            };
            var disabled = new SieveRule
            {
                Name = "Off",
                Enabled = false,
                Conditions = { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "[Off]") },
                Actions = { Act(SieveActionType.FileInto, "Junk") }
            };

            var script = _sut.Compile(new[] { enabled, disabled }).Value;
            var rules = _sut.Parse(script).Value;

            Assert.Equal(2, rules.Count);
            Assert.True(rules[0].Enabled);
            Assert.False(rules[1].Enabled);
        }

        // ----- Parse failure paths -----

        [Fact]
        public void Parse_MalformedBlock_ReturnsFailure()
        {
            var script = "# RAINLOOP:SIEVE\n/*\nBEGIN:FILTER:abc\n*/\n";
            var result = _sut.Parse(script);
            Assert.True(result.IsFailure);
        }

        [Fact]
        public void Parse_UnknownConditionField_ReturnsFailure()
        {
            // Build a JSON with a field Rainloop supports but we don't, base64 it.
            var json = "{\"ID\":\"abc\",\"Enabled\":true,\"Name\":\"x\",\"Conditions\":[{\"Field\":\"BogusField\",\"Type\":\"Contains\",\"Value\":\"v\"}],\"ConditionsType\":\"Any\",\"ActionType\":\"MoveTo\",\"ActionValue\":\"X\",\"Stop\":false,\"Keep\":false,\"MarkAsRead\":false}";
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            var script = $"# RAINLOOP:SIEVE\n/*\nBEGIN:FILTER:abc\nBEGIN:HEADER\n{b64}\nEND:HEADER\n*/\n";

            var result = _sut.Parse(script);

            Assert.True(result.IsFailure);
            Assert.Contains("BogusField", result.Error);
        }

        // ----- Helpers -----

        private static SieveCondition Cond(SieveConditionField f, SieveConditionOperator o, string v) =>
            new() { Field = f, Operator = o, Value = v };

        private static SieveAction Act(SieveActionType t, string? arg = null) =>
            new() { Type = t, Argument = arg };
    }
}
