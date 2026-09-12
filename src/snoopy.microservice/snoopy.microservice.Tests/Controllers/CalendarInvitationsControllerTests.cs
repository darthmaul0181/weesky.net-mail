using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CalendarInvitationsControllerTests
{
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private readonly Mock<IInvitationResponder> _responder = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();

    private CalendarInvitationsController Create()
    {
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Conn));
        return new CalendarInvitationsController(_responder.Object, _connections.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", Guid.NewGuid()),
        };
    }

    private static RespondInvitationRequest Request() => new() { Folder = "INBOX", Uid = 7, Part = "2", Answer = InvitationAnswer.Accepted };

    [Fact]
    public async Task Respond_ReturnsTheResponderAnswer()
    {
        var answer = new InvitationResponse(new MailInvitation { Uid = "u" }, true, null, false);
        _responder.Setup(r => r.RespondAsync(It.IsAny<User>(), Conn, It.IsAny<RespondInvitationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<InvitationResponse, ResponderFailure>(answer));

        var result = await Create().Respond(Request(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(answer, ok.Value);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(502)]
    public async Task Respond_MapsTheFailureStatus(int status)
    {
        _responder.Setup(r => r.RespondAsync(It.IsAny<User>(), Conn, It.IsAny<RespondInvitationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<InvitationResponse, ResponderFailure>(new ResponderFailure(status, "why")));

        var result = await Create().Respond(Request(), CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(status, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal(ResultState.Error, envelope.State);
        Assert.Equal("why", envelope.Message);
    }

    [Fact]
    public async Task Respond_WithoutCredentials_Is401()
    {
        var controller = Create();
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MailAccountConnection>("credentials_unavailable"));

        var result = await controller.Respond(Request(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);
        _responder.VerifyNoOtherCalls();
    }
}
