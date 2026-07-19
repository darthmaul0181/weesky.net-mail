namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>Where a discovered role came from: a server SPECIAL-USE flag, or a name guess.</summary>
    public readonly record struct SpecialUseAssignment(string Role, string Source)
    {
        public const string FromFlag = "specialUse";
        public const string FromName = "name";
    }
}
