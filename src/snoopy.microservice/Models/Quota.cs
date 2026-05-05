namespace weesky.Snoopy.Microservice.Models
{
    /// <summary>
    /// Mailbox quota usage reported by Dovecot.
    /// </summary>
    public class Quota
    {
        /// <summary>
        /// Storage currently used, in bytes.
        /// </summary>
        public long StorageBytesUsed { get; set; }

        /// <summary>
        /// Storage limit, in bytes. 0 means no limit.
        /// </summary>
        public long StorageBytesLimit { get; set; }

        /// <summary>
        /// Number of messages currently stored.
        /// </summary>
        public long MessageCount { get; set; }

        /// <summary>
        /// Message count limit. 0 means no limit.
        /// </summary>
        public long MessageLimit { get; set; }
    }
}
