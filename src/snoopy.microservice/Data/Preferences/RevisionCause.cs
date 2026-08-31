namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// Why a card was archived. Without it one cannot tell an overwrite to undo from a wanted edit,
/// which is the only question anybody asks of this table.
/// </summary>
public enum RevisionCause
{
    /// <summary>A CardDAV PUT replaced the card.</summary>
    Put,

    /// <summary>The webmail editor replaced it.</summary>
    Webmail,

    /// <summary>An import merged over it.</summary>
    Import,

    /// <summary>The card was deleted, by whichever door.</summary>
    Delete,

    /// <summary>
    /// A PUT body refused on a precondition, archived before the 412 leaves. DAVx5 applies
    /// "the server wins" without consulting anyone, so the refused version is otherwise lost.
    /// </summary>
    Rejected
}
