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
        modelBuilder.Entity<FolderRoleOverride>().HasKey(o => new { o.UserId, o.AccountId, o.Role });
        modelBuilder.Entity<UserPreference>().HasKey(p => new { p.UserId, p.PreferenceKey });
        // No relation edge here, unlike the five per-account tables below: this setting belongs
        // to no one, so there is nothing to order ahead of users.
        modelBuilder.Entity<AppSetting>().HasKey(s => s.SettingKey);
        modelBuilder.Entity<SendingIdentity>().HasKey(i => new { i.UserId, i.AccountId, i.Address });
        modelBuilder.Entity<TrustedSender>().HasKey(t => new { t.UserId, t.Address });
        modelBuilder.Entity<Contact>().HasKey(c => c.Id);
        modelBuilder.Entity<Contact>().HasIndex(c => new { c.UserId, c.Uid }).IsUnique();
        modelBuilder.Entity<ContactEmail>().HasKey(e => new { e.ContactId, e.Position });
        modelBuilder.Entity<ContactPhone>().HasKey(p => new { p.ContactId, p.Position });
        modelBuilder.Entity<ContactAddress>().HasKey(a => new { a.ContactId, a.Position });
        modelBuilder.Entity<ContactPhoto>().HasKey(p => p.ContactId);
        // Without this edge EF has no dependency between the two and orders their INSERTs by table
        // name — contact_emails before contacts — breaking fk_contact_emails_contact on any create
        // carrying an address. Declared without navigation: the entities stay flat, the order does
        // not stay accidental (parent first on insert, child first on delete).
        modelBuilder.Entity<ContactEmail>()
            .HasOne<Contact>()
            .WithMany()
            .HasForeignKey(e => e.ContactId)
            .OnDelete(DeleteBehavior.Cascade);
        // Same mechanism as ContactEmail -> Contact above, for the three sibling projection tables.
        modelBuilder.Entity<ContactPhone>()
            .HasOne<Contact>().WithMany().HasForeignKey(p => p.ContactId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContactAddress>()
            .HasOne<Contact>().WithMany().HasForeignKey(a => a.ContactId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContactPhoto>()
            .HasOne<Contact>().WithMany().HasForeignKey(p => p.ContactId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContactGroupMember>().HasKey(m => new { m.GroupId, m.Position });
        modelBuilder.Entity<ContactGroupMember>().HasIndex(m => new { m.GroupId, m.MemberUid }).IsUnique();
        modelBuilder.Entity<ContactGroupMember>()
            .HasOne<Contact>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<DavCredential>().HasKey(c => c.UserId);
        // Same mechanism as the five tables above: "dav_credentials" sorts before "users", so
        // without a declared edge EF orders the INSERTs by table name and breaks the FK on any
        // create. Declared without navigation, like its neighbours. The InMemory provider enforces
        // no foreign key, so no test can catch this — only the declaration can.
        modelBuilder.Entity<DavCredential>()
            .HasOne<WebmailUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExternalDomain>().HasKey(d => d.Id);
        modelBuilder.Entity<ExternalDomain>().HasIndex(d => d.Name).IsUnique();
        modelBuilder.Entity<ExternalDomain>()
            .Property(d => d.AuthMode)
            .HasConversion<string>()
            .HasMaxLength(16);
        modelBuilder.Entity<ConnectedAccount>().HasKey(a => a.Id);
        modelBuilder.Entity<ConnectedAccount>().HasIndex(a => new { a.UserId, a.DomainId, a.Email }).IsUnique();
        modelBuilder.Entity<ConnectedAccount>()
            .Property(a => a.AuthMode)
            .HasConversion<string>()
            .HasMaxLength(16);
        // Same mechanism again: "connected_accounts" sorts before both parents.
        modelBuilder.Entity<ConnectedAccount>()
            .HasOne<WebmailUser>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ConnectedAccount>()
            .HasOne<ExternalDomain>()
            .WithMany()
            .HasForeignKey(a => a.DomainId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ContactSyncState>().HasKey(s => s.UserId);
        modelBuilder.Entity<ContactTombstone>().HasKey(t => new { t.UserId, t.DavName });
        modelBuilder.Entity<ContactRevision>().HasKey(r => r.Id);
        modelBuilder.Entity<ContactRevision>().Property(r => r.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<ContactRevision>()
            .Property(r => r.Cause)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<RevisionCause>(v, true))
            .HasMaxLength(8);

        // Same mechanism as every table above: all three sort before "users", so without a declared
        // edge EF orders the INSERTs by table name and breaks the FK on any create. Declared without
        // navigation, like their neighbours. The InMemory provider enforces no foreign key, so no
        // test can catch this — only the declaration can.
        modelBuilder.Entity<ContactSyncState>()
            .HasOne<WebmailUser>().WithMany()
            .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContactTombstone>()
            .HasOne<WebmailUser>().WithMany()
            .HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ContactRevision>()
            .HasOne<WebmailUser>().WithMany()
            .HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Contact>().HasIndex(c => new { c.UserId, c.DavName }).IsUnique();
    }

    public DbSet<FolderRoleOverride> FolderRoleOverrides { get; set; }

    public DbSet<UserPreference> UserPreferences { get; set; }

    public DbSet<AppSetting> AppSettings { get; set; }

    public DbSet<SendingIdentity> SendingIdentities { get; set; }

    public DbSet<TrustedSender> TrustedSenders { get; set; }

    public DbSet<DavCredential> DavCredentials { get; set; }

    public DbSet<Contact> Contacts { get; set; }

    public DbSet<ContactEmail> ContactEmails { get; set; }

    public DbSet<ContactPhone> ContactPhones { get; set; }

    public DbSet<ContactAddress> ContactAddresses { get; set; }

    public DbSet<ContactPhoto> ContactPhotos { get; set; }

    public DbSet<ContactGroupMember> ContactGroupMembers { get; set; }

    public DbSet<WebmailUser> Users { get; set; }

    public DbSet<ExternalDomain> ExternalDomains { get; set; }

    public DbSet<ConnectedAccount> ConnectedAccounts { get; set; }

    public DbSet<ContactSyncState> ContactSyncStates { get; set; }

    public DbSet<ContactTombstone> ContactTombstones { get; set; }

    public DbSet<ContactRevision> ContactRevisions { get; set; }
}
