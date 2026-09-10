namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>A refused resource: the precondition element the response must name, and why.</summary>
internal sealed record IcsProblem(IcsPrecondition Precondition, string Message);
