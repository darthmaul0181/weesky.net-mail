using System.Diagnostics;

namespace weesky.MailAdminRestAPI.Models
{
	[DebuggerDisplay("{Name} ({Id})")]
	public class Domain
	{
		public string Id { get; set; }

		public string Name { get; set; }
	}
}
