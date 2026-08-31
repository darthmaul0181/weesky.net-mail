namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// What a <c>CARDDAV:address-data</c> element asked for. <see cref="Version"/> null means "as
/// stored", and an empty <see cref="PropertyNames"/> means the whole card — an address-data with
/// no <c>prop</c> child asks for everything, not for nothing.
/// </summary>
internal sealed record AddressDataRequest(string? Version, IReadOnlyList<string> PropertyNames);
