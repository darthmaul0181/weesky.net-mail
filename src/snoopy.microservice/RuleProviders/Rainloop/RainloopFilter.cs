namespace weesky.Snoopy.Microservice.RuleProviders.Rainloop
{
    /// <summary>
    /// Mirror of the JSON payload Rainloop embeds (base64-encoded) inside each
    /// <c>BEGIN:HEADER</c>/<c>END:HEADER</c> block. Property names use PascalCase to
    /// match Rainloop's wire format.
    /// </summary>
    internal class RainloopFilter
    {
        public string ID { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = string.Empty;
        public List<RainloopCondition> Conditions { get; set; } = new();

        /// <summary>"Any" (OR) or "All" (AND).</summary>
        public string ConditionsType { get; set; } = "Any";

        /// <summary>"MoveTo" | "Forward" | "Reject" | "Discard".</summary>
        public string ActionType { get; set; } = "MoveTo";
        public string ActionValue { get; set; } = string.Empty;
        public string ActionValueSecond { get; set; } = string.Empty;
        public string ActionValueThird { get; set; } = string.Empty;
        public string ActionValueFourth { get; set; } = string.Empty;

        public bool Keep { get; set; }
        public bool Stop { get; set; }
        public bool MarkAsRead { get; set; }
    }

    internal class RainloopCondition
    {
        /// <summary>"Subject" | "From" | "To" | "Recipient" | "Header".</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>"Contains" | "EqualTo" | "NotEqualTo" | "Regex" | "Over" | "Under".</summary>
        public string Type { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
        public string ValueSecond { get; set; } = string.Empty;
    }
}
