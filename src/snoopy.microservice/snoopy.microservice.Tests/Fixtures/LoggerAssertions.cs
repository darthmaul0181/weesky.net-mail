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

    internal static void VerifyWarningLoggedContaining<T>(this Mock<ILogger<T>> logger, string text) =>
        logger.Verify(Logged<T>(LogLevel.Warning, text), Times.AtLeastOnce);

    internal static void VerifyNoWarningLogged<T>(this Mock<ILogger<T>> logger) =>
        logger.Verify(Logged<T>(LogLevel.Warning), Times.Never);

    /// <summary>
    /// One Information entry carrying every one of <paramref name="texts"/> — not one entry per
    /// text. A line is read as a whole, and an assertion satisfied by fragments scattered over
    /// several entries would pass against a line that says none of what it claims.
    /// </summary>
    internal static void VerifyInformationLoggedWithAll<T>(
        this Mock<ILogger<T>> logger, params string[] texts) =>
        logger.Verify(LoggedWithAll<T>(LogLevel.Information, texts), Times.AtLeastOnce);

    /// <summary>
    /// No entry, at any level, whose state carries <paramref name="text"/> in any of its values —
    /// the message template among them, since it travels as the <c>{OriginalFormat}</c> value.
    /// Level-agnostic on purpose: a secret leaked at Warning is leaked.
    /// </summary>
    internal static void VerifyNoLoggedValueContains<T>(this Mock<ILogger<T>> logger, string text) =>
        logger.Verify(LoggedValueContaining<T>(text), Times.Never);

    /// <summary>
    /// The message template of the single entry logged — what a log query filters on, and what an
    /// interpolated call would make different on every request. Throws when the entries do not
    /// number exactly one, so it can never report the template of some other line.
    /// </summary>
    internal static string SingleTemplate<T>(this Mock<ILogger<T>> logger)
    {
        var states = logger.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ILogger.Log))
            .Select(invocation => invocation.Arguments[2])
            .ToList();
        return states.Count == 1
            ? Values(states[0]).First(pair => pair.Key == "{OriginalFormat}").Value?.ToString() ?? ""
            : throw new InvalidOperationException(
                $"Expected exactly one logged entry, found {states.Count}.");
    }

    // Two whole expression trees, not one with a ternary picking the third argument: Moq's
    // It.IsAnyType idiom is recognised only when It.Is/It.IsAny appears as the argument's own
    // MethodCallExpression. A ternary forces the compiler to build a ConditionalExpression node
    // there instead, which Moq cannot special-case — it collapses to a constant that never matches
    // the real state, so Verify always reports "never performed" regardless of what was logged,
    // including for Times.Never, which would then pass vacuously whether or not the level fired.
    private static Expression<Action<ILogger<T>>> Logged<T>(LogLevel level, string? containing = null) =>
        containing is null
            ? l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>())
            : l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(containing)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>());

    private static Expression<Action<ILogger<T>>> LoggedWithAll<T>(LogLevel level, string[] texts) =>
        l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => CarriesAll(state, texts)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>());

    // Any level, deliberately: this one asserts an absence, and restricting it to Information
    // would let the very same text through at Warning.
    private static Expression<Action<ILogger<T>>> LoggedValueContaining<T>(string text) =>
        l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => AnyValueCarries(state, text)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>());

    private static bool CarriesAll(object? state, string[] texts)
    {
        var rendered = state?.ToString() ?? string.Empty;
        return texts.All(text => rendered.Contains(text, StringComparison.Ordinal));
    }

    private static bool AnyValueCarries(object? state, string text)
    {
        var values = Values(state);
        return values.Count > 0
            ? values.Any(pair => Carries(pair.Value?.ToString(), text))
            : Carries(state?.ToString(), text);
    }

    private static bool Carries(string? candidate, string text) =>
        candidate?.Contains(text, StringComparison.Ordinal) is true;

    /// <summary>
    /// The state's structured values, <c>{OriginalFormat}</c> included. A state that is not the
    /// framework's own value list yields none, and the caller falls back to its rendering.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, object?>> Values(object? state) =>
        state is IEnumerable<KeyValuePair<string, object?>> pairs ? [.. pairs] : [];
}
