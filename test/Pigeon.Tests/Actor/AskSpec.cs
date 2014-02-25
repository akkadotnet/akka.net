using Microsoft.VisualStudio.TestTools.UnitTesting;
using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Tests.Actor
{
    [TestClass]
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

        [TestMethod]
        public void CanAskActor()
        {
            var actor = sys.ActorOf<SomeActor>();
            actor.Ask<string>("answer").Result.ShouldBe("answer");
        }

        [TestMethod]
        public void CanAskActorWithTimer()
        {
            var actor = sys.ActorOf<SomeActor>();
            actor.Ask<string>("answer",TimeSpan.FromSeconds(10)).Result.ShouldBe("answer");
        }

        [TestMethod]
        [ExpectedException(typeof(AggregateException))]
        public void CanGetTimeoutWhenAskingActor()
        {
            var actor = sys.ActorOf<SomeActor>();
            actor.Ask<string>("timeout", TimeSpan.FromSeconds(3)).Wait();
        }
    }
}
