namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>A message body made safe to render, plus what was withheld.</summary>
    public class SanitizedHtml
    {
        public string Html { get; set; } = string.Empty;

        /// <summary>
        /// Remote images moved to data-blocked-src. The client offers "show images" and swaps
        /// them back in without another round trip. Loading them would tell the sender the
        /// message was opened, which is why they are withheld until the user asks.
        /// </summary>
        public int BlockedImageCount { get; set; }
    }
}
