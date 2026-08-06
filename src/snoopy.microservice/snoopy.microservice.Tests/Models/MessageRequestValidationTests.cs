using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

// Same rule as FolderRequestValidationTests: attribute wording mirrors the controllers'
// guards. What only the controller can judge (target differs from source, at least one
// search criterion, the exact purpose literal) deliberately has no attribute here.
public sealed class MessageRequestValidationTests
{
    [Fact]
    public void MessageBatch_WithoutFolderAndUids_NamesBoth()
    {
        var messages = RequestValidation.Messages(new SetMessageFlagsRequest());

        Assert.Contains("A folder is required", messages);
        Assert.Contains("Uids must hold between 1 and 200 entries", messages);
    }

    [Fact]
    public void MessageBatch_Above200Uids_IsRefused()
    {
        var request = new DeleteMessagesRequest
        {
            FolderPath = "INBOX",
            Uids = [.. Enumerable.Range(1, 201).Select(i => (uint)i)],
        };

        Assert.Contains("Uids must hold between 1 and 200 entries", RequestValidation.Messages(request));
    }

    [Fact]
    public void MessageBatch_WithOneUid_IsValid()
    {
        var request = new SetMessageFlagsRequest { FolderPath = "INBOX", Uids = [7u] };

        Assert.Empty(RequestValidation.Messages(request));
    }

    [Fact]
    public void MoveMessages_WithoutATarget_IsRefused()
    {
        var request = new MoveMessagesRequest { FolderPath = "INBOX", Uids = [7u] };

        Assert.Contains("A target folder is required", RequestValidation.Messages(request));
    }

    [Fact]
    public void EmptyFolder_WithoutATarget_IsAValidPurge()
    {
        Assert.Empty(RequestValidation.Messages(new EmptyFolderRequest { FolderPath = "Trash" }));
    }

    [Fact]
    public void EmptyFolder_WithoutAFolder_IsRefused()
    {
        Assert.Contains("A folder is required", RequestValidation.Messages(new EmptyFolderRequest()));
    }

    [Fact]
    public void Search_WithDefaults_OnlyLacksTheFolder()
    {
        Assert.Equal(["A folder is required"], RequestValidation.Messages(new SearchMessagesRequest()));
    }

    [Fact]
    public void Search_WithANegativePage_IsRefused()
    {
        var request = new SearchMessagesRequest { FolderPath = "INBOX", Page = -1 };

        Assert.Contains("Page must not be negative", RequestValidation.Messages(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Search_WithAPageSizeOutOfBounds_IsRefused(int pageSize)
    {
        var request = new SearchMessagesRequest { FolderPath = "INBOX", PageSize = pageSize };

        Assert.Contains("Page size must be between 1 and 200", RequestValidation.Messages(request));
    }

    [Fact]
    public void PrepareQuote_WithoutFolderAndPurpose_NamesBoth()
    {
        var messages = RequestValidation.Messages(new PrepareQuoteRequest());

        Assert.Contains("A folder is required", messages);
        Assert.Contains("Purpose must be reply, forward or editAsNew", messages);
    }

    [Fact]
    public void PrepareQuote_DoesNotJudgeThePurposeLiteral_ThatIsTheControllersRule()
    {
        var request = new PrepareQuoteRequest { Folder = "INBOX", Uid = 4, Purpose = "REPLY" };

        Assert.Empty(RequestValidation.Messages(request));
    }

    [Fact]
    public void OpenDraft_WithoutAFolder_IsRefused()
    {
        Assert.Contains("A folder is required", RequestValidation.Messages(new OpenDraftRequest { Folder = "", Uid = 5 }));
    }

    [Fact]
    public void OpenDraft_WithAFolder_IsValid()
    {
        Assert.Empty(RequestValidation.Messages(new OpenDraftRequest { Folder = "Drafts", Uid = 5 }));
    }

    // ── Outgoing message ceilings ───────────────────────────────────────
    // Every other request on this API bounds what it accepts; this one bounded nothing at all,
    // so one authenticated caller could turn a single POST into a mass mailing.

    [Fact]
    public void Send_Above100RecipientsInOneList_IsRefused()
    {
        var request = new SendMessageRequest
        {
            To = [.. Enumerable.Range(1, 101).Select(i => $"a{i}@weesky.be")],
        };

        Assert.Contains("A message cannot name more than 100 recipients", RequestValidation.Messages(request));
    }

    // The cumulative rule is the controller's — three lists of 100 pass every attribute and still
    // address 300 people. This pins that the attributes alone do not, so the guard cannot be dropped.
    [Fact]
    public void Send_With100InEachList_PassesTheAttributesAlone()
    {
        var hundred = Enumerable.Range(1, 100).Select(i => $"a{i}@weesky.be").ToList();
        var request = new SendMessageRequest { To = hundred, Cc = hundred, Bcc = hundred };

        Assert.Empty(RequestValidation.Messages(request));
    }

    [Fact]
    public void Send_WithAnOverLongSubject_IsRefused()
    {
        var request = new SendMessageRequest { Subject = new string('s', 999) };

        Assert.Contains("The subject must be at most 998 characters", RequestValidation.Messages(request));
    }

    [Fact]
    public void Send_WithAnOversizedBody_IsRefused()
    {
        var request = new SendMessageRequest
        {
            HtmlBody = new string('x', SendMessageRequest.MaxBodyLength + 1),
        };

        Assert.Contains("The message body is too large", RequestValidation.Messages(request));
    }

    [Fact]
    public void Send_Above50Attachments_IsRefused()
    {
        var request = new SendMessageRequest
        {
            AttachmentIds = [.. Enumerable.Range(1, 51).Select(_ => Guid.NewGuid())],
        };

        Assert.Contains("A message cannot carry more than 50 attachments", RequestValidation.Messages(request));
    }

    // A draft is the send shape minus the recipient gate: it inherits every ceiling above.
    [Fact]
    public void Draft_InheritsTheSendCeilings()
    {
        var request = new SaveDraftRequest { Subject = new string('s', 999), ReplaceUid = 12 };

        Assert.Contains("The subject must be at most 998 characters", RequestValidation.Messages(request));
    }

    [Fact]
    public void Send_WithAnOrdinaryMessage_IsValid()
    {
        var request = new SendMessageRequest
        {
            To = ["bob@weesky.be"],
            Subject = "Lunch",
            HtmlBody = "<p>At one?</p>",
        };

        Assert.Empty(RequestValidation.Messages(request));
    }
}
