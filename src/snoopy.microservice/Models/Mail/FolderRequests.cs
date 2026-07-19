namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// Folder paths travel in the body, never in a route segment: the hierarchy separator may
/// be '/', which would break routing.
/// </summary>
public sealed class CreateFolderRequest
{
    /// <summary>Parent folder path. Empty creates at the namespace root.</summary>
    public string ParentPath { get; set; } = string.Empty;

    /// <summary>Leaf name of the new folder. Must not contain the hierarchy separator.</summary>
    public string Name { get; set; } = string.Empty;
}

public sealed class RenameFolderRequest
{
    public string Path { get; set; } = string.Empty;

    /// <summary>New parent path. Same as the current one for a pure rename.</summary>
    public string NewParentPath { get; set; } = string.Empty;

    public string NewName { get; set; } = string.Empty;
}

public sealed class DeleteFolderRequest
{
    public string Path { get; set; } = string.Empty;
}

public sealed class FolderSubscriptionRequest
{
    public string Path { get; set; } = string.Empty;

    /// <summary>True subscribes the folder, false hides it from the folder list.</summary>
    public bool Subscribed { get; set; }
}
