namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>What a 201 answers: the new row's identifier and, from the events API, what the invitation hook sent.</summary>
public sealed record CreatedId(Guid Id, SchedulingReport? Scheduling = null);
