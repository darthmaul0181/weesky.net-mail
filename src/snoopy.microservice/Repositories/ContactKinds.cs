namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>The card's two species. Pinned strings: the MariaDB ENUM refuses anything else.</summary>
public static class ContactKinds
{
    public const string Individual = "individual";
    public const string Group = "group";
}
