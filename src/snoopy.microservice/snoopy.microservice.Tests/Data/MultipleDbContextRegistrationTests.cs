using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Data.Preferences;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Data
{
    // Registering a second DbContext makes EF reject any context whose constructor takes the
    // non-generic DbContextOptions. Every test builds its context by hand, so the suite stayed
    // green while the running service failed on the first request that touched the database.
    // These resolve both contexts the way Program.cs does, through the container.
    public class MultipleDbContextRegistrationTests
    {
        private static ServiceProvider BuildProviderWithBothContexts()
        {
            var services = new ServiceCollection();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase("accounts"));
            services.AddDbContext<PreferencesDbContext>(options =>
                options.UseInMemoryDatabase("preferences"));

            return services.BuildServiceProvider();
        }

        [Fact]
        public void ApplicationDbContext_ResolvesWhenASecondContextIsRegistered()
        {
            using var provider = BuildProviderWithBothContexts();
            using var scope = provider.CreateScope();

            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        [Fact]
        public void PreferencesDbContext_ResolvesWhenASecondContextIsRegistered()
        {
            using var provider = BuildProviderWithBothContexts();
            using var scope = provider.CreateScope();

            Assert.NotNull(scope.ServiceProvider.GetRequiredService<PreferencesDbContext>());
        }
    }
}
