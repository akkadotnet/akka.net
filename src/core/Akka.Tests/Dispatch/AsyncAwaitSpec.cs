using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Dispatch
{
    public class SuspendActor : ReceiveActor
    {
        public SuspendActor()
        {
            var state = 0;
            Receive<string>(s => s == "change", _ =>
            {
                state = 1;
            });
            Receive<string>(AsyncBehavior.Suspend, async _ =>
            {
                Self.Tell("change");
                await Task.Delay(TimeSpan.FromSeconds(1));
                //we expect that state should not have changed due to an incoming message
                Sender.Tell(state);
            });
        }
    }
    public class ReentrantActor : ReceiveActor
    {
        public ReentrantActor()
        {
            var state = 0;
            Receive<string>(s => s == "change", _ => state = 1);
            Receive<string>(AsyncBehavior.Reentrant, async _ =>
            {
                Self.Tell("change");
                await Task.Delay(TimeSpan.FromSeconds(1));
                //we expect that state should have changed due to an incoming message
                Sender.Tell(state);
            });
        }
    }
    public class AsyncAwaitActor : ReceiveActor
    {
        public AsyncAwaitActor()
        {
            Receive<string>(async _ =>
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

    public class UntypedAsyncAwaitActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            if (message is string)
            {
                RunTask(AsyncBehavior.Suspend,  async () =>
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
            Receive<string>(async _ =>
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
                RunTask(AsyncBehavior.Suspend, async () =>
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
            Receive<string>(async _ =>
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
                RunTask(AsyncBehavior.Suspend, () =>
                {
                    Task.Delay(TimeSpan.FromSeconds(1))
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
                RunTask(AsyncBehavior.Suspend, () =>
                {
                    Task.Delay(TimeSpan.FromSeconds(1))
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
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>().WithDispatcher("akka.actor.task-dispatcher"),"Worker");
            var asker =Sys.ActorOf(Props.Create(() => new BlockingAsker(actor)).WithDispatcher("akka.actor.task-dispatcher"),"Asker");
            var task = asker.Ask("start", TimeSpan.FromSeconds(5));
            actor.Tell(123, ActorRefs.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact(Skip = "Maybe not possible to solve")]
        public async Task Actors_should_be_able_to_block_ask_self_message_loop()
        {
            var asker = Sys.ActorOf(Props.Create(() => new BlockingAskSelf()),"Asker");
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
        public async Task Actors_should_be_able_to_reenter()
        {
            var asker = Sys.ActorOf(Props.Create(() => new ReentrantActor()));
            var res = await asker.Ask<int>("start",TimeSpan.FromSeconds(5));
            res.ShouldBe(1);
        }

        [Fact]
        public async Task Actors_should_be_able_to_suspend_reentrancy()
        {
            var asker = Sys.ActorOf(Props.Create(() => new SuspendActor()));
            var res = await asker.Ask<int>("start", TimeSpan.FromSeconds(555));
            res.ShouldBe(0);
        }
    }
}