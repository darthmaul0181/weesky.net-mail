namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// What the sidebar's editor sends. A null field is one the caller did not touch: the colour and
/// the rank keep the values creation gave them.
/// </summary>
public sealed record CalendarWrite(string DisplayName, string? Description, string? Color, int? Order);
