using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Moq;

namespace weesky.Snoopy.Microservice.Tests.Fixtures;

/// <summary>
/// Moq cannot verify <see cref="ILogger"/>'s extension methods (<c>LogError</c>, <c>LogInformation</c>,
/// …) directly — they are static sugar over <see cref="ILogger.Log"/>, which is what the mock
/// actually records. Every assertion here goes through that one method instead.
/// </summary>
internal static class LoggerAssertions
{
    internal static void VerifyNoErrorLogged<T>(this Mock<ILogger<T>> logger)
    {
        // Critical is more severe than Error, not a sibling of it: a caller reading "no error
        // logged" means nothing went wrong, and a tolerated Critical would contradict that.
        logger.Verify(Logged<T>(LogLevel.Error), Times.Never);
        logger.Verify(Logged<T>(LogLevel.Critical), Times.Never);
    }

    internal static void VerifyErrorLoggedContaining<T>(this Mock<ILogger<T>> logger, string text) =>
        logger.Verify(Logged<T>(LogLevel.Error, text), Times.AtLeastOnce);

    internal static void VerifyInformationLogged<T>(this Mock<ILogger<T>> logger) =>
        logger.Verify(Logged<T>(LogLevel.Information), Times.AtLeastOnce);

    private static Expression<Action<ILogger<T>>> Logged<T>(LogLevel level, string? containing = null) =>
        l => l.Log(
            level,
            It.IsAny<EventId>(),
            containing == null
                ? It.IsAny<It.IsAnyType>()
                : It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(containing)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>());
}
