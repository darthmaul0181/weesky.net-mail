namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// The ten colours a new calendar is given, in order, the first being the one the mock-ups paint
/// the default calendar with. Ten and not a random draw: two calendars a user cannot tell apart
/// are worse than two that repeat after ten.
/// </summary>
internal static class CalendarPalette
{
    internal static readonly IReadOnlyList<string> Colours =
    [
        "#3b82c4", "#e0603a", "#4aa564", "#9b59b6", "#e0a63a",
        "#2f9e9e", "#c0507a", "#6b7ec4", "#7a9e3a", "#8a6a4a",
    ];

    /// <summary>The colour a user's next calendar takes, <paramref name="count"/> being how many
    /// they already hold.</summary>
    internal static string Next(int count) => Colours[Math.Abs(count) % Colours.Count];
}
