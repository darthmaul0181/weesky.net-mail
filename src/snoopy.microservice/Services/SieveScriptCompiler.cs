using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Services
{
    public class SieveScriptCompiler : ISieveScriptCompiler
    {
        public const string MarkerPrefix = "# WEESKY-RULES-V1:";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

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

        public SieveScriptParseResult Parse(string scriptContent)
        {
            if (string.IsNullOrEmpty(scriptContent))
                return new SieveScriptParseResult { Kind = SieveScriptKind.Structured };

            var newlineIndex = scriptContent.IndexOf('\n');
            var firstLine = newlineIndex < 0 ? scriptContent : scriptContent.Substring(0, newlineIndex);
            firstLine = firstLine.TrimEnd('\r');

            if (!firstLine.StartsWith(MarkerPrefix, StringComparison.Ordinal))
                return new SieveScriptParseResult { Kind = SieveScriptKind.Advanced };

            var encoded = firstLine.Substring(MarkerPrefix.Length).Trim();
            try
            {
                var bytes = Convert.FromBase64String(encoded);
                var json = Encoding.UTF8.GetString(bytes);
                var payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions);
                return new SieveScriptParseResult
                {
                    Kind = SieveScriptKind.Structured,
                    Rules = payload?.Rules ?? Array.Empty<SieveRule>()
                };
            }
            catch
            {
                // Marker was present but corrupted — fall back to raw editing rather than throwing.
                return new SieveScriptParseResult { Kind = SieveScriptKind.Advanced };
            }
        }

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

            if (isSize && !isSizeOp) return Result.Failure("Size condition requires the Larger or Smaller operator");
            if (!isSize && isSizeOp) return Result.Failure("Larger/Smaller operator is only valid for the Size field");

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
                        case SieveActionType.FileInto: set.Add("fileinto"); break;
                        case SieveActionType.Reject: set.Add("reject"); break;
                        case SieveActionType.SetFlag: set.Add("imap4flags"); break;
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

            var matchOp = c.Operator switch
            {
                SieveConditionOperator.Contains => ":contains",
                SieveConditionOperator.Equals => ":is",
                SieveConditionOperator.Matches => ":matches",
                _ => throw new InvalidOperationException($"Operator {c.Operator} is not valid for a text field")
            };

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
            SieveActionType.FileInto => $"fileinto {Quote(a.Argument!)};",
            SieveActionType.Redirect => $"redirect {Quote(a.Argument!)};",
            SieveActionType.Discard => "discard;",
            SieveActionType.Reject => $"reject {Quote(a.Argument!)};",
            SieveActionType.SetFlag => $"setflag {Quote(a.Argument!)};",
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

        private sealed record Payload(IReadOnlyList<SieveRule> Rules);
    }
}
