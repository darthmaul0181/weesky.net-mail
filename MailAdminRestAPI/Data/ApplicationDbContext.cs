using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace weesky.MailAdminRestAPI.Data
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

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			var connectionString = Configuration.GetConnectionString("MailUserAccountsDatabase");
			optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), options => options.EnableStringComparisonTranslations())
			.LogTo(Console.WriteLine, LogLevel.Information)
			.EnableSensitiveDataLogging()
			.EnableDetailedErrors();
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<MailUser>()
				.Property(u => u.Active)
				.HasConversion(new EnumToStringConverter<ActiveState>());
		}

		public DbSet<MailUser> Users {get; set;}
		public DbSet<MailDomain> Domains { get; set; }
		public DbSet<MailAlias> Aliases { get; set; }
		public DbSet<MailDomainOwnership> DomainsOwnerships { get; set; }
	}
}
