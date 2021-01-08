//-----------------------------------------------------------------------
// <copyright file="AsyncAwaitSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Dispatch
{
    class ReceiveTimeoutAsyncActor : ReceiveActor
    {
        private IActorRef _replyTo;
        public ReceiveTimeoutAsyncActor()
        {
            Receive<ReceiveTimeout>(t =>
            {
                _replyTo.Tell("GotIt");
            });
            ReceiveAsync<string>(async s =>
            {
                _replyTo = Sender;

                await Task.Delay(TimeSpan.FromMilliseconds(100));
                SetReceiveTimeout(TimeSpan.FromMilliseconds(100));
            });
        }
    }
    class AsyncActor : ReceiveActor
    {
        public AsyncActor()
        {
            ReceiveAsync<string>(async s =>
            {
                await Task.Yield();
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                if (s == "stop")
                {
                    Sender.Tell("done");
                }
            });
        }
    }

    public class SuspendActor : ReceiveActor
    {
        public SuspendActor()
        {
            var state = 0;
            Receive<string>(s => s == "change", _ =>
            {
                state = 1;
            });
            ReceiveAsync<string>(async m_ =>
            {
                Self.Tell("change");
                await Task.Delay(TimeSpan.FromSeconds(1));
                var cell = (ActorCell) Context;
                var current = cell.CurrentMessage;
                //ensure we have the correct current message here
                Assert.Same(m_, current);
                //we expect that state should not have changed due to an incoming message
                Sender.Tell(state);
            });
        }
    }

    public class AsyncAwaitActor : ReceiveActor
    {
        public AsyncAwaitActor()
        {
            ReceiveAsync<string>(async _ =>
            {
                var sender = Sender;
                var self = Self;
                await Task.Yield();
                await Task.Delay(TimeSpan.FromSeconds(1));
                Assert.Same(sender, Sender);
                Assert.Same(self, Self);
                Sender.Tell("done");
            });

            ReceiveAsync<int>(async msg =>
            {
                await Task.Yield();
                Sender.Tell("handled");
            }, i => i > 10);

            ReceiveAsync(typeof(double), async msg =>
            {
                await Task.Yield();
                Sender.Tell("handled");
            });

            ReceiveAnyAsync(async msg =>
            {
                await Task.Yield();
                Sender.Tell("receiveany");
            });
        }
    }

    public class UntypedAsyncAwaitActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            if (message is string)
            {
                RunTask(async () =>
                {
                    var sender = Sender;
                    var self = Self;
                    await Task.Yield();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    Assert.Same(sender, Sender);
                    Assert.Same(self, Self);
                    Sender.Tell("done");
                });
            }
        }
    }

    public class Asker : ReceiveActor
    {
        public Asker(IActorRef other)
        {
            ReceiveAsync<string>(async _ =>
            {
                var sender = Sender;
                var self = Self;
                var res = await other.Ask("start");
                Assert.Same(sender, Sender);
                Assert.Same(self, Self);
                Sender.Tell(res);
            });
        }
    }

    public class UntypedAsker : UntypedActor
    {
        private readonly IActorRef _other;

        public UntypedAsker(IActorRef other)
        {
            _other = other;
        }

        protected override void OnReceive(object message)
        {
            if (message is string)
            {
                RunTask(async () =>
                {
                    var sender = Sender;
                    var self = Self;
                    var res = await _other.Ask("start");
                    Assert.Same(sender, Sender);
                    Assert.Same(self, Self);
                    Sender.Tell(res);
                });
            }
        }
    }

    public class BlockingAsker : ReceiveActor
    {
        public BlockingAsker(IActorRef other)
        {
            Receive<string>(_ =>
            {
                //not async, blocking wait
                var res = other.Ask("start").Result;
                Sender.Tell(res);
            });
        }
    }

    public class BlockingAskSelf : ReceiveActor
    {
        public BlockingAskSelf()
        {
            Receive<int>(_ =>
            {
                //since this actor is blocking in the handler below, it will never
                //be able to execute this section
                Sender.Tell("done");
            });
            Receive<string>(_ =>
            {
                //ask and block
                var res = Self.Ask(123).Result;
                Sender.Tell(res);
            });
        }
    }

    public class AsyncExceptionActor : ReceiveActor
    {
        private readonly IActorRef _callback;

        public AsyncExceptionActor(IActorRef callback)
        {
            _callback = callback;
            ReceiveAsync<string>(async _ =>
            {
                await Task.Yield();
                ThrowException();
            });
        }

        protected override void PostRestart(Exception reason)
        {
            _callback.Tell("done");
            base.PostRestart(reason);
        }

        private static void ThrowException()
        {
            throw new Exception("should be handled by supervisor");
        }
    }

    public class AsyncTplActor : ReceiveActor
    {
        public AsyncTplActor()
        {
            Receive<string>(m =>
            {
                //this is also safe, all tasks complete in the actor context
                RunTask(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1))
                        .ContinueWith(t => { Sender.Tell("done"); });
                });
            });
        }
    }

    public class AsyncTplExceptionActor : ReceiveActor
    {
        private readonly IActorRef _callback;

        public AsyncTplExceptionActor(IActorRef callback)
        {
            _callback = callback;
            Receive<string>(m =>
            {
                RunTask(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1))
                   .ContinueWith(t => { throw new Exception("foo"); });
                });
            });
        }

        protected override void PostRestart(Exception reason)
        {
            _callback.Tell("done");
            base.PostRestart(reason);
        }
    }

    public class RestartMessage
    {
        public object Message { get; private set; }

        public RestartMessage(object message)
        {
            Message = message;
        }
    }

    public class ActorAsyncAwaitSpec : AkkaSpec
    {
        [Fact]
        public async Task UntypedActors_should_be_able_to_async_await_ask_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<UntypedAsyncAwaitActor>(), "Worker");
            var asker = Sys.ActorOf(Props.Create(() => new UntypedAsker(actor)), "Asker");
            var task = asker.Ask("start", TimeSpan.FromSeconds(5));
            actor.Tell(123, ActorRefs.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public async Task Actors_should_be_able_to_async_await_in_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>());
            var task = actor.Ask<string>("start", TimeSpan.FromSeconds(5));
            actor.Tell(123, ActorRefs.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public async Task Actors_should_be_able_to_async_await_ask_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>(), "Worker");
            var asker = Sys.ActorOf(Props.Create(() => new Asker(actor)), "Asker");
            var task = asker.Ask("start", TimeSpan.FromSeconds(5));
            actor.Tell(123, ActorRefs.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public async Task Actors_should_be_able_to_block_ask_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>().WithDispatcher("akka.actor.task-dispatcher"), "Worker");
            var asker = Sys.ActorOf(Props.Create(() => new BlockingAsker(actor)).WithDispatcher("akka.actor.task-dispatcher"), "Asker");
            var task = asker.Ask("start", TimeSpan.FromSeconds(5));
            actor.Tell(123, ActorRefs.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact(Skip = "Maybe not possible to solve")]
        public async Task Actors_should_be_able_to_block_ask_self_message_loop()
        {
            var asker = Sys.ActorOf(Props.Create(() => new BlockingAskSelf()), "Asker");
            var task = asker.Ask("start", TimeSpan.FromSeconds(5));
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public void Actors_should_be_able_to_supervise_async_exceptions()
        {
            var asker = Sys.ActorOf(Props.Create(() => new AsyncExceptionActor(TestActor)));
            asker.Tell("start");
            ExpectMsg("done", TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task Actors_should_be_able_to_use_ContinueWith()
        {
            var asker = Sys.ActorOf(Props.Create<AsyncTplActor>());
            var res = await asker.Ask("start", TimeSpan.FromSeconds(5));
            Assert.Equal("done", res);
        }

        [Fact]
        public void Actors_should_be_able_to_supervise_exception_ContinueWith()
        {
            var asker = Sys.ActorOf(Props.Create(() => new AsyncTplExceptionActor(TestActor)));
            asker.Tell("start");
            ExpectMsg("done", TimeSpan.FromSeconds(5));
        }


        [Fact]
        public async Task Actors_should_be_able_to_suspend_reentrancy()
        {
            var asker = Sys.ActorOf(Props.Create(() => new SuspendActor()));
            var res = await asker.Ask<int>("start", TimeSpan.FromSeconds(5));
            res.ShouldBe(0);
        }

        [Fact]
        public async Task Actor_should_be_able_to_resume_suspend()
        {
            var asker = Sys.ActorOf<AsyncActor>();

            for (var i = 0; i < 10; i++)
            {
                asker.Tell("msg #" + i);
            }

            var res = await asker.Ask<string>("stop", TimeSpan.FromSeconds(5));
            res.ShouldBe("done");
        }

        [Fact]
        public void Actor_should_be_able_to_ReceiveTimeout_after_async_operation()
        {
            var actor = Sys.ActorOf<ReceiveTimeoutAsyncActor>();

            actor.Tell("hello");
            ExpectMsg<string>(m => m == "GotIt");
        }

        public class AsyncExceptionCatcherActor : ReceiveActor
        {
            private string _lastMessage;

            public AsyncExceptionCatcherActor()
            {
                ReceiveAsync<string>(async m =>
                {
                    _lastMessage = m;
                    try
                    {
                        // Throw an exception in the ActorTaskScheduler
                        await Task.Factory.StartNew(() =>
                        {
                            throw new Exception("should not restart");
                        });
                    }
                    catch (Exception)
                    {
                    }
                });

                Receive<int>(_ => Sender.Tell(_lastMessage, Self));
            }
        }

        [Fact]
        public async Task Actor_should_not_restart_if_exception_is_catched()
        {
            var actor = Sys.ActorOf<AsyncExceptionCatcherActor>();

            actor.Tell("hello");

            var lastMessage = await actor.Ask(123);

            lastMessage.ShouldBe("hello");
        }

        public class AsyncFailingActor : ReceiveActor
        {
            public AsyncFailingActor()
            {
                ReceiveAsync<string>(async m =>
                {
                    ThrowException();
                });
            }

            protected override void PreRestart(Exception reason, object message)
            {
                Sender.Tell(new RestartMessage(message), Self);

                base.PreRestart(reason, message);
            }

            private static void ThrowException()
            {
                throw new Exception("foo");
            }
        }

        [Fact]
        public void Actor_PreRestart_should_give_the_failing_message()
        {
            var actor = Sys.ActorOf<AsyncFailingActor>();

            actor.Tell("hello");

            ExpectMsg<RestartMessage>(m => "hello".Equals(m.Message));
        }

        public class AsyncPipeToDelayActor : ReceiveActor
        {
            public AsyncPipeToDelayActor()
            {
                ReceiveAsync<string>(async msg =>
                {
                    Task.Run(() =>
                    {
                        Thread.Sleep(10);
                        return msg;
                    }).PipeTo(Sender, Self); //LogicalContext is lost?!?

                    Thread.Sleep(3000);
                });
            }
        }

        public class AsyncReentrantActor : ReceiveActor
        {
            public AsyncReentrantActor()
            {
                ReceiveAsync<string>(async msg =>
                {
                    var sender = Sender;
                    Task.Run(() =>
                    {
                        //Sleep to make sure the task is not completed when ContinueWith is called
                        Thread.Sleep(100);
                        return msg;
                    }).ContinueWith(_ => sender.Tell(msg)); // ContinueWith will schedule with the implicit ActorTaskScheduler

                    Thread.Sleep(3000);
                });
            }
        }

        [Fact]
        public void ActorTaskScheduler_reentrancy_should_not_be_possible()
        {
            var actor = Sys.ActorOf<AsyncReentrantActor>();
            actor.Tell("hello");

            ExpectNoMsg(1000);
        }

        [Fact]
        public void Actor_PipeTo_should_not_be_delayed_by_async_receive()
        {
            var actor = Sys.ActorOf<AsyncPipeToDelayActor>();

            actor.Tell("hello");
            ExpectMsg<string>(m => "hello".Equals(m), TimeSpan.FromMilliseconds(1000));
        }

        [Fact]
        public async Task Actor_receiveasync_overloads_should_work()
        {
            var actor = Sys.ActorOf<AsyncAwaitActor>();

            actor.Tell(11);
            ExpectMsg<string>(m => "handled".Equals(m), TimeSpan.FromMilliseconds(1000));

            actor.Tell(9);
            ExpectMsg<string>(m => "receiveany".Equals(m), TimeSpan.FromMilliseconds(1000));

            actor.Tell(1.0);
            ExpectMsg<string>(m => "handled".Equals(m), TimeSpan.FromMilliseconds(1000));


        }
    }
}

