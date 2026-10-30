using DnsClient;
using DnsClient.Protocol;
using Moq;
using weesky.Scotty.Microservice.Services.Bimi;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Bimi;

public sealed class DnsClientTxtResolverTests
{
    private const string Name = "default._bimi.example.com";

    [Fact]
    public void Each_record_joins_its_strings()
    {
        var records = DnsClientTxtResolver.Read(Response(DnsHeaderResponseCode.NoError, ["v=BIMI1; l=https://ex", "ample.com/l.svg"], ["other"]));

        Assert.Equal(["v=BIMI1; l=https://example.com/l.svg", "other"], records);
    }

    [Fact]
    public void A_name_that_does_not_exist_has_no_records() =>
        Assert.Empty(DnsClientTxtResolver.Read(Response(DnsHeaderResponseCode.NotExistentDomain)));

    [Fact]
    public void A_server_failure_is_an_error() =>
        Assert.Throws<DnsResponseException>(() => DnsClientTxtResolver.Read(Response(DnsHeaderResponseCode.ServerFailure)));

    private static IDnsQueryResponse Response(DnsHeaderResponseCode code, params string[][] records)
    {
        var info = new ResourceRecordInfo(Name, ResourceRecordType.TXT, QueryClass.IN, 300, 0);
        var response = new Mock<IDnsQueryResponse>();
        response.Setup(r => r.HasError).Returns(code != DnsHeaderResponseCode.NoError);
        response.Setup(r => r.ErrorMessage).Returns(code.ToString());
        response.Setup(r => r.Header).Returns(new DnsResponseHeader(1, (ushort)code, 1, records.Length, 0, 0));
        response.Setup(r => r.Answers).Returns([.. records.Select(text => new TxtRecord(info, text, text))]);
        return response.Object;
    }
}
