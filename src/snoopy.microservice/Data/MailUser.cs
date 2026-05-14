using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace weesky.Snoopy.Microservice.Data
{
	[Table("users")]
	[DebuggerDisplay("{Name}@{DomainId} ({Id})")]
	public class MailUser
	{
		[Key]
		[Column("id")]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int Id { get; set; }

		[Required]
		[Column("username")]
		public string Name { get; set ; }

		[Required]
		[Column("password")]
		public string Password { get; set ; }

		[Required]
		[Column("domain")]
		[StringLength(3, ErrorMessage = "The string 'DomainId' value cannot exceed 3 characters. ")]
		public string DomainId { get; set ; }

		[Required]
		[Column("active")]
		public ActiveState Active { get; set ; }

		[Column("fullname")]
		public string FullName { get; set; }

		[Required]
		[Column("admin")]
		public ActiveState Admin { get; set; }

		[Column("quota_mb")]
		public int QuotaMb { get; set; } = 1024;

		[Column("lastupdate")]
		public DateTime LastUpdate { get; set; }
	}

	public enum ActiveState
	{
		Y,
		N
	}
}
