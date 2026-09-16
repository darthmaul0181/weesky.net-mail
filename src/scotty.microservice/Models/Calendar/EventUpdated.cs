namespace weesky.Scotty.Microservice.Models.Calendar;

/// <summary>What a 200 on an update answers: the sum of what the hook sent for every resource the write touched.</summary>
public sealed record EventUpdated(SchedulingReport Scheduling);
