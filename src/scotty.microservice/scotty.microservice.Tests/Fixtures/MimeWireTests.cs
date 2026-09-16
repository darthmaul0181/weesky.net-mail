using MimeKit;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Fixtures;

public class MimeWireTests
{
    [Theory]
    [InlineData("a\r\nb\r\n", NewLineFormat.Unix, "a\nb\n")]
    [InlineData("a\nb\n", NewLineFormat.Dos, "a\r\nb\r\n")]
    public void TextOf_WritesTheAskedLineEndings_WhateverTheTextHeld(string text, NewLineFormat format, string expected) =>
        Assert.Equal(expected, MimeWire.TextOf(new TextPart("plain") { Text = text }, format));
}
