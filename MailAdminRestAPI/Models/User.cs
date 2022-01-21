using System.Diagnostics;
using System.Text.Json.Serialization;

namespace weesky.MailAdminRestAPI.Models
{
	[DebuggerDisplay("{Email}")]
	public class User
	{
		public User()
		{ }

		public User(string email)
		{
			if(email == null)
				throw new ArgumentNullException("email");

			string[] mailParts = email.Split('@');

			if (mailParts.Length != 2)
				throw new ArgumentException($"{email} is not a valid email address", nameof(email));

			Name = mailParts[0];
			DomainId = mailParts[1];
		}

		[JsonIgnore]
		public string Email => $"{Name}@{DomainId}";

		/// <summary>
		/// The name of the user (the mailbox name).
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The domain in which the user (mailbox) exists. 
		/// </summary>
		public string DomainId { get; set; }
	}
}
