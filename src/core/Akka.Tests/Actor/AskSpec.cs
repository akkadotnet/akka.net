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
using System.Threading.Tasks;

namespace Akka.Tests.Actor
{
    public class ExpectedTestException : Exception
    {
        public ExpectedTestException(string message) : base(message)
        {
        }
    }
    
    public class AskSpec : AkkaSpec
    {
        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case "timeout": Thread.Sleep(5000); break;
                    case "answer": Sender.Tell("answer"); break;
                    case "throw": Task.Run(ThrowNested).PipeTo(Sender); break;
                }
            }

            internal async Task ThrowNested()
            {
                throw new ExpectedTestException("BOOM!");
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

        [Fact]
        public void Generic_Ask_when_Failure_is_returned_should_throw_error_payload_and_preserve_stack_trace()
        {
            var actor = Sys.ActorOf<SomeActor>();
            var aggregateException = Assert.Throws<AggregateException>(() =>
            {
                var result = actor.Ask<string>("throw", timeout: TimeSpan.FromSeconds(3)).Result;
            });
            var exception = aggregateException.Flatten().InnerException;
            exception.GetType().ShouldBe(typeof(ExpectedTestException));
            exception.Message.ShouldBe("BOOM!");
            exception.StackTrace.Contains(nameof(SomeActor.ThrowNested)).ShouldBeTrue("stack trace should be preserved");
        }

        [Fact]
        public void Generic_Ask_when_Failure_is_and_Failure_was_expected_should_not_throw()
        {
            var actor = Sys.ActorOf<SomeActor>();
            var result = actor.Ask<Status.Failure>("throw", timeout: TimeSpan.FromSeconds(3)).Result;
            var exception = ((AggregateException)result.Cause).Flatten().InnerException;
            exception.GetType().ShouldBe(typeof(ExpectedTestException));
            exception.Message.ShouldBe("BOOM!");
        }
    }
}

