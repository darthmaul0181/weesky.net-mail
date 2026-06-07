namespace weesky.Snoopy.Microservice.Models
{
    public enum SieveConditionField
    {
        From,
        To,
        Cc,
        /// <summary>Either To or Cc — emits <c>header :contains ["To","Cc"] "value"</c>.</summary>
        Recipient,
        Subject,
        Header,
        Size,
        /// <summary>Message body — emits <c>body :text :contains/:matches "value"</c> (requires "body" extension). Weesky-only.</summary>
        Body,
        /// <summary>SMTP envelope sender — emits <c>envelope :op "from" "value"</c>. Weesky-only.</summary>
        EnvelopeFrom,
        /// <summary>SMTP envelope recipient — emits <c>envelope :op "to" "value"</c>. Weesky-only.</summary>
        EnvelopeTo,
        /// <summary>Subaddress detail part (e.g. +tag) — emits <c>address :detail :op ["To","Cc"] "value"</c> (requires "subaddress"). Weesky-only.</summary>
        RecipientDetail,
        /// <summary>
        /// Duplicate detection — emits <c>duplicate</c> or <c>duplicate :seconds N</c> (requires "duplicate"). Weesky-only.
        /// The condition <c>Value</c> is optional; if non-empty it must be a positive integer (seconds window).
        /// <c>Operator</c> is irrelevant and ignored.
        /// </summary>
        Duplicate,
        /// <summary>Current date at delivery — emits <c>currentdate :value "lt"/"ge" "date" "YYYY-MM-DD"</c> or <c>currentdate :is "date" "YYYY-MM-DD"</c> (requires "date"; also "relational" for Before/OnOrAfter). Weesky-only.</summary>
        CurrentDate,
        /// <summary>Message Date header — emits <c>date :value "lt"/"ge" "date" "Date" "YYYY-MM-DD"</c> or <c>date :is "date" "Date" "YYYY-MM-DD"</c> (requires "date"; also "relational" for Before/OnOrAfter). Weesky-only.</summary>
        MessageDate
    }
}
