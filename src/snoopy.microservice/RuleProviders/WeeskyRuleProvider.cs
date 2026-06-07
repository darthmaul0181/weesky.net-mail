using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.RuleProviders
{
    /// <summary>
    /// Native rule format for this microservice. The script begins with a marker comment
    /// <c># WEESKY-RULES-V1:&lt;base64 JSON&gt;</c> that round-trips the structured model,
    /// followed by a minimal <c>require[]</c> and a Sieve <c>if</c> block per enabled rule.
    /// </summary>
    public class WeeskyRuleProvider : IRuleProvider
    {
        public const string MarkerPrefix = "# WEESKY-RULES-V1:";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public string Id => "weesky";
        public string DisplayName => "weesky.net";
        public string DefaultScriptName => "weesky-rules";

        public bool CanHandle(string scriptContent)
        {
            if (string.IsNullOrEmpty(scriptContent)) return false;
            var firstLine = ExtractFirstLine(scriptContent);
            return firstLine.StartsWith(MarkerPrefix, StringComparison.Ordinal);
        }

        public Result<IReadOnlyList<SieveRule>> Parse(string scriptContent)
        {
            if (string.IsNullOrEmpty(scriptContent))
                return Result.Success<IReadOnlyList<SieveRule>>(Array.Empty<SieveRule>());

            var firstLine = ExtractFirstLine(scriptContent);
            if (!firstLine.StartsWith(MarkerPrefix, StringComparison.Ordinal))
                return Result.Failure<IReadOnlyList<SieveRule>>("Script does not contain the WEESKY-RULES marker");

            var encoded = firstLine.Substring(MarkerPrefix.Length).Trim();
            try
            {
                var bytes = Convert.FromBase64String(encoded);
                var json = Encoding.UTF8.GetString(bytes);
                var payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions);
                return Result.Success<IReadOnlyList<SieveRule>>(payload?.Rules ?? Array.Empty<SieveRule>());
            }
            catch (Exception ex)
            {
                return Result.Failure<IReadOnlyList<SieveRule>>($"Unable to decode WEESKY-RULES marker: {ex.Message}");
            }
        }

        public Result<string> Compile(IReadOnlyList<SieveRule> rules)
        {
            if (rules == null) return Result.Failure<string>("Rules collection is required");

            for (int i = 0; i < rules.Count; i++)
            {
                var v = Validate(rules[i]);
                if (v.IsFailure)
                {
                    var label = string.IsNullOrWhiteSpace(rules[i].Name) ? $"rule #{i + 1}" : $"rule '{rules[i].Name}'";
                    return Result.Failure<string>($"Invalid {label}: {v.Error}");
                }
            }

            var json = JsonSerializer.Serialize(new Payload(rules), JsonOptions);
            var marker = MarkerPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            var requires = ComputeRequires(rules);

            var sb = new StringBuilder();
            sb.Append(marker).Append('\n');
            if (requires.Count > 0)
            {
                sb.Append("require [");
                sb.Append(string.Join(", ", requires.Select(Quote)));
                sb.Append("];\n");
            }
            sb.Append('\n');

            foreach (var rule in rules.Where(r => r.Enabled))
            {
                EmitRule(sb, rule);
            }

            return Result.Success(sb.ToString());
        }

        /// <summary>
        /// The Weesky format is a superset of every supported rule feature, so it can
        /// represent any rule that compiles. We surface compile-level validation here so a
        /// structurally invalid rule (e.g. missing name) is reported rather than silently
        /// passing a compatibility check.
        /// </summary>
        public Result CanRepresent(SieveRule rule) => Validate(rule);

        // ---------- Validation ----------

        private static Result Validate(SieveRule rule)
        {
            if (rule == null) return Result.Failure("Rule is null");
            if (string.IsNullOrWhiteSpace(rule.Name)) return Result.Failure("Name is required");
            if (rule.Conditions == null || rule.Conditions.Count == 0) return Result.Failure("At least one condition is required");
            if (rule.Actions == null || rule.Actions.Count == 0) return Result.Failure("At least one action is required");

            foreach (var c in rule.Conditions)
            {
                var cv = ValidateCondition(c);
                if (cv.IsFailure) return cv;
            }
            foreach (var a in rule.Actions)
            {
                var av = ValidateAction(a);
                if (av.IsFailure) return av;
            }
            return Result.Success();
        }

        private static Result ValidateCondition(SieveCondition c)
        {
            bool isSize = c.Field == SieveConditionField.Size;
            bool isSizeOp = c.Operator == SieveConditionOperator.Larger || c.Operator == SieveConditionOperator.Smaller;
            bool isDateField = c.Field == SieveConditionField.CurrentDate || c.Field == SieveConditionField.MessageDate;
            bool isDateOp = c.Operator == SieveConditionOperator.Before || c.Operator == SieveConditionOperator.OnOrAfter;

            if (isSize && !isSizeOp) return Result.Failure("Size condition requires the Larger or Smaller operator");
            if (!isSize && isSizeOp) return Result.Failure("Larger/Smaller operator is only valid for the Size field");
            if (!isDateField && isDateOp) return Result.Failure("Before/OnOrAfter operator is only valid for date fields");

            if (isDateField)
            {
                if (!isDateOp && c.Operator != SieveConditionOperator.Equals)
                    return Result.Failure("Date condition requires Before, OnOrAfter or Equals operator");
                if (string.IsNullOrWhiteSpace(c.Value))
                    return Result.Failure("Date condition requires a value");
                if (!DateOnly.TryParseExact(c.Value.Trim(), "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _))
                    return Result.Failure("Date value must be in YYYY-MM-DD format (e.g. 2026-06-07)");
                return Result.Success();
            }

            if (c.Field == SieveConditionField.Duplicate)
            {
                if (isSizeOp) return Result.Failure("Larger/Smaller operator is not valid for the Duplicate condition");
                if (!string.IsNullOrWhiteSpace(c.Value) &&
                    (!long.TryParse(c.Value.Trim(), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var secs) || secs <= 0))
                    return Result.Failure("Duplicate :seconds value must be a positive integer");
                return Result.Success();
            }

            if (c.Field == SieveConditionField.Body &&
                c.Operator != SieveConditionOperator.Contains &&
                c.Operator != SieveConditionOperator.Matches &&
                c.Operator != SieveConditionOperator.Regex)
                return Result.Failure("Body condition only supports the Contains, Matches or Regex operator");

            if (c.Field == SieveConditionField.Header && string.IsNullOrWhiteSpace(c.HeaderName))
                return Result.Failure("Header name is required when matching a custom header");

            if (string.IsNullOrEmpty(c.Value)) return Result.Failure("Condition value is required");

            if (isSize && !IsValidSize(c.Value)) return Result.Failure($"Invalid size value: {c.Value}");
            return Result.Success();
        }

        private static Result ValidateAction(SieveAction a)
        {
            switch (a.Type)
            {
                case SieveActionType.FileInto:
                case SieveActionType.Redirect:
                case SieveActionType.Reject:
                case SieveActionType.SetFlag:
                    if (string.IsNullOrEmpty(a.Argument))
                        return Result.Failure($"{a.Type} action requires an argument");
                    break;
                case SieveActionType.Discard:
                case SieveActionType.Keep:
                    break;
            }
            return Result.Success();
        }

        private static bool IsValidSize(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var trimmed = s.Trim();
            var suffix = trimmed[^1];
            var digits = suffix is 'K' or 'M' or 'G' or 'k' or 'm' or 'g'
                ? trimmed[..^1]
                : trimmed;
            return long.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0;
        }

        // ---------- Emission ----------

        private static IReadOnlyList<string> ComputeRequires(IReadOnlyList<SieveRule> rules)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules.Where(r => r.Enabled))
            {
                foreach (var a in rule.Actions)
                {
                    switch (a.Type)
                    {
                        case SieveActionType.FileInto:
                            set.Add("fileinto");
                            if (a.AutoCreate) set.Add("mailbox");
                            break;
                        case SieveActionType.Reject: set.Add("reject"); break;
                        case SieveActionType.SetFlag: set.Add("imap4flags"); break;
                    }
                }
                foreach (var c in rule.Conditions)
                {
                    if (c.Field == SieveConditionField.Body) set.Add("body");
                    if (c.Field == SieveConditionField.RecipientDetail) set.Add("subaddress");
                    if (c.Field == SieveConditionField.Duplicate) set.Add("duplicate");
                    if (c.Operator == SieveConditionOperator.Regex) set.Add("regex");
                    if (c.Field == SieveConditionField.CurrentDate || c.Field == SieveConditionField.MessageDate)
                    {
                        set.Add("date");
                        if (c.Operator == SieveConditionOperator.Before || c.Operator == SieveConditionOperator.OnOrAfter)
                            set.Add("relational");
                    }
                }
            }
            return set.ToList();
        }

        private static void EmitRule(StringBuilder sb, SieveRule rule)
        {
            sb.Append("# Rule: ").Append(EscapeForComment(rule.Name)).Append('\n');
            sb.Append("if ");
            if (rule.Conditions.Count == 1)
            {
                sb.Append(BuildCondition(rule.Conditions[0]));
            }
            else
            {
                sb.Append(rule.MatchAll ? "allof " : "anyof ");
                sb.Append('(');
                sb.Append(string.Join(", ", rule.Conditions.Select(BuildCondition)));
                sb.Append(')');
            }
            sb.Append(" {\n");
            foreach (var action in rule.Actions)
            {
                sb.Append("    ").Append(BuildAction(action)).Append('\n');
            }
            if (rule.StopAfter)
            {
                sb.Append("    stop;\n");
            }
            sb.Append("}\n\n");
        }

        private static string BuildCondition(SieveCondition c)
        {
            if (c.Field == SieveConditionField.Size)
            {
                var op = c.Operator == SieveConditionOperator.Larger ? ":over" : ":under";
                return $"size {op} {c.Value.Trim()}";
            }

            if (c.Field == SieveConditionField.Duplicate)
            {
                return string.IsNullOrWhiteSpace(c.Value)
                    ? "duplicate"
                    : $"duplicate :seconds {c.Value.Trim()}";
            }

            if (c.Field == SieveConditionField.CurrentDate || c.Field == SieveConditionField.MessageDate)
            {
                var dateValue = Quote(c.Value!.Trim());
                bool isCurrent = c.Field == SieveConditionField.CurrentDate;
                return c.Operator switch
                {
                    SieveConditionOperator.Before    => isCurrent
                        ? $"currentdate :value \"lt\" \"date\" {dateValue}"
                        : $"date :value \"lt\" \"date\" \"Date\" {dateValue}",
                    SieveConditionOperator.OnOrAfter => isCurrent
                        ? $"currentdate :value \"ge\" \"date\" {dateValue}"
                        : $"date :value \"ge\" \"date\" \"Date\" {dateValue}",
                    _                                => isCurrent
                        ? $"currentdate :is \"date\" {dateValue}"
                        : $"date :is \"date\" \"Date\" {dateValue}"
                };
            }

            if (c.Field == SieveConditionField.Body)
            {
                var bodyOp = c.Operator switch
                {
                    SieveConditionOperator.Matches => ":matches",
                    SieveConditionOperator.Regex   => ":regex",
                    _                              => ":contains"
                };
                return $"body :text {bodyOp} {Quote(c.Value)}";
            }

            var matchOp = c.Operator switch
            {
                SieveConditionOperator.Contains => ":contains",
                SieveConditionOperator.Equals   => ":is",
                SieveConditionOperator.Matches  => ":matches",
                SieveConditionOperator.Regex    => ":regex",
                _ => throw new InvalidOperationException($"Operator {c.Operator} is not valid for a text field")
            };

            if (c.Field == SieveConditionField.Recipient)
            {
                return $"header {matchOp} [\"To\", \"Cc\"] {Quote(c.Value)}";
            }

            if (c.Field == SieveConditionField.EnvelopeFrom || c.Field == SieveConditionField.EnvelopeTo)
            {
                var part = c.Field == SieveConditionField.EnvelopeFrom ? "from" : "to";
                return $"envelope {matchOp} {Quote(part)} {Quote(c.Value)}";
            }

            if (c.Field == SieveConditionField.RecipientDetail)
            {
                return $"address :detail {matchOp} [\"To\", \"Cc\"] {Quote(c.Value)}";
            }

            var headerName = c.Field switch
            {
                SieveConditionField.From => "From",
                SieveConditionField.To => "To",
                SieveConditionField.Cc => "Cc",
                SieveConditionField.Subject => "Subject",
                SieveConditionField.Header => c.HeaderName!,
                _ => throw new InvalidOperationException($"Field {c.Field} is not a text field")
            };

            return $"header {matchOp} {Quote(headerName)} {Quote(c.Value)}";
        }

        private static string BuildAction(SieveAction a) => a.Type switch
        {
            SieveActionType.FileInto => a.AutoCreate
                ? $"fileinto :create {Quote(a.Argument!)};"
                : $"fileinto {Quote(a.Argument!)};",
            SieveActionType.Redirect => $"redirect {Quote(a.Argument!)};",
            SieveActionType.Discard => "discard;",
            SieveActionType.Reject => $"reject {Quote(a.Argument!)};",
            SieveActionType.SetFlag => $"addflag {Quote(a.Argument!)};",
            SieveActionType.Keep => "keep;",
            _ => throw new InvalidOperationException($"Unknown action type {a.Type}")
        };

        private static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string EscapeForComment(string s) =>
            s.Replace("\r", " ").Replace("\n", " ");

        private static string ExtractFirstLine(string s)
        {
            var newlineIndex = s.IndexOf('\n');
            var firstLine = newlineIndex < 0 ? s : s.Substring(0, newlineIndex);
            return firstLine.TrimEnd('\r');
        }

        private sealed record Payload(IReadOnlyList<SieveRule> Rules);
    }
}
