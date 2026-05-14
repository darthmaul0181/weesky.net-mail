using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace weesky.Snoopy.Microservice.Data
{
	public class ApplicationDbContext : DbContext
	{
		private IConfiguration Configuration { get; }
		private DbContextOptions DbContextOptions { get; }

		public ApplicationDbContext(DbContextOptions dbContextOptions, IConfiguration configuration)
		{
			DbContextOptions = dbContextOptions;
			Configuration = configuration;
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			var connectionString = Configuration.GetConnectionString("MailUserAccountsDatabase");
			optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), options => options.EnableStringComparisonTranslations())
			.LogTo(Console.WriteLine, LogLevel.Warning);
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<MailUser>()
				.Property(u => u.Active)
				.HasConversion(new EnumToStringConverter<ActiveState>());

			modelBuilder.Entity<MailUser>()
				.Property(u => u.Admin)
				.HasConversion(new EnumToStringConverter<ActiveState>());
		}

		public DbSet<MailUser> Users {get; set;}
		public DbSet<MailDomain> Domains { get; set; }
		public DbSet<MailAlias> Aliases { get; set; }
		public DbSet<MailDomainOwnership> DomainsOwnerships { get; set; }
	}
}
