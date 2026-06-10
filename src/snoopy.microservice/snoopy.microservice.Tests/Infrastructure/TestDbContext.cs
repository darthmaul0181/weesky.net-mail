using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure
{
    internal class TestDbContext : ApplicationDbContext
    {
        public TestDbContext(string databaseName)
            : base(new DbContextOptionsBuilder<ApplicationDbContext>()
                  .UseInMemoryDatabase(databaseName)
                  .Options)
        {
        }
    }
}
