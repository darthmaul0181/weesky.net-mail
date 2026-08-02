using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Outcome of resolving the active account: exactly one of a connection or the ActionResult to
/// answer with. <see cref="Failed"/> is the only way to either, so the compiler proves the
/// non-null branch at every call site — no null-forgiving reads.
/// </summary>
public readonly struct AccountResolution<T> where T : class
{
    private readonly T? connection;
    private readonly ActionResult? error;

    private AccountResolution(T? connection, ActionResult? error)
    {
        this.connection = connection;
        this.error = error;
    }

    public static AccountResolution<T> Success(T connection) => new(connection, null);

    public static AccountResolution<T> Failure(ActionResult error) => new(null, error);

    public bool Failed([NotNullWhen(true)] out ActionResult? error, [NotNullWhen(false)] out T? connection)
    {
        // A default-constructed instance holds neither; failing loudly beats an NRE downstream.
        if (this.connection is null && this.error is null)
            throw new InvalidOperationException("AccountResolution must be built via Success or Failure.");

        error = this.error;
        connection = this.connection;
        return error is not null;
    }
}
