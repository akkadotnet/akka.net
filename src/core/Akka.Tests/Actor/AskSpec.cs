//-----------------------------------------------------------------------
// <copyright file="AskSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public void Can_Ask_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            actor.Ask<string>("answer").Result.ShouldBe("answer");
        }

        [Fact]
        public void Can_Ask_actor_with_timeout()
        {
            var actor = Sys.ActorOf<SomeActor>();
            actor.Ask<string>("answer",TimeSpan.FromSeconds(10)).Result.ShouldBe("answer");
        }

        [Fact]
        public void Can_get_timeout_when_asking_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            Assert.Throws<AggregateException>(() => { actor.Ask<string>("timeout", TimeSpan.FromSeconds(3)).Wait(); });
        }

        [Fact]
        public void Can_cancel_when_asking_actor()
        {            
            var actor = Sys.ActorOf<SomeActor>();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Assert.Throws<AggregateException>(() => { actor.Ask<string>("timeout", Timeout.InfiniteTimeSpan, cts.Token).Wait(); });
            Assert.True(cts.IsCancellationRequested);
        }
        [Fact]
        public void Cancelled_ask_with_null_timeout_should_remove_temp_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            Assert.Throws<AggregateException>(() => { actor.Ask<string>("cancel", cts.Token).Wait(); });
            Assert.True(cts.IsCancellationRequested);
            Are_Temp_Actors_Removed(actor);
        }
        [Fact]
        public void Cancelled_ask_with_timeout_should_remove_temp_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            Assert.Throws<AggregateException>(() => { actor.Ask<string>("cancel", TimeSpan.FromSeconds(30), cts.Token).Wait(); });
            Assert.True(cts.IsCancellationRequested);
            Are_Temp_Actors_Removed(actor);
        }
        private void Are_Temp_Actors_Removed(IActorRef actor)
        {
            var actorCell = actor as ActorRefWithCell;
            Assert.True(actorCell != null, "Test method only valid with ActorRefWithCell actors.");
            // ReSharper disable once PossibleNullReferenceException
            var container = actorCell.Provider.TempContainer as VirtualPathContainer;

            AwaitAssert(() =>
            {
                var childCounter = 0;
                // ReSharper disable once PossibleNullReferenceException
                container.ForEachChild(x => childCounter++);
                Assert.True(childCounter == 0, "Temp actors not all removed.");
            });
            
        }

        /// <summary>
        /// Tests to ensure that if we wait on the result of an Ask inside an actor's receive loop
        /// that we don't deadlock
        /// </summary>
        [Fact]
        public void Can_Ask_actor_inside_receive_loop()
        {
            var replyActor = Sys.ActorOf<ReplyActor>();
            var waitActor = Sys.ActorOf(Props.Create(() => new WaitActor(replyActor, TestActor)));
            waitActor.Tell("ask");
            ExpectMsg("bar");
        }
    }
}

