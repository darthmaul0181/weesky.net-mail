using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories
{
    public class FolderRoleStoreTests
    {
        private static FolderRoleStore CreateStore(string dbName) =>
            new(new PreferencesTestDbContext(dbName));

        private static FolderRoleOverride Override(
            string role, string path, ulong uidValidity = 1, string? mailboxId = null,
            string accountId = "alice@weesky.be") =>
            new() { AccountId = accountId, Role = role, FolderPath = path, UidValidity = uidValidity, MailboxId = mailboxId };

        [Fact]
        public void CanonicalAccountId_TrimsAndLowercases()
        {
            Assert.Equal("alice@weesky.be", FolderRoleStore.CanonicalAccountId("  Alice@WEESKY.be "));
        }

        [Fact]
        public async Task Upsert_InsertsThenUpdatesTheSameRow()
        {
            var store = CreateStore(nameof(Upsert_InsertsThenUpdatesTheSameRow));

            await store.UpsertAsync(Override("trash", "Deleted Items", 10), CancellationToken.None);
            await store.UpsertAsync(Override("trash", "Corbeille", 20, "M1"), CancellationToken.None);

            var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);
            var row = Assert.Single(rows);
            Assert.Equal("Corbeille", row.FolderPath);
            Assert.Equal(20UL, row.UidValidity);
            Assert.Equal("M1", row.MailboxId);
        }

        [Fact]
        public async Task Get_ReturnsOnlyTheAccountsRows()
        {
            var store = CreateStore(nameof(Get_ReturnsOnlyTheAccountsRows));
            await store.UpsertAsync(Override("trash", "T"), CancellationToken.None);
            await store.UpsertAsync(Override("junk", "J", accountId: "bob@weesky.be"), CancellationToken.None);

            var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);

            Assert.Equal("trash", Assert.Single(rows).Role);
        }

        [Fact]
        public async Task Delete_IsIdempotent()
        {
            var store = CreateStore(nameof(Delete_IsIdempotent));
            await store.UpsertAsync(Override("junk", "Spam"), CancellationToken.None);

            await store.DeleteAsync("alice@weesky.be", "junk", CancellationToken.None);
            await store.DeleteAsync("alice@weesky.be", "junk", CancellationToken.None); // no throw

            Assert.Empty(await store.GetAsync("alice@weesky.be", CancellationToken.None));
        }

        // The exact row gets the re-read identity — some servers change UIDVALIDITY on rename,
        // and carrying the old value would make our own rename trip our own staleness guard.
        [Fact]
        public async Task ApplyRename_UpdatesTheExactRowWithTheFreshIdentity()
        {
            var store = CreateStore(nameof(ApplyRename_UpdatesTheExactRowWithTheFreshIdentity));
            await store.UpsertAsync(Override("trash", "Old", 10, "M-old"), CancellationToken.None);

            await store.ApplyRenameAsync("alice@weesky.be", "Old", "New", '/', 42, "M-new", CancellationToken.None);

            var row = Assert.Single(await store.GetAsync("alice@weesky.be", CancellationToken.None));
            Assert.Equal("New", row.FolderPath);
            Assert.Equal(42UL, row.UidValidity);
            Assert.Equal("M-new", row.MailboxId);
        }

        // A parent rename moves the whole subtree in IMAP — the overrides must follow.
        // Both separators, in the same test: '.' on the home server, '/' elsewhere.
        [Theory]
        [InlineData('/')]
        [InlineData('.')]
        public async Task ApplyRename_MovesTheSubtree(char separator)
        {
            var store = CreateStore(nameof(ApplyRename_MovesTheSubtree) + separator);
            await store.UpsertAsync(Override("archive", $"Projects{separator}Archive", 5), CancellationToken.None);

            await store.ApplyRenameAsync("alice@weesky.be", "Projects", "Work", separator, 99, null, CancellationToken.None);

            var row = Assert.Single(await store.GetAsync("alice@weesky.be", CancellationToken.None));
            Assert.Equal($"Work{separator}Archive", row.FolderPath);
            // A child keeps its own identity: the parent's rename does not change its
            // UIDVALIDITY. If a server does change it, the staleness guard degrades — it
            // never lies.
            Assert.Equal(5UL, row.UidValidity);
        }

        // "Projects2" starts with "Projects" but is a sibling, not a child. The prefix match
        // must include the separator, or a rename corrupts unrelated overrides.
        [Fact]
        public async Task ApplyRename_LeavesASiblingWithASharedNamePrefixAlone()
        {
            var store = CreateStore(nameof(ApplyRename_LeavesASiblingWithASharedNamePrefixAlone));
            await store.UpsertAsync(Override("archive", "Projects2/Archive", 5), CancellationToken.None);

            await store.ApplyRenameAsync("alice@weesky.be", "Projects", "Work", '/', 99, null, CancellationToken.None);

            var row = Assert.Single(await store.GetAsync("alice@weesky.be", CancellationToken.None));
            Assert.Equal("Projects2/Archive", row.FolderPath);
        }

        [Theory]
        [InlineData('/')]
        [InlineData('.')]
        public async Task RemoveSubtree_PurgesTheFolderAndItsChildren(char separator)
        {
            var store = CreateStore(nameof(RemoveSubtree_PurgesTheFolderAndItsChildren) + separator);
            await store.UpsertAsync(Override("trash", "Projects"), CancellationToken.None);
            await store.UpsertAsync(Override("archive", $"Projects{separator}Old"), CancellationToken.None);
            await store.UpsertAsync(Override("junk", "Spam"), CancellationToken.None);

            await store.RemoveSubtreeAsync("alice@weesky.be", "Projects", separator, CancellationToken.None);

            var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);
            Assert.Equal("junk", Assert.Single(rows).Role);
        }
    }
}
