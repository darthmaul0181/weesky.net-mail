using Xunit;

namespace weesky.Scotty.Microservice.Tests.Infrastructure;

[CollectionDefinition(nameof(MariaDbCollection))]
public sealed class MariaDbCollection : ICollectionFixture<MariaDbFixture>;
