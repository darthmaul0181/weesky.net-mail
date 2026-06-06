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
        Size
    }
}
