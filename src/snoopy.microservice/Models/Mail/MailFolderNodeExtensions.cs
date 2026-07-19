namespace weesky.Snoopy.Microservice.Models.Mail;

public static class MailFolderNodeExtensions
{
    /// <summary>Depth-first lookup by path. Null when the tree holds no such folder.</summary>
    public static MailFolderNode? FindByPath(this IEnumerable<MailFolderNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (node.Path == path) return node;
            if (node.Children.FindByPath(path) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// Every folder below this one. Walks the children rather than matching path prefixes:
    /// the hierarchy separator belongs to the server and is never assumed here.
    /// </summary>
    public static IEnumerable<MailFolderNode> Descendants(this MailFolderNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var grandchild in child.Descendants()) yield return grandchild;
        }
    }
}
