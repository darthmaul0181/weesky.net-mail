namespace weesky.Snoopy.Microservice.Models
{
    public class SieveAction
    {
        public SieveActionType Type { get; set; }

        /// <summary>
        /// FileInto → folder name. Redirect → email address. Reject → reason text. SetFlag → flag name (e.g. <c>\Seen</c>).
        /// Unused for Discard / Keep.
        /// </summary>
        public string? Argument { get; set; }
    }
}
