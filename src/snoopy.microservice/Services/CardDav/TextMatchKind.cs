namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>The four match-type values of CARDDAV:text-match; contains is the default.</summary>
internal enum TextMatchKind
{
    Contains,
    Equals,
    StartsWith,
    EndsWith,
}
