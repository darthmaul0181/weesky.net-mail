using DnsClient;

namespace weesky.Scotty.Microservice.Services.Bimi;

internal sealed class DnsClientTxtResolver : IDnsTxtResolver
{
    private readonly LookupClient client = new(new LookupClientOptions
    {
        Timeout = TimeSpan.FromSeconds(2),
        Retries = 1,
        ThrowDnsErrors = false,
    });

    public async Task<IReadOnlyList<string>> GetTxtAsync(string name, CancellationToken cancellationToken) =>
        Read(await client.QueryAsync(name, QueryType.TXT, cancellationToken: cancellationToken));

    internal static IReadOnlyList<string> Read(IDnsQueryResponse response) =>
        response.HasError && response.Header.ResponseCode != DnsHeaderResponseCode.NotExistentDomain
            ? throw new DnsResponseException((DnsResponseCode)response.Header.ResponseCode, response.ErrorMessage)
            : [.. response.Answers.TxtRecords().Select(record => string.Concat(record.Text))];
}
