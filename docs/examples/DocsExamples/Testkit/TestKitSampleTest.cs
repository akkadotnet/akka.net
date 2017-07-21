using System;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;

namespace DocsExamples.Testkit
{

    public class SomeActor : ReceiveActor
    {
        IActorRef target = null;

        public SomeActor()
        {
            Receive<string>(s => s.Equals("hello"), (message) => {
                Sender.Tell("world", Self);
                if (target != null)
                    target.Forward(message);
            });

            Receive<IActorRef>(actorRef => {
                target = actorRef;
                Sender.Tell("done");
            });
        }
    }

    public class TestKitSampleTest : TestKit
    {

        [Fact]
        public void Test()
        {
            var subject = this.Sys.ActorOf<SomeActor>();

            var probe = this.CreateTestProbe();

            //inject the probe by passing it to the test subject
            //like a real resource would be passing in production
            subject.Tell(probe.Ref, this.TestActor);

            ExpectMsg("done", TimeSpan.FromSeconds(1));

            // the action needs to finish within 3 seconds
            Within(TimeSpan.FromSeconds(3), () => {
                subject.Tell("hello", this.TestActor);

                // This is a demo: would normally use expectMsgEquals().
                // Wait time is bounded by 3-second deadline above.
                AwaitCondition(() => probe.HasMessages);

                // response must have been enqueued to us before probe
                ExpectMsg("world", TimeSpan.FromSeconds(0));
                // check that the probe we injected earlier got the msg
                probe.ExpectMsg("hello", TimeSpan.FromSeconds(0));

                Assert.Equal(TestActor, probe.Sender);
                
                // Will wait for the rest of the 3 seconds
                ExpectNoMsg();
            });
        }
    }
}
