using Microsoft.EntityFrameworkCore;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// Webmail user preferences. A separate database from the dovecot schema on purpose: that
/// schema belongs to Dovecot and can be rebuilt by mail-server provisioning, which would
/// take our data with it. Created manually — no EF migrations in this project; see
/// docs/superpowers/mail-2a5-database-prerequisite.md.
/// </summary>
public class PreferencesDbContext : DbContext
{
    public PreferencesDbContext(DbContextOptions<PreferencesDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FolderRoleOverride>().HasKey(o => new { o.UserId, o.Role });
        modelBuilder.Entity<UserPreference>().HasKey(p => new { p.UserId, p.PreferenceKey });
        modelBuilder.Entity<SendingIdentity>().HasKey(i => new { i.UserId, i.Address });
        modelBuilder.Entity<WebmailUser>().HasKey(u => u.Id);
        modelBuilder.Entity<WebmailUser>().HasIndex(u => u.Email).IsUnique();
    }

    public DbSet<FolderRoleOverride> FolderRoleOverrides { get; set; }

    public DbSet<UserPreference> UserPreferences { get; set; }

    public DbSet<SendingIdentity> SendingIdentities { get; set; }

    public DbSet<WebmailUser> Users { get; set; }
}
