//-----------------------------------------------------------------------
// <copyright file="SerializeAllMessagesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Serialization;

public class SerializeAllMessagesSpec : AkkaSpec
{
    public SerializeAllMessagesSpec(ITestOutputHelper output) : base("akka.actor.serialize-messages = on", output)
    {
    }
    
    private class MyMessage : INoSerializationVerificationNeeded
    {
        public MyMessage(Action myAction)
        {
            MyAction = myAction;
        }

        // add an unserializable member, such as a delegate
        public Action MyAction { get; }
    }

    private class MyWrappedMessage : IWrappedMessage
    {
        public MyWrappedMessage(object message)
        {
            Message = message;
        }

        public object Message { get; }
    }
    
    // create an actor that will process a MyWrappedMessage and invoke the delegate
    private class MyActor : ReceiveActor
    {
        public MyActor()
        {
            Receive<MyWrappedMessage>(msg =>
            {
                var myMessage = (MyMessage) msg.Message;
                myMessage.MyAction();
            });
        }
    }
    
    [Fact]
    public async Task Should_not_serialize_WrappedMessage_with_INoSerializationVerificationNeeded()
    {
        // Arrange
        var myProbe = CreateTestProbe();
        var action = () => { myProbe.Tell("worked"); };
        var message = new MyMessage(action);
        var wrappedMessage = new MyWrappedMessage(message);
        
        var myActor = Sys.ActorOf(Props.Create(() => new MyActor()), "wrapped-message-actor");

        await EventFilter.Error().ExpectAsync(0, async () =>
        {
            // Act
            myActor.Tell(wrappedMessage);
            await myProbe.ExpectMsgAsync("worked");
        });
    }
}
