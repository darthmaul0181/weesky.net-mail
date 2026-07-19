using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace weesky.Snoopy.Microservice.Data
{
    public class ApplicationDbContext : DbContext
    {
        // Must be the generic DbContextOptions<ApplicationDbContext>: EF refuses a non-generic
        // one as soon as a second context is registered, and 2a.5 added PreferencesDbContext.
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
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

        public DbSet<MailUser> Users { get; set; }
        public DbSet<MailDomain> Domains { get; set; }
        public DbSet<MailAlias> Aliases { get; set; }
        public DbSet<MailDomainOwnership> DomainsOwnerships { get; set; }
        public DbSet<LastLogin> LastLogins { get; set; }
    }
}
