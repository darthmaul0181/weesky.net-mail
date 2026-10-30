namespace weesky.Scotty.Microservice.Services.Bimi;

internal interface IDnsTxtResolver
{
    /// <summary>The TXT records of a name; empty when it has none, an exception when the lookup itself failed.</summary>
    Task<IReadOnlyList<string>> GetTxtAsync(string name, CancellationToken cancellationToken);
}
