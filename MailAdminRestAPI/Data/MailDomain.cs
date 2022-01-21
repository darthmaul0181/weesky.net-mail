using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace weesky.MailAdminRestAPI.Data
{
	[Table("domains")]
	[DebuggerDisplay("{Name} ({Id})")]
	public class MailDomain
	{
		[Key]
		[Column("id")]
		[StringLength(3, ErrorMessage = "The string 'Id' value cannot exceed 3 characters. ")]
		public string Id { get; set; }

		[Required]
		[Column("name")]
		[StringLength(30, ErrorMessage = "The string 'Name' value cannot exceed 30 characters. ")]
		public string Name { get; set; }
	}
}
