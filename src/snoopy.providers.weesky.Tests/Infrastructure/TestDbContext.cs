using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Providers.Weesky.Data;

namespace weesky.Snoopy.Providers.Weesky.Tests.Infrastructure;

internal sealed class TestDbContext : ApplicationDbContext
{
    public TestDbContext(string databaseName)
        : base(new DbContextOptionsBuilder<ApplicationDbContext>()
              .UseInMemoryDatabase(databaseName)
              .Options)
    {
    }
}
