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

        // ----- Regex operator -----

        [Fact]
        public void Compile_RegexOperator_EmitsRegexMatchTypeAndRequire()
        {
            var rule = MakeRule("Regex",
                new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Regex, Value = "^ALERT" },
                Act(SieveActionType.FileInto, "Alerts"));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("\"regex\"", script);
            Assert.Contains("header :regex \"Subject\" \"^ALERT\"", script);
        }

        [Fact]
        public void Compile_RegexOnBody_EmitsBodyRegex()
        {
            var rule = MakeRule("BodyRegex",
                new SieveCondition { Field = SieveConditionField.Body, Operator = SieveConditionOperator.Regex, Value = "casino|lottery" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("body :text :regex \"casino|lottery\"", script);
            Assert.Contains("\"body\"", script);
            Assert.Contains("\"regex\"", script);
        }

        // ----- :create (mailbox) -----

        [Fact]
        public void Compile_FileIntoCreate_EmitsCreateFlagAndMailboxRequire()
        {
            var rule = MakeRule("Create",
                Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x"),
                new SieveAction { Type = SieveActionType.FileInto, Argument = "NewFolder", AutoCreate = true });

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("\"mailbox\"", script);
            Assert.Contains("fileinto :create \"NewFolder\";", script);
        }

        [Fact]
        public void Compile_FileIntoWithoutCreate_DoesNotEmitCreateFlag()
        {
            var rule = MakeRule("NoCreate",
                Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x"),
                Act(SieveActionType.FileInto, "Folder"));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.DoesNotContain(":create", script);
            Assert.DoesNotContain("\"mailbox\"", script);
        }

        // ----- duplicate -----

        [Fact]
        public void Compile_DuplicateCondition_EmitsDuplicateTestAndRequire()
        {
            var rule = MakeRule("Dedup",
                new SieveCondition { Field = SieveConditionField.Duplicate, Operator = SieveConditionOperator.Contains, Value = "" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("\"duplicate\"", script);
            Assert.Contains("if duplicate {", script);
        }

        [Fact]
        public void Compile_DuplicateConditionWithSeconds_EmitsSecondsParameter()
        {
            var rule = MakeRule("DedupHour",
                new SieveCondition { Field = SieveConditionField.Duplicate, Operator = SieveConditionOperator.Contains, Value = "3600" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("if duplicate :seconds 3600 {", script);
        }

        [Fact]
        public void Compile_DuplicateConditionWithInvalidSeconds_Fails()
        {
            var rule = MakeRule("Bad",
                new SieveCondition { Field = SieveConditionField.Duplicate, Operator = SieveConditionOperator.Contains, Value = "not-a-number" },
                Act(SieveActionType.Discard));

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("positive integer", result.Error);
        }

        // ----- SetFlag emission -----

        [Fact]
        public void Compile_SetFlagSeen_EmitsAddflag()
        {
            // Must use addflag (not setflag) so multiple flag actions don't clobber each other.
            var rule = MakeRule("Read",
                Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x"),
                Act(SieveActionType.SetFlag, @"\Seen"));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("addflag \"\\\\Seen\";", script);
            Assert.DoesNotContain("setflag", script);
        }

        [Fact]
        public void Compile_SeenAndFlaggedTogether_BothFlagsPresent()
        {
            var rule = MakeRule("ReadAndFlag",
                new[] { Cond(SieveConditionField.Subject, SieveConditionOperator.Contains, "x") },
                new[]
                {
                    Act(SieveActionType.SetFlag, @"\Seen"),
                    Act(SieveActionType.SetFlag, @"\Flagged"),
                    Act(SieveActionType.FileInto, "X"),
                });

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("addflag \"\\\\Seen\";", script);
            Assert.Contains("addflag \"\\\\Flagged\";", script);
        }

        // ----- Body condition -----

        [Fact]
        public void Compile_BodyContains_EmitsBodyExtensionAndRequire()
        {
            var rule = MakeRule("Spam",
                new SieveCondition { Field = SieveConditionField.Body, Operator = SieveConditionOperator.Contains, Value = "casino" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("require [\"body\"];", script);
            Assert.Contains("body :text :contains \"casino\"", script);
        }

        [Fact]
        public void Compile_BodyMatches_EmitsMatchesKeyword()
        {
            var rule = MakeRule("Wildcard",
                new SieveCondition { Field = SieveConditionField.Body, Operator = SieveConditionOperator.Matches, Value = "*win*" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("body :text :matches \"*win*\"", script);
        }

        [Fact]
        public void Compile_BodyWithEqualsOperator_Fails()
        {
            var rule = MakeRule("Bad",
                new SieveCondition { Field = SieveConditionField.Body, Operator = SieveConditionOperator.Equals, Value = "x" },
                Act(SieveActionType.Discard));

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("Body", result.Error);
        }

        // ----- Envelope / subaddress conditions -----

        [Fact]
        public void Compile_EnvelopeFrom_EmitsEnvelopeFromTest()
        {
            var rule = MakeRule("NoReply",
                new SieveCondition { Field = SieveConditionField.EnvelopeFrom, Operator = SieveConditionOperator.Contains, Value = "noreply@" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("envelope :contains \"from\" \"noreply@\"", script);
            Assert.DoesNotContain("require", script); // envelope is core, no require
        }

        [Fact]
        public void Compile_EnvelopeTo_EmitsEnvelopeToTest()
        {
            var rule = MakeRule("ToMe",
                new SieveCondition { Field = SieveConditionField.EnvelopeTo, Operator = SieveConditionOperator.Equals, Value = "me@example.com" },
                Act(SieveActionType.FileInto, "Me"));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("envelope :is \"to\" \"me@example.com\"", script);
        }

        [Fact]
        public void Compile_RecipientDetail_EmitsSubaddressRequireAndAddressDetail()
        {
            var rule = MakeRule("Tagged",
                new SieveCondition { Field = SieveConditionField.RecipientDetail, Operator = SieveConditionOperator.Equals, Value = "support" },
                Act(SieveActionType.FileInto, "Support"));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("\"subaddress\"", script);
            Assert.Contains("address :detail :is [\"To\", \"Cc\"] \"support\"", script);
        }

        // ----- Date / currentdate conditions -----

        [Fact]
        public void Compile_CurrentDateBefore_EmitsCurrentdateValueLtAndRequires()
        {
            var rule = MakeRule("Expire",
                new SieveCondition { Field = SieveConditionField.CurrentDate, Operator = SieveConditionOperator.Before, Value = "2026-12-31" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("\"date\"", script);
            Assert.Contains("\"relational\"", script);
            Assert.Contains("currentdate :value \"lt\" \"date\" \"2026-12-31\"", script);
        }

        [Fact]
        public void Compile_CurrentDateOnOrAfter_EmitsValueGe()
        {
            var rule = MakeRule("Active",
                new SieveCondition { Field = SieveConditionField.CurrentDate, Operator = SieveConditionOperator.OnOrAfter, Value = "2026-01-01" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("currentdate :value \"ge\" \"date\" \"2026-01-01\"", script);
            Assert.Contains("\"relational\"", script);
        }

        [Fact]
        public void Compile_CurrentDateEquals_EmitsIsWithoutRelational()
        {
            var rule = MakeRule("Today",
                new SieveCondition { Field = SieveConditionField.CurrentDate, Operator = SieveConditionOperator.Equals, Value = "2026-06-07" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("currentdate :is \"date\" \"2026-06-07\"", script);
            Assert.DoesNotContain("\"relational\"", script);
        }

        [Fact]
        public void Compile_MessageDateBefore_EmitsDateTestWithHeaderName()
        {
            var rule = MakeRule("OldMail",
                new SieveCondition { Field = SieveConditionField.MessageDate, Operator = SieveConditionOperator.Before, Value = "2026-01-01" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("\"date\"", script);
            Assert.Contains("\"relational\"", script);
            Assert.Contains("date :value \"lt\" \"date\" \"Date\" \"2026-01-01\"", script);
        }

        [Fact]
        public void Compile_MessageDateOnOrAfter_EmitsValueGe()
        {
            var rule = MakeRule("NewMail",
                new SieveCondition { Field = SieveConditionField.MessageDate, Operator = SieveConditionOperator.OnOrAfter, Value = "2026-06-01" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("date :value \"ge\" \"date\" \"Date\" \"2026-06-01\"", script);
        }

        [Fact]
        public void Compile_DateWithInvalidFormat_Fails()
        {
            var rule = MakeRule("Bad",
                new SieveCondition { Field = SieveConditionField.CurrentDate, Operator = SieveConditionOperator.Before, Value = "31/12/2026" },
                Act(SieveActionType.Discard));

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("YYYY-MM-DD", result.Error);
        }

        [Fact]
        public void Compile_DateWithWrongOperator_Fails()
        {
            var rule = MakeRule("Bad",
                new SieveCondition { Field = SieveConditionField.CurrentDate, Operator = SieveConditionOperator.Contains, Value = "2026-06-07" },
                Act(SieveActionType.Discard));

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("Before", result.Error);
        }

        [Fact]
        public void Compile_BeforeOperatorOnNonDateField_Fails()
        {
            var rule = MakeRule("Bad",
                new SieveCondition { Field = SieveConditionField.Subject, Operator = SieveConditionOperator.Before, Value = "test" },
                Act(SieveActionType.Discard));

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("Before", result.Error);
        }

        // ----- Weekday condition -----

        [Fact]
        public void Compile_CurrentWeekday_SingleDay_EmitsIs()
        {
            var rule = MakeRule("Monday",
                new SieveCondition { Field = SieveConditionField.CurrentWeekday, Operator = SieveConditionOperator.Contains, Value = "1" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("\"date\"", script);
            Assert.DoesNotContain("\"relational\"", script);
            Assert.Contains("currentdate :is \"weekday\" \"1\"", script);
        }

        [Fact]
        public void Compile_CurrentWeekday_Weekdays_EmitsList()
        {
            var rule = MakeRule("Workweek",
                new SieveCondition { Field = SieveConditionField.CurrentWeekday, Operator = SieveConditionOperator.Contains, Value = "1,2,3,4,5" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("currentdate :is \"weekday\" [\"1\", \"2\", \"3\", \"4\", \"5\"]", script);
        }

        [Fact]
        public void Compile_CurrentWeekday_Weekend_EmitsList()
        {
            var rule = MakeRule("Weekend",
                new SieveCondition { Field = SieveConditionField.CurrentWeekday, Operator = SieveConditionOperator.Contains, Value = "0,6" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("currentdate :is \"weekday\" [\"0\", \"6\"]", script);
        }

        [Fact]
        public void Compile_CurrentWeekday_InvalidValue_Fails()
        {
            var rule = MakeRule("Bad",
                new SieveCondition { Field = SieveConditionField.CurrentWeekday, Operator = SieveConditionOperator.Contains, Value = "8" },
                Act(SieveActionType.Discard));

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("weekday", result.Error.ToLower());
        }

        // ----- Hour condition -----

        [Fact]
        public void Compile_CurrentHourBefore_EmitsCurrentdateHourValueLt()
        {
            var rule = MakeRule("BeforeNoon",
                new SieveCondition { Field = SieveConditionField.CurrentHour, Operator = SieveConditionOperator.Before, Value = "12" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("\"date\"", script);
            Assert.Contains("\"relational\"", script);
            Assert.Contains("currentdate :value \"lt\" \"hour\" \"12\"", script);
        }

        [Fact]
        public void Compile_CurrentHourOnOrAfter_EmitsValueGe()
        {
            var rule = MakeRule("AfterWork",
                new SieveCondition { Field = SieveConditionField.CurrentHour, Operator = SieveConditionOperator.OnOrAfter, Value = "18" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("currentdate :value \"ge\" \"hour\" \"18\"", script);
        }

        [Fact]
        public void Compile_CurrentHourEquals_EmitsIsWithoutRelational()
        {
            var rule = MakeRule("Noon",
                new SieveCondition { Field = SieveConditionField.CurrentHour, Operator = SieveConditionOperator.Equals, Value = "12" },
                Act(SieveActionType.Discard));

            var script = _sut.Compile(new[] { rule }).Value;

            Assert.Contains("currentdate :is \"hour\" \"12\"", script);
            Assert.DoesNotContain("\"relational\"", script);
        }

        [Fact]
        public void Compile_CurrentHour_InvalidValue_Fails()
        {
            var rule = MakeRule("Bad",
                new SieveCondition { Field = SieveConditionField.CurrentHour, Operator = SieveConditionOperator.Before, Value = "25" },
                Act(SieveActionType.Discard));

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("0 and 23", result.Error);
        }

        [Fact]
        public void Compile_CurrentHour_WrongOperator_Fails()
        {
            var rule = MakeRule("Bad",
                new SieveCondition { Field = SieveConditionField.CurrentHour, Operator = SieveConditionOperator.Contains, Value = "9" },
                Act(SieveActionType.Discard));

            var result = _sut.Compile(new[] { rule });

            Assert.True(result.IsFailure);
            Assert.Contains("Before", result.Error);
        }

        // ----- CanRepresent (superset) -----

        [Fact]
        public void CanRepresent_ExtendedFlagAndMultipleActions_Succeeds()
        {
            var rule = MakeRule(
                "extended",
                new[] { Cond(SieveConditionField.From, SieveConditionOperator.Contains, "a@b.c") },
                new[]
                {
                    Act(SieveActionType.SetFlag, @"\Flagged"),
                    Act(SieveActionType.FileInto, "A"),
                    Act(SieveActionType.Redirect, "y@z.com")
                });

            Assert.True(_sut.CanRepresent(rule).IsSuccess);
        }

        [Fact]
        public void CanRepresent_StructurallyInvalidRule_Fails()
        {
            var rule = new SieveRule { Name = "", Conditions = { }, Actions = { } };

            Assert.True(_sut.CanRepresent(rule).IsFailure);
        }

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
