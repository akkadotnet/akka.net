//-----------------------------------------------------------------------
// <copyright file="ActorRefIgnoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;
using Akka.Util.Internal;
using FluentAssertions;

namespace Akka.Tests.Actor
{
    public class ActorRefIgnoreSpec : AkkaSpec, INoImplicitSender
    {
        [Fact]
        public void IgnoreActorRef_should_ignore_all_incoming_messages()
        {
            var askMeRef = Sys.ActorOf(Props.Create(() => new AskMeActor()));

            var probe = CreateTestProbe("response-probe");
            askMeRef.Tell(new Request(probe.Ref));
            probe.ExpectMsg(1);

            // this is more a compile-time proof
            // since the reply is ignored, we can't check that a message was sent to it
            askMeRef.Tell(new Request(Sys.IgnoreRef));

            probe.ExpectNoMsg();

            // but we do check that the counter has increased when we used the ActorRef.ignore
            askMeRef.Tell(new Request(probe.Ref));
            probe.ExpectMsg(3);
        }

        [Fact]
        public void IgnoreActorRef_should_make_a_Future_timeout_when_used_in_a_ask()
        {
            // this is kind of obvious, the Future won't complete because the ignoreRef is used

            var timeout = TimeSpan.FromMilliseconds(500);
            var askMeRef = Sys.ActorOf(Props.Create(() => new AskMeActor()));

            Assert.Throws<AskTimeoutException>(() =>
            {
                _ = askMeRef.Ask(new Request(Sys.IgnoreRef), timeout).GetAwaiter().GetResult();
            });
        }

        [Fact]
        public void IgnoreActorRef_should_be_watchable_from_another_actor_without_throwing_an_exception()
        {
            var probe = CreateTestProbe("probe-response");
            var forwardMessageRef = Sys.ActorOf(Props.Create(() => new ForwardMessageWatchActor(probe)));

            // this proves that the actor started and is operational and 'watch' didn't impact it
            forwardMessageRef.Tell("abc");
            probe.ExpectMsg("abc");
        }

        [Fact]
        public void IgnoreActorRef_should_be_a_singleton()
        {
            Sys.IgnoreRef.Should().BeSameAs(Sys.IgnoreRef);
        }

        /// <summary>
        /// this Actor behavior receives simple request and answers back total number of
        /// messages it received so far
        /// </summary>
        internal class AskMeActor : ActorBase
        {
            private int counter;

            public AskMeActor()
            {
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case Request r:
                        counter++;
                        r.ReplyTo.Tell(counter);
                        return true;
                }
                return false;
            }
        }

        internal class Request
        {
            public Request(IActorRef replyTo)
            {
                ReplyTo = replyTo;
            }

            public IActorRef ReplyTo { get; }
        }

        internal class ForwardMessageWatchActor : ActorBase
        {
            private readonly IActorRef probe;

            public ForwardMessageWatchActor(IActorRef probe)
            {
                Context.Watch(Context.System.IgnoreRef);
                this.probe = probe;
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case string str:
                        probe.Tell(str);
                        return true;
                }
                return false;
            }
        }
    }
}

