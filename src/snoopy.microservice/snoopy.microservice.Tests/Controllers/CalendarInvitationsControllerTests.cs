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
    private readonly Mock<IInvitationReplyApplier> _applier = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();

    private CalendarInvitationsController Create()
    {
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Conn));
        return new CalendarInvitationsController(_responder.Object, _applier.Object, _connections.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", Guid.NewGuid()),
        };
    }

    private static RespondInvitationRequest Request() => new() { Folder = "INBOX", Uid = 7, Part = "2", Answer = InvitationAnswer.Accepted };

    private static readonly ApplyReplyRequest Reply = new() { Folder = "INBOX", Uid = 7, Part = "2" };

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

    [Fact]
    public async Task ApplyReply_ReturnsTheApplierAnswer_ForTheAuthenticatedUsersOwnMailbox()
    {
        var answer = new ApplyReplyResponse(new MailInvitation { Uid = "u" }, false, "calendar_busy");
        _applier.Setup(a => a.ApplyAsync(It.Is<User>(u => u.Email == "alice@weesky.be"), Conn, Reply, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<ApplyReplyResponse, ResponderFailure>(answer));

        var result = await Create().ApplyReply(Reply, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(answer, ok.Value);
    }

    [Theory]
    [InlineData(400, "reply_not_a_reply")]
    [InlineData(400, "reply_not_applicable")]
    [InlineData(404, "Message not found")]
    [InlineData(422, "invitation_too_large")]
    [InlineData(502, "why")]
    public async Task ApplyReply_MapsTheFailureStatus(int status, string code)
    {
        _applier.Setup(a => a.ApplyAsync(It.IsAny<User>(), Conn, Reply, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ApplyReplyResponse, ResponderFailure>(new ResponderFailure(status, code)));

        var result = await Create().ApplyReply(Reply, CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(status, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal((ResultState.Error, code), (envelope.State, envelope.Message));
    }

    [Fact]
    public async Task ApplyReply_WithoutCredentials_Is401()
    {
        var controller = Create();
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MailAccountConnection>("credentials_unavailable"));

        var result = await controller.ApplyReply(Reply, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);
        _applier.VerifyNoOtherCalls();
    }
}
