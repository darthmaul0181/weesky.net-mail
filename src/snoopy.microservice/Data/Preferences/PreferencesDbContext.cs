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
        modelBuilder.Entity<TrustedSender>().HasKey(t => new { t.UserId, t.Address });
        modelBuilder.Entity<Contact>().HasKey(c => c.Id);
        modelBuilder.Entity<Contact>().HasIndex(c => new { c.UserId, c.Uid }).IsUnique();
        modelBuilder.Entity<ContactEmail>().HasKey(e => new { e.ContactId, e.Address });
        // Without this edge EF has no dependency between the two and orders their INSERTs by table
        // name — contact_emails before contacts — breaking fk_contact_emails_contact on any create
        // carrying an address. Declared without navigation: the entities stay flat, the order does
        // not stay accidental (parent first on insert, child first on delete).
        modelBuilder.Entity<ContactEmail>()
            .HasOne<Contact>()
            .WithMany()
            .HasForeignKey(e => e.ContactId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WebmailUser>().HasKey(u => u.Id);
        modelBuilder.Entity<WebmailUser>().HasIndex(u => u.Email).IsUnique();

        // Same mechanism as ContactEmail -> Contact above: each of these five tables carries a real
        // fk_..._user ON DELETE CASCADE to users(id) in the schema, but without a declared edge EF
        // has nothing to order their INSERTs against theirs, and falls back to alphabetical table
        // name — every one of them sorts before "users". Declared without navigation, same as above.
        modelBuilder.Entity<Contact>()
            .HasOne<WebmailUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<FolderRoleOverride>()
            .HasOne<WebmailUser>()
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserPreference>()
            .HasOne<WebmailUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SendingIdentity>()
            .HasOne<WebmailUser>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TrustedSender>()
            .HasOne<WebmailUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public DbSet<FolderRoleOverride> FolderRoleOverrides { get; set; }

    public DbSet<UserPreference> UserPreferences { get; set; }

    public DbSet<SendingIdentity> SendingIdentities { get; set; }

    public DbSet<TrustedSender> TrustedSenders { get; set; }

    public DbSet<Contact> Contacts { get; set; }

    public DbSet<ContactEmail> ContactEmails { get; set; }

    public DbSet<WebmailUser> Users { get; set; }
}
