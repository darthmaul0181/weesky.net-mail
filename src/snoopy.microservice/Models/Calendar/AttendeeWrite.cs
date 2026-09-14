namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>A guest as the editor names them: the address, lower-cased and bare, and the name the
/// client's contacts know — the server has no lookup by address (spec 5e, décision 8).</summary>
public sealed record AttendeeWrite(string Email, string? Name);
