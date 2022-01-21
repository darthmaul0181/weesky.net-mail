using System.Diagnostics;
using System.Text.Json.Serialization;

namespace weesky.MailAdminRestAPI.Models
{
	[DebuggerDisplay("{DebuggerDisplay}")]
	public class Alias
	{
		/// <summary>
		/// The alias name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The domain
		/// </summary>
		public string Domain { get; set; }

		[JsonIgnore]
		public string DebuggerDisplay => $"{Name}@{Domain}";

		public override bool Equals(object? obj)
		{
			if(obj == null)
				return false;


			if(obj is Alias otherAlias)
			{
				return string.Equals(Name, otherAlias.Name, StringComparison.InvariantCultureIgnoreCase)
					&& string.Equals(Domain, otherAlias.Domain, StringComparison.InvariantCultureIgnoreCase);
			}

			return false;
		}

		public override int GetHashCode()
		{
			return $"{Name}@{Domain}".GetHashCode();
		}
	}
}
