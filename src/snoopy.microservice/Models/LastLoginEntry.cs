namespace weesky.Snoopy.Microservice.Models
{
    public class LastLoginEntry
    {
        public string Service { get; set; }
        public DateTime? At { get; set; }
        public string Ip { get; set; }
    }
}
