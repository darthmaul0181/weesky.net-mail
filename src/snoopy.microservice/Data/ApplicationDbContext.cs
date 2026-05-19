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
			var raw = Configuration.GetConnectionString("MailUserAccountsDatabase");
			var connectionString = new MySqlConnector.MySqlConnectionStringBuilder(raw)
			{
				ConvertZeroDateTime = true
			}.ToString();
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

			modelBuilder.Entity<MailDomainOwnership>()
				.HasKey(o => new { o.DomainId, o.UserId });

			modelBuilder.Entity<LastLogin>()
				.HasKey(l => new { l.UserId, l.Service });
		}

		public DbSet<MailUser> Users {get; set;}
		public DbSet<MailDomain> Domains { get; set; }
		public DbSet<MailAlias> Aliases { get; set; }
		public DbSet<MailDomainOwnership> DomainsOwnerships { get; set; }
		public DbSet<LastLogin> LastLogins { get; set; }
	}
}
