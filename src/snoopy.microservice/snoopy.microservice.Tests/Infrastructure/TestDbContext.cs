using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using weesky.Snoopy.Microservice.Data;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure
{
    internal class TestDbContext : ApplicationDbContext
    {
        private readonly string _databaseName;

        public TestDbContext(string databaseName)
            : base(new DbContextOptionsBuilder<ApplicationDbContext>().Options,
                   new ConfigurationBuilder().Build())
        {
            _databaseName = databaseName;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseInMemoryDatabase(_databaseName);
        }
    }
}
