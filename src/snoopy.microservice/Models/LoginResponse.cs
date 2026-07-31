namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// What a successful login answers. The JWT itself is deliberately absent: it travels only in the
/// HttpOnly cookie, so no page script, devtool or intermediary log ever sees it.
/// </summary>
/// <param name="ExpiresIn">Inactivity window of the issued session, in minutes.</param>
public sealed record LoginResponse(long ExpiresIn);
