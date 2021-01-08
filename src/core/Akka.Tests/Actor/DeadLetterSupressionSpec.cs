//-----------------------------------------------------------------------
// <copyright file="DeadLetterSupressionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

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
        public void Must_suppress_message_from_default_dead_letters_logging_sent_to_deadActor()
        {
            var deadListener = CreateTestProbe();
            Sys.EventStream.Subscribe(deadListener.Ref, typeof(DeadLetter));

            var suppressedListener = CreateTestProbe();
            Sys.EventStream.Subscribe(suppressedListener.Ref, typeof(SuppressedDeadLetter));

            var allListener = CreateTestProbe();
            Sys.EventStream.Subscribe(allListener.Ref, typeof(AllDeadLetters));

            deadActor.Tell(new SuppressedMessage());
            deadActor.Tell(new NormalMessage());

            var deadLetter = deadListener.ExpectMsg<DeadLetter>();
            deadLetter.Message.Should().BeOfType<NormalMessage>();
            deadLetter.Sender.Should().Be(TestActor);
            deadLetter.Recipient.Should().Be(deadActor);
            deadListener.ExpectNoMsg(200.Milliseconds());

            var suppressedDeadLetter = suppressedListener.ExpectMsg<SuppressedDeadLetter>();
            suppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();
            suppressedDeadLetter.Sender.Should().Be(TestActor);
            suppressedDeadLetter.Recipient.Should().Be(Sys.DeadLetters);
            suppressedListener.ExpectNoMsg(200.Milliseconds());

            var allSuppressedDeadLetter = allListener.ExpectMsg<SuppressedDeadLetter>();
            allSuppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();
            allSuppressedDeadLetter.Sender.Should().Be(TestActor);
            allSuppressedDeadLetter.Recipient.Should().Be(Sys.DeadLetters);

            var allDeadLetter = allListener.ExpectMsg<DeadLetter>();
            allDeadLetter.Message.Should().BeOfType<NormalMessage>();
            allDeadLetter.Sender.Should().Be(TestActor);
            allDeadLetter.Recipient.Should().Be(deadActor);

            allListener.ExpectNoMsg(200.Milliseconds());
        }

        [Fact]
        public void Must_suppress_message_from_default_dead_letters_logging_sent_to_dead_letters()
        {
            var deadListener = CreateTestProbe();
            Sys.EventStream.Subscribe(deadListener.Ref, typeof(DeadLetter));

            var suppressedListener = CreateTestProbe();
            Sys.EventStream.Subscribe(suppressedListener.Ref, typeof(SuppressedDeadLetter));

            var allListener = CreateTestProbe();
            Sys.EventStream.Subscribe(allListener.Ref, typeof(AllDeadLetters));

            Sys.DeadLetters.Tell(new SuppressedMessage());
            Sys.DeadLetters.Tell(new NormalMessage());

            var deadLetter = deadListener.ExpectMsg<DeadLetter>(200.Milliseconds());
            deadLetter.Message.Should().BeOfType<NormalMessage>();
            deadLetter.Sender.Should().Be(TestActor);
            deadLetter.Recipient.Should().Be(Sys.DeadLetters);

            var suppressedDeadLetter = suppressedListener.ExpectMsg<SuppressedDeadLetter>(200.Milliseconds());
            suppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();
            suppressedDeadLetter.Sender.Should().Be(TestActor);
            suppressedDeadLetter.Recipient.Should().Be(Sys.DeadLetters);

            var allSuppressedDeadLetter = allListener.ExpectMsg<SuppressedDeadLetter>(200.Milliseconds());
            allSuppressedDeadLetter.Message.Should().BeOfType<SuppressedMessage>();
            allSuppressedDeadLetter.Sender.Should().Be(TestActor);
            allSuppressedDeadLetter.Recipient.Should().Be(Sys.DeadLetters);

            var allDeadLetter = allListener.ExpectMsg<DeadLetter>(200.Milliseconds());
            allDeadLetter.Message.Should().BeOfType<NormalMessage>();
            allDeadLetter.Sender.Should().Be(TestActor);
            allDeadLetter.Recipient.Should().Be(Sys.DeadLetters);

            Thread.Sleep(200);
            deadListener.ExpectNoMsg(TimeSpan.Zero);
            suppressedListener.ExpectNoMsg(TimeSpan.Zero);
            allListener.ExpectNoMsg(TimeSpan.Zero);
        }
    }
}
