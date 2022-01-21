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
		[StringLength(3)]
		public string Id { get; set; }

		[Required]
		[Column("name")]
		[StringLength(30)]
		public string Name { get; set; }
	}
}
