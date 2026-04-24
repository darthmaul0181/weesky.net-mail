namespace weesky.MailAdminRestAPI.Models
{
    public class DovecotOptions
    {
        /// <summary>
        /// Full URL of the doveadm HTTP API endpoint, e.g. https://mail.example.net:8080/doveadm/v1.
        /// </summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>
        /// Value of the Dovecot `doveadm_api_key` shared with the service.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;
    }
}
