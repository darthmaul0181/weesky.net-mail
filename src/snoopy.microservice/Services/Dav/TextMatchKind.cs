namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>The four match-type values of CARDDAV:text-match; contains is the default.</summary>
internal enum TextMatchKind
{
    Contains,
    Equals,
    StartsWith,
    EndsWith,
}
