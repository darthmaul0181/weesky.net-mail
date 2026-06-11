using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public class ResultEnveloppeTests
{
    [Fact]
    public void CreateSuccessEnveloppe_WithMessage_SetsMessage()
    {
        var result = ResultEnveloppe.CreateSuccessEnveloppe("done");
        Assert.Equal("done", result.Message);
        Assert.Equal(ResultState.Success, result.State);
    }

    [Fact]
    public void CreateSuccessEnveloppe_WithoutMessage_HasNullMessage()
    {
        var result = ResultEnveloppe.CreateSuccessEnveloppe();
        Assert.Null(result.Message);
    }

    [Fact]
    public void CreateErrorEnveloppe_SetsErrorStateAndMessage()
    {
        var result = ResultEnveloppe.CreateErrorEnveloppe("oops");
        Assert.Equal(ResultState.Error, result.State);
        Assert.Equal("oops", result.Message);
    }

    [Fact]
    public void GenericResultEnveloppe_CanHoldTypedResult()
    {
        var result = new ResultEnveloppe<int> { Result = 42 };
        Assert.Equal(42, result.Result);
    }
}
