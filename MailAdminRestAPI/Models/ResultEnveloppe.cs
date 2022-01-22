namespace weesky.MailAdminRestAPI.Models
{
	public class ResultEnveloppe
	{
		public ResultState State { get; set; }
		public string Message { get; set; }

		public static ResultEnveloppe CreateSuccessEnveloppe(string message = null)
		{
			return new ResultEnveloppe
			{
				Message = message
			};
		}

		public static ResultEnveloppe CrateErrorEnveloppe(string message)
		{
			return new ResultEnveloppe
			{
				State = ResultState.Error,
				Message = message
			};
		}
	}

	public class ResultEnveloppe<T> : ResultEnveloppe
	{
		public T Result { get; set; }
	}

	public enum ResultState
	{
		Success,
		Error
	}
}
