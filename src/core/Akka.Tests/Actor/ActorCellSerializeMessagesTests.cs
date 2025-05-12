//-----------------------------------------------------------------------
// <copyright file="ActorCellSerializeMessagesTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Tests.Actor;

public class ActorCellSerializeMessagesTests: AkkaSpec
{
    private class InnerMessage
    {
        public InnerMessage(string payload)
        {
            Payload = payload;
        }

        public string Payload { get; }
    }
    
    private class MessageWrapper: IWrappedMessage
    {
        public MessageWrapper(object message)
        {
            Message = message;
        }

        public object Message { get; }
    }
    
    private class EchoActor: ReceiveActor
    {
        public EchoActor()
        {
            ReceiveAny(msg => Sender.Tell(msg));
        }
    }
    
    public ActorCellSerializeMessagesTests(ITestOutputHelper output) : base("akka.actor.serialize-messages=on", output)
    {
    }

    [Fact(DisplayName = "Wrapped message should be received by actors intact")]
    public void WrappedMessageTest()
    {
        var actor = Sys.ActorOf(Props.Create(() => new EchoActor()));
        actor.Tell(new MessageWrapper(new InnerMessage("payload")));
        var receivedMsg = ExpectMsg<MessageWrapper>();
        receivedMsg.Message.Should().BeOfType<InnerMessage>().Which.Payload.Should().Be("payload");
    }
}
