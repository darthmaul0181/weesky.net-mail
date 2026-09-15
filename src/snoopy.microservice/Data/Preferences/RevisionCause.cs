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
    Rejected,

    /// <summary>The server advanced a component's SEQUENCE after a device wrote a change the
    /// invitees have to receive (spec 5e, décision 9) — neither a PUT nor the user's gesture.</summary>
    Scheduling,

    /// <summary>A guest's REPLY applied by the mail server at delivery (spec 5e3, décision 11): the
    /// user was not there, and the history must not say they were.</summary>
    Delivery
}
