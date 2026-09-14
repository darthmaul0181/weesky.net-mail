namespace weesky.Snoopy.Microservice.Models;

/// <summary>A connection test's verdict. <c>Error</c> is one of <c>SchedulingAccountTestErrors</c>, absent on success.</summary>
public sealed record SchedulingAccountTestResult(bool Ok, string? Error = null);
