//-----------------------------------------------------------------------
// <copyright file="DeadLetterSupressionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using FluentAssertions;
using FluentAssertions.Extensions;
using System.Threading.Tasks;

namespace Akka.Tests.Actor
{
    public class DeadLetterSupressionSpec : AkkaSpec
    {
        private class NormalMessage
        {
        }

        private class SuppressedMessage : IDeadLetterSuppression
        {
        }

        private class EchoActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }

        private readonly IActorRef deadActor;

        public DeadLetterSupressionSpec()
        {
            deadActor = Sys.ActorOf(Props.Create<EchoActor>());
            Watch(deadActor);
            deadActor.Tell(PoisonPill.Instance);
            ExpectTerminated(deadActor);
        }

        [Fact]
        public async Task Must_suppress_message_from_default_dead_letters_logging_sent_to_deadActor()
        {
            var deadListener = CreateTestProbe();
            Sys.EventStream.Subscribe(deadListener.Ref, typeof(DeadLetter));

            var suppressedListener = CreateTestProbe();
            Sys.EventStream.Subscribe(suppressedListener.Ref, typeof(SuppressedDeadLetter));

            var allListener = CreateTestProbe();
            Sys.EventStream.Subscribe(allListener.Ref, typeof(AllDeadLetters));

            deadActor.Tell(new SuppressedMessage());
            deadActor.Tell(new NormalMessage());

            var deadLetter = await deadListener.ExpectMsgAsync<DeadLetter>();
            deadLetter.Message.Should().BeOfType<NormalMessage>();
            deadLetter.Sender.Should().Be(TestActor);
            deadLetter.Recipient.Should().Be(deadActor);
            await deadListener.ExpectNoMsgAsync(200.Milliseconds());

            var suppressedDeadLetter = await suppressedListener.ExpectMsgAsync<SuppressedDeadLetter>();
            suppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();
            suppressedDeadLetter.Sender.Should().Be(TestActor);
            suppressedDeadLetter.Recipient.Should().Be(Sys.DeadLetters);
            await suppressedListener.ExpectNoMsgAsync(200.Milliseconds());

            var allSuppressedDeadLetter = await allListener.ExpectMsgAsync<SuppressedDeadLetter>();
            allSuppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();
            allSuppressedDeadLetter.Sender.Should().Be(TestActor);
            allSuppressedDeadLetter.Recipient.Should().Be(Sys.DeadLetters);

            var allDeadLetter = await allListener.ExpectMsgAsync<DeadLetter>();
            allDeadLetter.Message.Should().BeOfType<NormalMessage>();
            allDeadLetter.Sender.Should().Be(TestActor);
            allDeadLetter.Recipient.Should().Be(deadActor);

            await allListener.ExpectNoMsgAsync(200.Milliseconds());

            // unwrap for ActorSelection
            Sys.ActorSelection(deadActor.Path).Tell(new SuppressedMessage());
            Sys.ActorSelection(deadActor.Path).Tell(new NormalMessage());

            // the recipient ref isn't the same as deadActor here so only checking the message
            deadLetter = await deadListener.ExpectMsgAsync<DeadLetter>();//
            deadLetter.Message.Should().BeOfType<NormalMessage>();
            suppressedDeadLetter = await suppressedListener.ExpectMsgAsync<SuppressedDeadLetter>();
            suppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();

            await deadListener.ExpectNoMsgAsync(200.Milliseconds());
            await suppressedListener.ExpectNoMsgAsync(200.Milliseconds());
        }

        [Fact]
        public async Task Must_suppress_message_from_default_dead_letters_logging_sent_to_dead_letters()
        {
            var deadListener = CreateTestProbe();
            Sys.EventStream.Subscribe(deadListener.Ref, typeof(DeadLetter));

            var suppressedListener = CreateTestProbe();
            Sys.EventStream.Subscribe(suppressedListener.Ref, typeof(SuppressedDeadLetter));

            var allListener = CreateTestProbe();
            Sys.EventStream.Subscribe(allListener.Ref, typeof(AllDeadLetters));

            Sys.DeadLetters.Tell(new SuppressedMessage());
            Sys.DeadLetters.Tell(new NormalMessage());

            var deadLetter = await deadListener.ExpectMsgAsync<DeadLetter>(200.Milliseconds());
            deadLetter.Message.Should().BeOfType<NormalMessage>();
            deadLetter.Sender.Should().Be(TestActor);
            deadLetter.Recipient.Should().Be(Sys.DeadLetters);

            var suppressedDeadLetter = await suppressedListener.ExpectMsgAsync<SuppressedDeadLetter>(200.Milliseconds());
            suppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();
            suppressedDeadLetter.Sender.Should().Be(TestActor);
            suppressedDeadLetter.Recipient.Should().Be(Sys.DeadLetters);

            var allSuppressedDeadLetter = await allListener.ExpectMsgAsync<SuppressedDeadLetter>(200.Milliseconds());
            allSuppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();
            allSuppressedDeadLetter.Sender.Should().Be(TestActor);
            allSuppressedDeadLetter.Recipient.Should().Be(Sys.DeadLetters);

            var allDeadLetter = await allListener.ExpectMsgAsync<DeadLetter>(200.Milliseconds());
            allDeadLetter.Message.Should().BeOfType<NormalMessage>();
            allDeadLetter.Sender.Should().Be(TestActor);
            allDeadLetter.Recipient.Should().Be(Sys.DeadLetters);

            await Task.Delay(200);
            await deadListener.ExpectNoMsgAsync(TimeSpan.Zero);
            await suppressedListener.ExpectNoMsgAsync(TimeSpan.Zero);
            await allListener.ExpectNoMsgAsync(TimeSpan.Zero);

            // unwrap for ActorSelection
            Sys.ActorSelection(Sys.DeadLetters.Path).Tell(new SuppressedMessage());
            Sys.ActorSelection(Sys.DeadLetters.Path).Tell(new NormalMessage());

            deadLetter = await deadListener.ExpectMsgAsync<DeadLetter>();
            deadLetter.Message.Should().BeOfType<NormalMessage>();
            deadLetter.Sender.Should().Be(TestActor);
            deadLetter.Recipient.Should().Be(Sys.DeadLetters);
            suppressedDeadLetter = await suppressedListener.ExpectMsgAsync<SuppressedDeadLetter>();
            suppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();
            suppressedDeadLetter.Sender.Should().Be(TestActor);
            suppressedDeadLetter.Recipient.Should().Be(Sys.DeadLetters);

            await deadListener.ExpectNoMsgAsync(200.Milliseconds());
            await suppressedListener.ExpectNoMsgAsync(200.Milliseconds());
        }
    }
}
