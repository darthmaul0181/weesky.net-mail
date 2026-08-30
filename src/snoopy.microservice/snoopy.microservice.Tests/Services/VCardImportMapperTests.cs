using weesky.Snoopy.Microservice.Services.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class VCardImportMapperTests
{
    [Fact]
    public void RawValueOf_ReadsPastAQuotedColonInAParameter() =>
        Assert.Equal("u1", VCardImportMapper.RawValueOf(
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID;X-A=\"a:b\":u1\r\nEND:VCARD\r\n", "UID"));

    [Fact]
    public void RawValueOf_UnfoldsTheValueAndStripsTheGroup() =>
        Assert.Equal("u1", VCardImportMapper.RawValueOf(
            "BEGIN:VCARD\r\nitem1.UID:u\r\n 1\r\nEND:VCARD\r\n", "UID"));

    [Fact]
    public void RawValueOf_StopsAtTheEndOfTheCard() =>
        Assert.Null(VCardImportMapper.RawValueOf("BEGIN:VCARD\r\nEND:VCARD\r\nUID:after\r\n", "UID"));

    [Fact]
    public void RawValueOf_AnEmptyValue_IsAbsent() =>
        Assert.Null(VCardImportMapper.RawValueOf("BEGIN:VCARD\r\nUID:\r\nEND:VCARD\r\n", "UID"));
}
