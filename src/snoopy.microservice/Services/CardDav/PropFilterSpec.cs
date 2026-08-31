namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// One prop-filter. Its conditions apply to the card's instances of the named property, the name
/// compared case-insensitively and without any group prefix; no children at all means « the
/// property exists ».
/// </summary>
internal sealed record PropFilterSpec(
    string Name,
    bool AllOf,
    bool IsNotDefined,
    IReadOnlyList<TextMatchSpec> TextMatches,
    IReadOnlyList<ParamFilterSpec> ParamFilters);
