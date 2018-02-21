//-----------------------------------------------------------------------
// <copyright file="AskSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.TestKit;
using Xunit;
using Akka.Actor;
using System;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Akka.Tests.Actor
{
    public class AskSpec : AkkaSpec
    {
        public AskSpec()
            : base(@"akka.actor.ask-timeout = 3000ms")
        { }

        public class ExpectedTestException : Exception
        {
            public ExpectedTestException(string message) : base(message)
            {
            }
        }
        
        public class SomeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case "timeout":
                        Thread.Sleep(5000);
                        break;
                    case "answer":
                        Sender.Tell("answer");
                        break;
                    case "throw":
                        Task.Run(ThrowNested).PipeTo(Sender); 
                        break;
                    case "return-cancelled":
                        var token = new CancellationToken(canceled: true);
                        new Task(() => { }, token).PipeTo(Sender);
                        break;
                }
            }
            
            internal async Task ThrowNested()
            {
                await Task.Delay(1);
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

            private readonly IActorRef _replyActor;

            private readonly IActorRef _testActor;

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
        public async Task Can_Ask_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            var res = await actor.Ask<string>("answer");
            res.ShouldBe("answer");
        }

        [Fact]
        public async Task Can_Ask_actor_with_timeout()
        {
            var actor = Sys.ActorOf<SomeActor>();
            var res = await actor.Ask<string>("answer", TimeSpan.FromSeconds(10));
            res.ShouldBe("answer");
        }

        [Fact]
        public async Task Can_get_timeout_when_asking_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            await Assert.ThrowsAsync<AskTimeoutException>(async () => await actor.Ask<string>("timeout", TimeSpan.FromSeconds(3)));
        }

        [Fact]
        public async Task Can_cancel_when_asking_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                await Assert.ThrowsAsync<TaskCanceledException>(async () => await actor.Ask<string>("timeout", Timeout.InfiniteTimeSpan, cts.Token));
            }
        }

        [Fact]
        public async Task Ask_should_honor_config_specified_timeout()
        {
            var actor = Sys.ActorOf<SomeActor>();
            try
            {
                await actor.Ask<string>("timeout");
                Assert.True(false, "the ask should have timed out with default timeout");
            }
            catch (AskTimeoutException e)
            {
                Assert.Equal("Timeout after 00:00:03 seconds", e.Message);
            }
        }

        [Fact]
        public async Task Cancelled_ask_with_null_timeout_should_remove_temp_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();

            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                await Assert.ThrowsAsync<TaskCanceledException>(async () => await actor.Ask<string>("cancel", cts.Token));
            }

            Are_Temp_Actors_Removed(actor);
        }

        [Fact]
        public async Task Cancelled_ask_with_timeout_should_remove_temp_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();
            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                await Assert.ThrowsAsync<TaskCanceledException>(async () => await actor.Ask<string>("cancel", TimeSpan.FromSeconds(30), cts.Token));
            }

            Are_Temp_Actors_Removed(actor);
        }

        [Fact]
        public async Task AskTimeout_with_default_timeout_should_remove_temp_actor()
        {
            var actor = Sys.ActorOf<SomeActor>();

            await Assert.ThrowsAsync<AskTimeoutException>(async () => await actor.Ask<string>("timeout"));

            Are_Temp_Actors_Removed(actor);
        }

        [Fact]
        public async Task ShouldFailWhenAskExpectsWrongType()
        {
            var actor = Sys.ActorOf<SomeActor>();

            // expect int, but in fact string
            await Assert.ThrowsAsync<InvalidCastException>(async () => await actor.Ask<int>("answer"));
        }

        [Fact]
        public void AskDoesNotDeadlockWhenWaitForResultInGuiApplication()
        {
            AsyncContext.Run(() =>
            {
                var actor = Sys.ActorOf<SomeActor>();
                var res = actor.Ask<string>("answer").Result; // blocking on purpose
                res.ShouldBe("answer");
            });
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
        public async Task Generic_Ask_when_Failure_is_returned_should_throw_error_payload_and_preserve_stack_trace()
        {
            var actor = Sys.ActorOf<SomeActor>();

            try
            {
                await actor.Ask<string>("throw", timeout: TimeSpan.FromSeconds(3));
            }
            catch (ExpectedTestException exception)
            {
                exception.Message.ShouldBe("BOOM!");
                exception.StackTrace.Contains(nameof(SomeActor.ThrowNested)).ShouldBeTrue("stack trace should be preserved");
            }
        }
        
        [Fact]
        public async Task Generic_Ask_when_Failure_is_and_Failure_was_expected_should_not_throw()
        {
            var actor = Sys.ActorOf<SomeActor>();
            var result = await actor.Ask<Status.Failure>("throw", timeout: TimeSpan.FromSeconds(3));
            var exception = ((AggregateException)result.Cause).Flatten().InnerException;
            exception.GetType().ShouldBe(typeof(ExpectedTestException));
            exception.Message.ShouldBe("BOOM!");
        }
        
        [Fact]
        public async Task Generic_Ask_when_asker_task_was_cancelled_and_should_fail()
        {
            var actor = Sys.ActorOf<SomeActor>();
            await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            {
                var result = await actor.Ask<string>("return-cancelled", timeout: TimeSpan.FromSeconds(3));
            });
        }
        
        [Fact]
        public async Task Generic_Ask_when_asker_task_was_cancelled_and_Failure_was_expected_should_return_a_Failure()
        {
            var actor = Sys.ActorOf<SomeActor>();
            var result = await actor.Ask<Status.Failure>("return-cancelled", timeout: TimeSpan.FromSeconds(3));
            result.Cause.ShouldBe(null);
        }
    }
}

