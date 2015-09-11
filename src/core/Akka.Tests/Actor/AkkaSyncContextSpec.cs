using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class AkkaSyncContextSpec : AkkaSpec
    {
        private readonly IActorRef _actor;

        [Fact]
        public void Continuation_is_in_the_same_thread_as_initiation()
        {
            var sys = ActorSystem.Create("sys");
            var actor = sys.ActorOf(Props.Create<MyActor>());

            actor.Tell("start");
            Thread.Sleep(200);
            actor.Tell("start2");

            ExpectMsg("finish");
            ExpectMsg("finish2");
        }

        public class MyActor : UntypedActor
        {
            ILoggingAdapter Log;

            public MyActor()
            {
                Log = Logging.GetLogger(Context.System, GetType());
            }

            async protected override void OnReceive(object message)
            {
                switch(message as string)
                {
                    case "start":
                        System.Diagnostics.Debug.WriteLine("{0:O} Before async thread: {1}", DateTime.Now, Thread.CurrentThread.ManagedThreadId);
                        await Task.Delay(100);
                        System.Diagnostics.Debug.WriteLine("{0:O} After async thread: {1}", DateTime.Now, Thread.CurrentThread.ManagedThreadId);
                        Thread.Sleep(300);
                        System.Diagnostics.Debug.WriteLine("{0:O} After sleep thread: {1}", DateTime.Now, Thread.CurrentThread.ManagedThreadId);
                        Sender.Tell("finish");
                        break;
                    case "start2":
                        System.Diagnostics.Debug.WriteLine("{0:O} Got start2 in thread {1}", DateTime.Now, Thread.CurrentThread.ManagedThreadId);
                        Sender.Tell("finish2");
                        break;
                    default:
                        Unhandled(message);
                        break;
                }
            }
        }
    }
}
