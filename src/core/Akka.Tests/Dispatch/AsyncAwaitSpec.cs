using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Dispatch
{
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
                Assert.Same(sender,Sender);
                Assert.Same(self, Self);
                Sender.Tell("done");
            });
        }
    }

    public class Asker : ReceiveActor
    {
        public Asker(ActorRef other)
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

    public class BlockingAsker : ReceiveActor
    {
        public BlockingAsker(ActorRef other)
        {
            Receive<string>( _ =>
            {
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

    public class ActorAsyncAwaitSpec : AkkaSpec
    {
        [Fact]
        public async Task Actors_should_be_able_to_async_await_in_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>().WithDispatcher("akka.actor.task-dispatcher"));
            var task = actor.Ask<string>("start", TimeSpan.FromSeconds(55));
            actor.Tell(123, ActorRef.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public async Task Actors_should_be_able_to_async_await_ask_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>().WithDispatcher("akka.actor.task-dispatcher"),
                "Worker");
            var asker = Sys.ActorOf(Props.Create(() => new Asker(actor)).WithDispatcher("akka.actor.task-dispatcher"),
                "Asker");
            var task = asker.Ask("start", TimeSpan.FromSeconds(55));
            actor.Tell(123, ActorRef.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public async Task Actors_should_be_able_to_block_ask_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>().WithDispatcher("akka.actor.task-dispatcher"),
                "Worker");
            var asker = Sys.ActorOf(Props.Create(() => new BlockingAsker(actor)).WithDispatcher("akka.actor.task-dispatcher"),
                "Asker");
            var task = asker.Ask("start", TimeSpan.FromSeconds(55));
            actor.Tell(123, ActorRef.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact(Skip = "Maybe not possible to solve")]
        public async Task Actors_should_be_able_to_block_ask_self_message_loop()
        {
            var asker = Sys.ActorOf(Props.Create(() => new BlockingAskSelf()).WithDispatcher("akka.actor.task-dispatcher"),
                "Asker");
            var task = asker.Ask("start", TimeSpan.FromSeconds(55));
            var res = await task;
            Assert.Equal("done", res);
        }
    }
}