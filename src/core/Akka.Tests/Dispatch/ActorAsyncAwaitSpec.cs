using System;
using System.Collections.Generic;
using System.Linq;
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
            var myContext = Context;
            Receive<string>(async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                var tId = System.Threading.Thread.CurrentThread.GetHashCode();
                var currentContext = InternalCurrentActorCellKeeper.Current;
                if (currentContext != myContext)
                {
                    throw new Exception("Actor concurrency constraint is broken");
                }
                Sender.Tell("done");
            });
        }
    }

    public class ActorAsyncAwaitSpec : AkkaSpec
    {
        [Fact]
        public async Task Actors_should_be_able_to_async_await_in_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create<AsyncAwaitActor>());
            var result = await actor.Ask("start", TimeSpan.FromSeconds(5));
            
        }
    }
}
