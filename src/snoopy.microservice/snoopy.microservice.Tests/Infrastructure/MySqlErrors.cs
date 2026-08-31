using System.Reflection;
using MySqlConnector;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// Fabricates the <see cref="MySqlException"/> InnoDB throws, which no test can raise for real:
/// the suite runs on the InMemory provider, so there is no engine to take a lock, and every
/// constructor MySqlConnector exposes is non-public. The number is what the production code
/// branches on — never the message, which the server translates to its own locale — and
/// <c>TheFabricatedException_ReallyCarriesItsNumber</c> pins that this reflection still produces
/// one carrying it, so a translation test cannot pass over an exception that says nothing.
/// </summary>
internal static class MySqlErrors
{
    internal const int LockWaitTimeout = 1205;
    internal const int Deadlock = 1213;

    internal static MySqlException With(int number, string message = "Lock wait timeout exceeded") =>
        (MySqlException)Activator.CreateInstance(
            typeof(MySqlException),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [(MySqlErrorCode)number, message],
            culture: null)!;
}
