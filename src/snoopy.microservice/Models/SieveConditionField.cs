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
        RecipientDetail
    }
}
