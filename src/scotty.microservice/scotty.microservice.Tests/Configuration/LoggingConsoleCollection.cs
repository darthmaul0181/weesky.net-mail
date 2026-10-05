using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

/// <summary>Console.SetOut is process-wide: the tests that capture it run apart from the rest.</summary>
[CollectionDefinition(nameof(LoggingConsoleCollection), DisableParallelization = true)]
public sealed class LoggingConsoleCollection;
