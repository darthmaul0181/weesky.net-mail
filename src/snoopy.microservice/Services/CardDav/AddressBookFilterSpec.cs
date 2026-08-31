namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// A parsed addressbook-query filter. No prop-filter at all means the whole book — a special case
/// the evaluator takes before any anyof/allof logic, never a consequence of it: anyof over zero
/// tests would say false and hand an empty book to a client that asked for everything.
/// </summary>
internal sealed record AddressBookFilterSpec(bool AllOf, IReadOnlyList<PropFilterSpec> PropFilters);
