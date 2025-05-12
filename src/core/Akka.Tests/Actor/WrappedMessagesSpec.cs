//-----------------------------------------------------------------------
// <copyright file="WrappedMessagesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests;

public class WrappedMessagesSpec
{
    private sealed record WrappedClass(object Message) : IWrappedMessage;
        
    private sealed record WrappedSuppressedClass(object Message) : IWrappedMessage, IDeadLetterSuppression;
        
    private sealed class SuppressedMessage : IDeadLetterSuppression
    {
            
    }
        
        
    [Fact]
    public void ShouldUnwrapWrappedMessage()
    {
        var message = new WrappedClass("chocolate-beans");
        var unwrapped = WrappedMessage.Unwrap(message);
        unwrapped.ShouldBe("chocolate-beans");
    }
        
    public static readonly TheoryData<object, bool> SuppressedMessages = new()
    {
        {new SuppressedMessage(), true},
        {new WrappedClass(new SuppressedMessage()), true},
        {new WrappedClass(new WrappedClass(new SuppressedMessage())), true},
        {new WrappedClass(new WrappedClass("chocolate-beans")), false},
        {new WrappedSuppressedClass("foo"), true},
        {new WrappedClass(new WrappedSuppressedClass("chocolate-beans")), true},
        {new WrappedClass("chocolate-beans"), false},
        {"chocolate-beans", false}
    };
        
    [Theory]
    [MemberData(nameof(SuppressedMessages))]
    public void ShouldDetectIfWrappedMessageIsSuppressed(object message, bool shouldBeSuppressed)
    {
        var isSuppressed = WrappedMessage.IsDeadLetterSuppressedAnywhere(message);
        isSuppressed.ShouldBe(shouldBeSuppressed);
    }
}
