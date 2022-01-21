namespace weesky.MailAdminRestAPI.Services
{
	public class RepositoryResult
	{
		public RespositoryResultState State { get; set; }
		public string Message { get; set; }
	}

	public enum RespositoryResultState
	{
		Success,
		Error
	}
}
