namespace weesky.Scotty.Microservice.Models.Calendar;

public static class CalendarKinds
{
    public const string Regular = "regular";
    public const string Birthdays = "birthdays";
}

public static class BirthdayReminders
{
    public const string None = "none";
    public const string SameDay = "same_day";
    public const string DayBefore = "day_before";
    public const string WeekBefore = "week_before";
    public const string Default = SameDay;
    public static readonly IReadOnlyList<string> All = [None, SameDay, DayBefore, WeekBefore];
}

public static class BirthdayLanguages
{
    public const string Fr = "fr";
    public const string En = "en";

    /// <summary>The first of the candidates that is a language the file is written in; English otherwise.</summary>
    public static string Of(params string?[] candidates) =>
        candidates.FirstOrDefault(c => c is Fr or En) ?? En;
}
