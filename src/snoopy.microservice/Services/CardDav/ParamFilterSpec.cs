namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// One param-filter, evaluated on the parameters of the property its prop-filter retained. No
/// child at all means « the parameter exists ».
/// </summary>
internal sealed record ParamFilterSpec(string Name, bool IsNotDefined, TextMatchSpec? TextMatch);
