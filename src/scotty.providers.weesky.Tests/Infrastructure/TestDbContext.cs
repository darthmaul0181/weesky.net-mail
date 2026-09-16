using Microsoft.EntityFrameworkCore;
using weesky.Scotty.Providers.Weesky.Data;

namespace weesky.Scotty.Providers.Weesky.Tests.Infrastructure;

internal sealed class TestDbContext : ApplicationDbContext
{
    public TestDbContext(string databaseName)
        : base(new DbContextOptionsBuilder<ApplicationDbContext>()
              .UseInMemoryDatabase(databaseName)
              .Options)
    {
    }
}
