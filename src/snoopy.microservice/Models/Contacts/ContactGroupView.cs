namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// One group as the client reads it. The card itself never leaves: a group is a name and a list of
/// contacts to every screen that shows one.
/// </summary>
/// <param name="Id">The group's identifier.</param>
/// <param name="Name">The card's FN.</param>
/// <param name="MemberIds">
/// The members this book actually holds, in the order the card lists them. A MEMBER pointing at
/// nothing, at another book, or at a group is no contact and is left out (décisions 2 and 9).
/// </param>
public sealed record ContactGroupView(Guid Id, string Name, IReadOnlyList<Guid> MemberIds);
