namespace weesky.Snoopy.Microservice.Models
{
    /// <summary>
    /// Information about the authenticated user account.
    /// </summary>
    public class AccountInfo
    {
        /// <summary>
        /// The internal user identifier.
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// The mailbox name (without domain).
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// The full name of the user.
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// The domain id of the user's primary mailbox (matches one of the Domains ids).
        /// </summary>
        public string Mailbox { get; set; }

        /// <summary>
        /// Domains owned by this user.
        /// </summary>
        public IEnumerable<Domain> Domains { get; set; }

        /// <summary>
        /// Whether this user has administrator privileges.
        /// </summary>
        public bool IsAdmin { get; set; }
    }
}
