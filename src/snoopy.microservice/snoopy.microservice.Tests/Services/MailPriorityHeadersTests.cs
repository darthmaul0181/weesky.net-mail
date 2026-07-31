using System.Text.Json;
using System.Text.Json.Serialization;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailPriorityHeadersTests
{
    private static MimeMessage Applied(MailPriority priority)
    {
        var message = new MimeMessage();
        MailPriorityHeaders.Apply(message, priority);
        return message;
    }

    [Fact]
    public void HighWritesTheThreeHeaders()
    {
        var message = Applied(MailPriority.High);

        Assert.Equal("1 (Highest)", message.Headers["X-Priority"]);
        Assert.Equal("high", message.Headers["Importance"]);
        Assert.Equal("High", message.Headers["X-MSMail-Priority"]);
    }

    [Fact]
    public void LowWritesTheThreeHeaders()
    {
        var message = Applied(MailPriority.Low);

        Assert.Equal("5 (Lowest)", message.Headers["X-Priority"]);
        Assert.Equal("low", message.Headers["Importance"]);
        Assert.Equal("Low", message.Headers["X-MSMail-Priority"]);
    }

    /// <summary>An ordinary message says nothing about its priority — three absent headers, not "3".</summary>
    [Fact]
    public void NormalWritesNothing()
    {
        var message = Applied(MailPriority.Normal);

        Assert.Null(message.Headers["X-Priority"]);
        Assert.Null(message.Headers["Importance"]);
        Assert.Null(message.Headers["X-MSMail-Priority"]);
    }

    /// <summary>The pair exists so the two directions cannot drift; this is the assertion that says so.</summary>
    [Theory]
    [InlineData(MailPriority.High)]
    [InlineData(MailPriority.Low)]
    [InlineData(MailPriority.Normal)]
    public void WhatIsWrittenIsWhatIsReadBack(MailPriority priority) =>
        Assert.Equal(priority, MailPriorityReader.Parse(Applied(priority).Headers));

    /// <summary>Program.cs registers a bare JsonStringEnumConverter, which would otherwise emit "High".</summary>
    [Theory]
    [InlineData(MailPriority.Normal, "\"normal\"")]
    [InlineData(MailPriority.High, "\"high\"")]
    [InlineData(MailPriority.Low, "\"low\"")]
    public void SerialisesToTheLowerCaseWireValue(MailPriority priority, string expected)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        Assert.Equal(expected, JsonSerializer.Serialize(priority, options));
    }

    [Fact]
    public void DeserialisesFromTheLowerCaseWireValue()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        Assert.Equal(MailPriority.High, JsonSerializer.Deserialize<MailPriority>("\"high\"", options));
    }
}
