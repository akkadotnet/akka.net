//-----------------------------------------------------------------------
// <copyright file="ProbeSampleTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Xunit;

namespace DocsExamples.Testkit
{
    public class ProbeSampleTest : TestKit
    {
#region ProbeSample_0
        public class Forwarder : ReceiveActor
        {
            private IActorRef target;

            public Forwarder(IActorRef target)
            {
                this.target = target;

                ReceiveAny(target.Forward);
            }
        }

        [Fact]
        public void Test()
        {
            //create a test probe
            var probe = CreateTestProbe();
            
            //create a forwarder, injecting the probo's testActor
            var props = Props.Create(() => new Forwarder(probe));
            var forwarder = Sys.ActorOf(props, "forwarder");

            //verify correct forwarding
            forwarder.Tell(43, TestActor);
            probe.ExpectMsg(43);
            Assert.Equal(TestActor, probe.LastSender);
        }
#endregion ProbeSample_0

#region MultipleProbeSample_0
        [Fact]
        public void MultipleProbes()
        {
            var worker = CreateTestProbe("worker");
            var aggregator = CreateTestProbe("aggregator");

            Assert.StartsWith("worker", worker.Ref.Path.Name);
            Assert.StartsWith("aggregator", aggregator.Ref.Path.Name);
        }
#endregion MultipleProbeSample_0

#region ReplyingToProbeMessages_0
        [Fact]
        public void ReplyingToProbeMessages()
        {

            var probe = CreateTestProbe();
            probe.Tell("hello");
            probe.ExpectMsg("hello");
            probe.Reply("world");
            ExpectMsg("world");
            Assert.Equal(probe.Ref, LastSender);
        }
#endregion ReplyingToProbeMessages_0

#region ForwardingProbeMessages_0
        [Fact]
        public void ForwardingProbeMessages()
        {
            var probe = CreateTestProbe();
            probe.Tell("hello");
            probe.ExpectMsg("hello");
            probe.Forward(TestActor);
            ExpectMsg("hello");
            Assert.Equal(TestActor, LastSender);
        }
#endregion ForwardingProbeMessages_0

#region ProbeAutopilot_0
        [Fact]
        public void ProbeAutopilot()
        {
            var probe = CreateTestProbe();
            probe.SetAutoPilot(new DelegateAutoPilot((sender, message) =>
            {
                sender.Tell(message, ActorRefs.NoSender);
                return AutoPilot.NoAutoPilot;
            }));

            //first one is replied to directly
            probe.Tell("Hello");
            ExpectMsg("Hello");
            //... but then the auto-pilot switched itself off
            probe.Tell("world");
            ExpectNoMsg();
        }
#endregion ProbeAutopilot_0

    }
}
