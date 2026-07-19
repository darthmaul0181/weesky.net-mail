using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure
{
    internal class PreferencesTestDbContext : PreferencesDbContext
    {
        public PreferencesTestDbContext(string databaseName)
            : base(new DbContextOptionsBuilder<PreferencesDbContext>()
                  .UseInMemoryDatabase(databaseName)
                  .Options)
        {
        }
    }
}
