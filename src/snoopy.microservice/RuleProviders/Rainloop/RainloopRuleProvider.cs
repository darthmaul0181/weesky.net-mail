using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.RuleProviders.Rainloop
{
    /// <summary>
    /// Rainloop / Snappymail Sieve dialect. Detects the <c># RAINLOOP:SIEVE</c> marker
    /// and round-trips through Rainloop's <c>BEGIN:FILTER</c> / <c>BEGIN:HEADER</c>
    /// blocks containing a base64-encoded JSON payload (<see cref="RainloopFilter"/>).
    /// </summary>
    public class RainloopRuleProvider : IRuleProvider
    {
        public const string MarkerLine = "# RAINLOOP:SIEVE";
        private const int Base64WrapWidth = 74;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            WriteIndented = false
        };

        private static readonly Regex BeginFilterRegex = new(@"^BEGIN:FILTER:([A-Za-z0-9]+)\s*$", RegexOptions.Compiled);

        public string Id => "rainloop";
        public string DisplayName => "Rainloop / Snappymail";
        public string DefaultScriptName => "rainloop.user";

        public bool CanHandle(string scriptContent)
        {
            if (string.IsNullOrEmpty(scriptContent)) return false;
            return scriptContent.Contains(MarkerLine, StringComparison.Ordinal);
        }

        // ---------- Parse ----------

        public Result<IReadOnlyList<SieveRule>> Parse(string scriptContent)
        {
            if (string.IsNullOrEmpty(scriptContent))
                return Result.Success<IReadOnlyList<SieveRule>>(Array.Empty<SieveRule>());

            var rules = new List<SieveRule>();
            var lines = scriptContent.Split('\n');

            int i = 0;
            while (i < lines.Length)
            {
                var line = lines[i].TrimEnd('\r').TrimStart();
                var match = BeginFilterRegex.Match(line);
                if (!match.Success) { i++; continue; }

                // Expect "BEGIN:HEADER" on a following line, then base64 lines, then "END:HEADER".
                i++;
                while (i < lines.Length && !lines[i].TrimEnd('\r').TrimStart().Equals("BEGIN:HEADER", StringComparison.Ordinal))
                    i++;
                if (i >= lines.Length)
                    return Result.Failure<IReadOnlyList<SieveRule>>("Malformed Rainloop block: missing BEGIN:HEADER");

                i++; // skip BEGIN:HEADER line
                var base64Builder = new StringBuilder();
                while (i < lines.Length)
                {
                    var raw = lines[i].TrimEnd('\r').TrimStart();
                    if (raw.Equals("END:HEADER", StringComparison.Ordinal)) break;
                    base64Builder.Append(raw);
                    i++;
                }
                if (i >= lines.Length)
                    return Result.Failure<IReadOnlyList<SieveRule>>("Malformed Rainloop block: missing END:HEADER");
                i++; // skip END:HEADER line

                var ruleResult = DecodeFilter(base64Builder.ToString());
                if (ruleResult.IsFailure)
                    return Result.Failure<IReadOnlyList<SieveRule>>(ruleResult.Error);
                rules.Add(ruleResult.Value);
            }

            return Result.Success<IReadOnlyList<SieveRule>>(rules);
        }

        private static Result<SieveRule> DecodeFilter(string base64)
        {
            RainloopFilter? filter;
            try
            {
                var bytes = Convert.FromBase64String(base64);
                var json = Encoding.UTF8.GetString(bytes);
                filter = JsonSerializer.Deserialize<RainloopFilter>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                return Result.Failure<SieveRule>($"Unable to decode Rainloop filter: {ex.Message}");
            }

            if (filter == null)
                return Result.Failure<SieveRule>("Rainloop filter decoded to null");

            var rule = new SieveRule
            {
                Id = DeriveGuidFromRainloopId(filter.ID),
                Name = filter.Name,
                Enabled = filter.Enabled,
                MatchAll = !string.Equals(filter.ConditionsType, "Any", StringComparison.Ordinal),
                StopAfter = filter.Stop
            };

            foreach (var cond in filter.Conditions)
            {
                var mapped = MapCondition(cond);
                if (mapped.IsFailure) return Result.Failure<SieveRule>(mapped.Error);
                rule.Conditions.Add(mapped.Value);
            }

            // MarkAsRead is emitted first in Sieve, but in our action list order doesn't matter for execution;
            // we put it first to mirror Rainloop's emission so round-trip preserves Sieve byte-equality.
            if (filter.MarkAsRead)
                rule.Actions.Add(new SieveAction { Type = SieveActionType.SetFlag, Argument = @"\Seen" });

            // For Forward + Keep, Rainloop emits an explicit fileinto "INBOX" before redirect.
            // We capture that intent in our model.
            if (string.Equals(filter.ActionType, "Forward", StringComparison.Ordinal) && filter.Keep)
                rule.Actions.Add(new SieveAction { Type = SieveActionType.FileInto, Argument = "INBOX" });

            var primary = MapPrimaryAction(filter);
            if (primary.IsFailure) return Result.Failure<SieveRule>(primary.Error);
            rule.Actions.Add(primary.Value);

            return Result.Success(rule);
        }

        private static Result<SieveCondition> MapCondition(RainloopCondition c)
        {
            var op = c.Type switch
            {
                "Contains" => SieveConditionOperator.Contains,
                "EqualTo" => SieveConditionOperator.Equals,
                "Regex" => SieveConditionOperator.Matches,
                "Over" => SieveConditionOperator.Larger,
                "Under" => SieveConditionOperator.Smaller,
                _ => (SieveConditionOperator?)null
            };
            if (op == null) return Result.Failure<SieveCondition>($"Unsupported Rainloop condition type: {c.Type}");

            var field = c.Field switch
            {
                "From" => SieveConditionField.From,
                "To" => SieveConditionField.To,
                "Cc" => SieveConditionField.Cc,
                "Recipient" => SieveConditionField.Recipient,
                "Subject" => SieveConditionField.Subject,
                "Header" => SieveConditionField.Header,
                "Size" => SieveConditionField.Size,
                _ => (SieveConditionField?)null
            };
            if (field == null) return Result.Failure<SieveCondition>($"Unsupported Rainloop condition field: {c.Field}");

            return Result.Success(new SieveCondition
            {
                Field = field.Value,
                Operator = op.Value,
                Value = c.Value,
                HeaderName = field == SieveConditionField.Header ? c.ValueSecond : null
            });
        }

        private static Result<SieveAction> MapPrimaryAction(RainloopFilter f) => f.ActionType switch
        {
            "MoveTo" => Result.Success(new SieveAction { Type = SieveActionType.FileInto, Argument = f.ActionValue }),
            "Forward" => Result.Success(new SieveAction { Type = SieveActionType.Redirect, Argument = f.ActionValue }),
            "Reject" => Result.Success(new SieveAction { Type = SieveActionType.Reject, Argument = f.ActionValue }),
            "Discard" => Result.Success(new SieveAction { Type = SieveActionType.Discard }),
            _ => Result.Failure<SieveAction>($"Unsupported Rainloop action type: {f.ActionType}")
        };

        // ---------- Compile ----------

        public Result<string> Compile(IReadOnlyList<SieveRule> rules)
        {
            if (rules == null) return Result.Failure<string>("Rules collection is required");

            var filters = new List<(SieveRule Rule, RainloopFilter Filter)>();
            for (int i = 0; i < rules.Count; i++)
            {
                var built = BuildFilter(rules[i]);
                if (built.IsFailure)
                {
                    var label = string.IsNullOrWhiteSpace(rules[i].Name) ? $"rule #{i + 1}" : $"rule '{rules[i].Name}'";
                    return Result.Failure<string>($"Invalid {label} for Rainloop format: {built.Error}");
                }
                filters.Add((rules[i], built.Value));
            }

            var requires = ComputeRequires(rules);

            var sb = new StringBuilder();
            sb.Append("\r\n");
            if (requires.Count > 0)
            {
                sb.Append("require [");
                sb.Append(string.Join(", ", requires.Select(QuoteSimple)));
                sb.Append("];\r\n");
            }
            sb.Append("\r\n");
            sb.Append("# This is RainLoop Webmail sieve script.\r\n");
            sb.Append("# Please don't change anything here.\r\n");
            sb.Append("# RAINLOOP:SIEVE\r\n");
            sb.Append("\r\n");

            foreach (var (rule, filter) in filters)
            {
                EmitFilterBlock(sb, rule, filter);
            }

            return Result.Success(sb.ToString());
        }

        /// <summary>
        /// Strict whitelist: a rule is representable only if every condition and action maps
        /// to the frozen Snappymail schema. Anything not explicitly supported (extended flags,
        /// multiple primary actions, new condition fields like body/envelope/subaddress) is
        /// rejected so it can never be silently dropped on a weesky→rainloop conversion.
        /// </summary>
        public Result CanRepresent(SieveRule rule)
        {
            if (rule == null) return Result.Failure("Rule is null");
            if (string.IsNullOrWhiteSpace(rule.Name)) return Result.Failure("Name is required");
            if (rule.Conditions == null || rule.Conditions.Count == 0)
                return Result.Failure("At least one condition is required");
            if (rule.Actions == null || rule.Actions.Count == 0)
                return Result.Failure("At least one action is required");

            // Conditions: only the whitelisted field/operator combinations Rainloop understands.
            foreach (var c in rule.Conditions)
            {
                var mapped = MapConditionToRainloop(c);
                if (mapped.IsFailure) return Result.Failure(mapped.Error);
            }

            // Actions: exactly one primary (Move/Forward/Reject/Discard). The "fileinto INBOX"
            // companion to a Forward+Keep is not a primary action and is ignored when counting.
            var primaries = rule.Actions
                .Where(a => a.Type is SieveActionType.FileInto or SieveActionType.Redirect or SieveActionType.Reject or SieveActionType.Discard)
                .ToList();

            if (primaries.Any(a => a.Type == SieveActionType.Redirect))
                primaries.RemoveAll(a => a.Type == SieveActionType.FileInto && a.Argument == "INBOX");

            if (primaries.Count == 0)
                return Result.Failure("The Rainloop format requires exactly one Move/Forward/Reject/Discard action");
            if (primaries.Count > 1)
                return Result.Failure("The Rainloop format only supports one primary action per rule (Move/Forward/Reject/Discard)");

            // Every action must be the recognised primary, a Keep, the Forward+Keep INBOX
            // companion, or the \Seen flag (= MarkAsRead). Anything else is unsupported.
            foreach (var a in rule.Actions)
            {
                switch (a.Type)
                {
                    case SieveActionType.FileInto:
                        if (a.AutoCreate)
                            return Result.Failure("The Rainloop format does not support auto-creating folders (:create)");
                        break;
                    case SieveActionType.Redirect:
                    case SieveActionType.Reject:
                    case SieveActionType.Discard:
                        break;
                    case SieveActionType.SetFlag:
                        if (a.Argument != @"\Seen" && a.Argument != @"\\Seen")
                            return Result.Failure($"The Rainloop format only supports the \\Seen flag (Mark as read), not '{a.Argument}'");
                        break;
                    case SieveActionType.Keep:
                        // Rainloop's JSON 'Keep' field only applies to Forward+Keep; a standalone
                        // 'keep;' action has no representation in the Snappymail schema.
                        return Result.Failure("The Rainloop format does not support the Keep action (use the Forward+Keep pattern instead)");
                    default:
                        return Result.Failure($"The Rainloop format does not support the {a.Type} action");
                }
            }

            return Result.Success();
        }

        private static Result<RainloopFilter> BuildFilter(SieveRule rule)
        {
            if (rule == null) return Result.Failure<RainloopFilter>("Rule is null");
            if (string.IsNullOrWhiteSpace(rule.Name)) return Result.Failure<RainloopFilter>("Name is required");
            if (rule.Conditions == null || rule.Conditions.Count == 0) return Result.Failure<RainloopFilter>("At least one condition is required");
            if (rule.Actions == null || rule.Actions.Count == 0) return Result.Failure<RainloopFilter>("At least one action is required");

            var primaries = rule.Actions
                .Where(a => a.Type is SieveActionType.FileInto or SieveActionType.Redirect or SieveActionType.Reject or SieveActionType.Discard)
                .ToList();

            // The "fileinto INBOX" companion to a Forward+Keep pairing isn't a primary action;
            // ignore it when counting so the user isn't penalised for the natural Rainloop pattern.
            var redirect = primaries.FirstOrDefault(a => a.Type == SieveActionType.Redirect);
            if (redirect != null)
            {
                primaries.RemoveAll(a => a.Type == SieveActionType.FileInto && a.Argument == "INBOX");
            }

            if (primaries.Count == 0)
                return Result.Failure<RainloopFilter>("The Rainloop format requires exactly one Move/Forward/Reject/Discard action");
            if (primaries.Count > 1)
                return Result.Failure<RainloopFilter>(
                    "The Rainloop format only supports one primary action per rule (Move/Forward/Reject/Discard)");

            var primary = primaries[0];
            var (actionType, actionValue) = primary.Type switch
            {
                SieveActionType.FileInto => ("MoveTo", primary.Argument ?? string.Empty),
                SieveActionType.Redirect => ("Forward", primary.Argument ?? string.Empty),
                SieveActionType.Reject => ("Reject", primary.Argument ?? string.Empty),
                SieveActionType.Discard => ("Discard", string.Empty),
                _ => throw new InvalidOperationException()
            };

            var keep = rule.Actions.Any(a => a.Type == SieveActionType.Keep)
                       || (primary.Type == SieveActionType.Redirect
                           && rule.Actions.Any(a => a.Type == SieveActionType.FileInto && a.Argument == "INBOX"));

            var markAsRead = rule.Actions.Any(a => a.Type == SieveActionType.SetFlag &&
                                                   (a.Argument == @"\Seen" || a.Argument == @"\\Seen"));

            var conditions = new List<RainloopCondition>();
            foreach (var c in rule.Conditions)
            {
                var mapped = MapConditionToRainloop(c);
                if (mapped.IsFailure) return Result.Failure<RainloopFilter>(mapped.Error);
                conditions.Add(mapped.Value);
            }

            return Result.Success(new RainloopFilter
            {
                ID = GenerateRainloopId(rule.Id),
                Enabled = rule.Enabled,
                Name = rule.Name,
                Conditions = conditions,
                ConditionsType = rule.MatchAll ? "All" : "Any",
                ActionType = actionType,
                ActionValue = actionValue,
                Keep = keep,
                Stop = rule.StopAfter,
                MarkAsRead = markAsRead
            });
        }

        private static Result<RainloopCondition> MapConditionToRainloop(SieveCondition c)
        {
            // Check field first so extended fields produce a field-specific error message.
            var field = c.Field switch
            {
                SieveConditionField.From => "From",
                SieveConditionField.To => "To",
                SieveConditionField.Cc => "Cc",
                SieveConditionField.Recipient => "Recipient",
                SieveConditionField.Subject => "Subject",
                SieveConditionField.Header => "Header",
                SieveConditionField.Size => "Size",
                _ => null
            };
            if (field == null) return Result.Failure<RainloopCondition>($"Unsupported field for Rainloop: {c.Field}");

            var type = c.Operator switch
            {
                SieveConditionOperator.Contains => "Contains",
                SieveConditionOperator.Equals => "EqualTo",
                SieveConditionOperator.Matches => "Regex",
                SieveConditionOperator.Larger => "Over",
                SieveConditionOperator.Smaller => "Under",
                _ => null
            };
            if (type == null) return Result.Failure<RainloopCondition>($"Unsupported operator for Rainloop: {c.Operator}");

            return Result.Success(new RainloopCondition
            {
                Field = field,
                Type = type,
                Value = c.Value ?? string.Empty,
                ValueSecond = c.HeaderName ?? string.Empty
            });
        }

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

        private static void EmitFilterBlock(StringBuilder sb, SieveRule rule, RainloopFilter filter)
        {
            var json = JsonSerializer.Serialize(filter, JsonOptions);
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            sb.Append("/*\r\n");
            sb.Append("BEGIN:FILTER:").Append(filter.ID).Append("\r\n");
            sb.Append("BEGIN:HEADER\r\n");
            for (int i = 0; i < b64.Length; i += Base64WrapWidth)
            {
                sb.Append(b64.AsSpan(i, Math.Min(Base64WrapWidth, b64.Length - i)));
                sb.Append("\r\n");
            }
            sb.Append("END:HEADER\r\n");
            sb.Append("*/\r\n");

            if (!rule.Enabled)
            {
                sb.Append("/* @Filter is disabled\r\n");
                EmitSieveBody(sb, rule, filter);
                sb.Append("\r\n*/\r\n");
            }
            else
            {
                sb.Append("\r\n");
                EmitSieveBody(sb, rule, filter);
                sb.Append("\r\n");
            }

            sb.Append("/* END:FILTER */\r\n");
            sb.Append("\r\n");
        }

        private static void EmitSieveBody(StringBuilder sb, SieveRule rule, RainloopFilter filter)
        {
            sb.Append("if ");
            if (rule.Conditions.Count == 1)
            {
                sb.Append(BuildSieveCondition(rule.Conditions[0]));
                sb.Append("\r\n");
            }
            else
            {
                sb.Append(rule.MatchAll ? "allof(\r\n" : "anyof(\r\n");
                for (int i = 0; i < rule.Conditions.Count; i++)
                {
                    sb.Append("    ").Append(BuildSieveCondition(rule.Conditions[i]));
                    sb.Append(i == rule.Conditions.Count - 1 ? "\r\n" : ",\r\n");
                }
                sb.Append(")\r\n");
            }
            sb.Append("{\r\n");

            if (filter.MarkAsRead)
                sb.Append("    addflag \"\\\\Seen\";\r\n");
            if (string.Equals(filter.ActionType, "Forward", StringComparison.Ordinal) && filter.Keep)
                sb.Append("    fileinto \"INBOX\";\r\n");

            switch (filter.ActionType)
            {
                case "MoveTo": sb.Append("    fileinto ").Append(QuoteSimple(filter.ActionValue)).Append(";\r\n"); break;
                case "Forward": sb.Append("    redirect ").Append(QuoteSimple(filter.ActionValue)).Append(";\r\n"); break;
                case "Reject": sb.Append("    reject ").Append(QuoteSimple(filter.ActionValue)).Append(";\r\n"); break;
                case "Discard": sb.Append("    discard;\r\n"); break;
            }

            if (filter.Stop) sb.Append("    stop;\r\n");
            sb.Append("}");
        }

        private static string BuildSieveCondition(SieveCondition c)
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
                _ => throw new InvalidOperationException()
            };

            if (c.Field == SieveConditionField.Recipient)
                return $"header {matchOp} [\"To\", \"CC\"] {QuoteSimple(c.Value)}";

            var name = c.Field switch
            {
                SieveConditionField.From => "From",
                SieveConditionField.To => "To",
                SieveConditionField.Cc => "Cc",
                SieveConditionField.Subject => "Subject",
                SieveConditionField.Header => c.HeaderName!,
                _ => throw new InvalidOperationException()
            };
            return $"header {matchOp} [{QuoteSimple(name)}] {QuoteSimple(c.Value)}";
        }

        private static string QuoteSimple(string s)
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

        private static string GenerateRainloopId(Guid g) =>
            string.Concat(g.ToByteArray().Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

        private static Guid DeriveGuidFromRainloopId(string rainloopId)
        {
            // Try to recover the original Guid (when the rule was authored by us). For any other
            // shape just hash deterministically so the id is stable across round-trips.
            if (!string.IsNullOrEmpty(rainloopId) && rainloopId.Length == 32 && rainloopId.All(IsHex))
            {
                try
                {
                    var bytes = new byte[16];
                    for (int i = 0; i < 16; i++)
                        bytes[i] = byte.Parse(rainloopId.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
                    return new Guid(bytes);
                }
                catch { /* fall through */ }
            }
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(rainloopId ?? string.Empty));
            var guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);
            return new Guid(guidBytes);
        }

        private static bool IsHex(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
