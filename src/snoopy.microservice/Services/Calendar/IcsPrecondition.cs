namespace weesky.Snoopy.Microservice.Services.Calendar;

/// <summary>The RFC 4791 § 5.3.2.1 preconditions a PUT can break, in the order they are judged.</summary>
internal enum IcsPrecondition
{
    SupportedCalendarData,
    ValidCalendarData,
    ValidCalendarObjectResource,
    SupportedCalendarComponent,
    MaxResourceSize,
    MaxInstances,
}
