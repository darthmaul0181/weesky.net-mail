using System.ComponentModel.DataAnnotations;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Models.Calendar;

public enum InvitationAnswer { Accepted, Tentative, Declined, AddOnly, Remove }

/// <summary>What the card sends (spec 5e, décision 4). The file itself never travels: the server
/// re-reads it from IMAP by <see cref="Folder"/>/<see cref="Uid"/>/<see cref="Part"/>.</summary>
public sealed class RespondInvitationRequest
{
    [Required(ErrorMessage = "A folder is required")]
    public string Folder { get; set; } = string.Empty;

    public uint Uid { get; set; }

    /// <summary>The MIME part specifier the detail carried. Empty is a real specifier.</summary>
    [Required(AllowEmptyStrings = true)]
    public string Part { get; set; } = string.Empty;

    /// <summary>Checked: the binder takes any integer, and an undefined one would reach the
    /// write as an answer nobody chose.</summary>
    [EnumDataType(typeof(InvitationAnswer), ErrorMessage = "Unknown answer")]
    public InvitationAnswer Answer { get; set; }

    /// <summary>The target calendar of a creation; ignored once the event exists (décision 4).</summary>
    public Guid? CalendarId { get; set; }

    /// <summary>"fr" or "en": the language of the reply's subject and line.</summary>
    public string Language { get; set; } = "en";

    /// <summary>IANA zone of the browser, for the reply's date in words.</summary>
    public string TimeZone { get; set; } = "UTC";
}

/// <summary>The invitation block as it stands after the answer, and what was and was not done
/// beyond the calendar: a reply that could not leave and a mail that stayed put are reported,
/// never rolled back (décision 4).</summary>
public sealed record InvitationResponse(MailInvitation Invitation, bool ReplySent, string? ReplyError, bool Trashed);

/// <summary>A refusal before anything was written, and the HTTP status the controller answers with.</summary>
public sealed record ResponderFailure(int Status, string Message);
