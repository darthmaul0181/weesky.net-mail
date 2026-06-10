using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models
{
    public class Credentials
    {
        /// <summary>
        /// The email of the user
        /// </summary>
        [Required]
        [EmailAddress]
        [StringLength(159)]
        public string Email { get; set; }

        /// <summary>
        /// The password of the user
        /// </summary>
        [Required]
        [StringLength(256, MinimumLength = 1)]
        public string Password { get; set; }
    }
}
