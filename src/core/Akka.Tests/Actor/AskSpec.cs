﻿//-----------------------------------------------------------------------
// <copyright file="AskSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.TestKit;
using Xunit;
using Akka.Actor;
using System;
using System.Threading;

namespace Akka.Tests.Actor
{
    
    public class AskSpec : AkkaSpec
    {
        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message.Equals("timeout"))
                {
                    Thread.Sleep(5000);
                }
                if (message.Equals("answer"))
                {
                    Sender.Tell("answer");
                }
            }
        }

        public class WaitActor : UntypedActor
        {
            public WaitActor(IActorRef replyActor, IActorRef testActor)
            {
                _replyActor = replyActor;
                _testActor = testActor;
            }

            private IActorRef _replyActor;

            private IActorRef _testActor;

            protected override void OnReceive(object message)
            {
                if (message.Equals("ask"))
                {
                    var result = _replyActor.Ask("foo");
                    result.Wait(TimeSpan.FromSeconds(2));
                    _testActor.Tell(result.Result);
                }
            }
        }

        public class ReplyActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message.Equals("foo"))
                {
                    Sender.Tell("bar");
                }
            }
        }

        [Fact]
        public void CanAskActor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            actor.Ask<string>("answer").Result.ShouldBe("answer");
        }

        [Fact]
        public void CanAskActorWithTimeout()
        {
            var actor = Sys.ActorOf<SomeActor>();
            actor.Ask<string>("answer",TimeSpan.FromSeconds(10)).Result.ShouldBe("answer");
        }

        [Fact]
        public void CanGetTimeoutWhenAskingActor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            Assert.Throws<AggregateException>(() => { actor.Ask<string>("timeout", TimeSpan.FromSeconds(3)).Wait(); });
        }

        /// <summary>
        /// Tests to ensure that if we wait on the result of an Ask inside an actor's receive loop
        /// that we don't deadlock
        /// </summary>
        [Fact]
        public void CanAskActorInsideReceiveLoop()
        {
            var replyActor = Sys.ActorOf<ReplyActor>();
            var waitActor = Sys.ActorOf(Props.Create(() => new WaitActor(replyActor, TestActor)));
            waitActor.Tell("ask");
            ExpectMsg("bar");
        }
    }
}

