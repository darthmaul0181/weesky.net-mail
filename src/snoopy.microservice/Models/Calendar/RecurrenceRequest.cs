namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The <c>repeat</c> block of <see cref="EventRequest"/>, as the editor states it —
/// see <see cref="EventRequestValidator"/> for what each field must carry.</summary>
public sealed class RecurrenceRequest
{
    public string? Frequency { get; set; }

    public int Interval { get; set; } = 1;

    public List<string>? ByDay { get; set; }

    public int? ByMonthDay { get; set; }

    public int? BySetPos { get; set; }

    public string? BySetPosDay { get; set; }

    public RecurrenceEnd End { get; set; } = RecurrenceEnd.Never;

    public int? Count { get; set; }

    public DateOnly? Until { get; set; }
}
