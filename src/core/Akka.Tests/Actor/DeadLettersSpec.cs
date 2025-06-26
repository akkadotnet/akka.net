//-----------------------------------------------------------------------
// <copyright file="DeadLettersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests
{
    public class DeadLettersSpec : AkkaSpec
    {
        [Fact]
        public async Task Can_send_messages_to_dead_letters()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            Sys.DeadLetters.Tell("foobar");
            await ExpectMsgAsync<DeadLetter>(deadLetter=>deadLetter.Message.Equals("foobar"));
        }

        private sealed record WrappedClass(object Message) : IWrappedMessage;
        
        private sealed class SuppressedMessage : IDeadLetterSuppression
        {
            
        }

        [Fact]
        public async Task ShouldLogNormalWrappedMessages()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            Sys.DeadLetters.Tell(new WrappedClass("chocolate-beans"));
            await ExpectMsgAsync<DeadLetter>();
        }
        
        [Fact]
        public async Task ShouldNotLogWrappedMessagesWithDeadLetterSuppression()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(AllDeadLetters));
            Sys.DeadLetters.Tell(new WrappedClass(new SuppressedMessage()));
            var msg = await ExpectMsgAsync<SuppressedDeadLetter>();
            msg.Message.ToString()!.Contains("SuppressedMessage").ShouldBeTrue();
        }
        
        [Fact]
        public async Task ShouldLogNormalActorSelectionWrappedMessages()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            var selection = Sys.ActorSelection("/user/foobar");
            selection.Tell(new WrappedClass("chocolate-beans"));
            await ExpectMsgAsync<DeadLetter>();
        }
        
        [Fact]
        public async Task ShouldNotLogActorSelectionWrappedMessagesWithDeadLetterSuppression()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(AllDeadLetters));
            var selection = Sys.ActorSelection("/user/foobar");
            selection.Tell(new WrappedClass(new SuppressedMessage()));
            var msg = await ExpectMsgAsync<SuppressedDeadLetter>();
            msg.Message.ToString()!.Contains("SuppressedMessage").ShouldBeTrue();
        }
    }
}
