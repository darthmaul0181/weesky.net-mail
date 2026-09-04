namespace weesky.Snoopy.Microservice.Services;

/// <summary>The borrower the pool indexes by. Null on a request that never resolved an account.</summary>
internal interface IRequestIdentity
{
    Guid? UserUid { get; }
}

/// <summary>
/// Scoped: set once per request by <see cref="AccountConnectionResolver"/>, the only mail-path
/// service that holds the user, and read by <see cref="ScopedImapSessionProvider"/> when it
/// borrows. Neither the connection record nor the session interface carries the user, on purpose.
/// </summary>
internal sealed class RequestIdentity : IRequestIdentity
{
    public Guid? UserUid { get; private set; }

    public void Set(Guid uid)
    {
        if (uid != Guid.Empty) UserUid = uid;
    }
}
