using MailKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public class MailThreadingTests
{
    private static MessageThread Node(uint uid, params MessageThread[] children)
    {
        var node = new MessageThread(new UniqueId(uid));
        foreach (var child in children) node.Children.Add(child);
        return node;
    }

    private static UniqueId U(uint uid) => new(uid);

    [Fact]
    public void Orders_threads_by_their_newest_member()
    {
        // Sorted newest-first: 30, 20, 10. Thread A = {10, 30}, thread B = {20}.
        // A's newest member (30) outranks B's (20), so A comes first despite its old root.
        var tree = new List<MessageThread> { Node(10, Node(30)), Node(20) };
        var sorted = new List<UniqueId> { U(30), U(20), U(10) };

        var threads = MailThreading.Arrange(tree, sorted);

        Assert.Equal(2, threads.Count);
        Assert.Equal([U(30), U(10)], threads[0]);
        Assert.Equal([U(20)], threads[1]);
    }

    [Fact]
    public void Members_come_newest_first_whatever_the_tree_order()
    {
        var tree = new List<MessageThread> { Node(1, Node(3, Node(2))) };
        var sorted = new List<UniqueId> { U(3), U(2), U(1) };

        var threads = MailThreading.Arrange(tree, sorted);

        Assert.Equal([U(3), U(2), U(1)], Assert.Single(threads));
    }

    [Fact]
    public void A_phantom_root_contributes_no_uid()
    {
        // THREAD may answer a parent the mailbox no longer holds: UniqueId is null there.
        var phantom = new MessageThread((UniqueId?)null);
        phantom.Children.Add(Node(5));
        var sorted = new List<UniqueId> { U(5) };

        var threads = MailThreading.Arrange([phantom], sorted);

        Assert.Equal([U(5)], Assert.Single(threads));
    }

    [Fact]
    public void A_uid_the_sort_does_not_know_is_dropped()
    {
        // THREAD and SORT are two commands; a message expunged between them is in one only.
        var tree = new List<MessageThread> { Node(7, Node(99)) };
        var sorted = new List<UniqueId> { U(7) };

        var threads = MailThreading.Arrange(tree, sorted);

        Assert.Equal([U(7)], Assert.Single(threads));
    }

    [Fact]
    public void A_thread_with_no_known_member_disappears()
    {
        var tree = new List<MessageThread> { Node(99), Node(4) };
        var sorted = new List<UniqueId> { U(4) };

        var threads = MailThreading.Arrange(tree, sorted);

        Assert.Equal([U(4)], Assert.Single(threads));
    }

    [Fact]
    public void Empty_inputs_answer_empty()
    {
        Assert.Empty(MailThreading.Arrange([], []));
    }
}
