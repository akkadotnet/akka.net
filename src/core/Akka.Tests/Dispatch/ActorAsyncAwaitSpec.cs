using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
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
                await Task.Yield();
                await Task.Delay(TimeSpan.FromSeconds(1));
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
                var res = await other.Ask("start");
                //bug, we get the sender of the previous ask, the one in AsyncAwaitActor
                Sender.Tell(res);
            });
        }
    }

    public class ActorAsyncAwaitSpec : AkkaSpec
    {
        [Fact]
        public async Task Actors_should_be_able_to_async_await_in_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>());
            var task = actor.Ask<string>("start", TimeSpan.FromSeconds(55));
            actor.Tell(123, ActorRef.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public async Task Actors_should_be_able_to_async_await_ask_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>());
            var asker = Sys.ActorOf(Props.Create(() => new Asker(actor)));
            var task = asker.Ask<string>("start", TimeSpan.FromSeconds(55));
            actor.Tell(123, ActorRef.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }
    }
}
