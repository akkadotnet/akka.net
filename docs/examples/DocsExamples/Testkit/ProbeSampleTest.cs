﻿using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Xunit;

namespace DocsExamples.Testkit
{
    public class ProbeSampleTest : TestKit
    {
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
            var props = Props.Create<Forwarder>(new Forwarder(probe));
            var forwarder = Sys.ActorOf(props, "forwarder");

            //verify correct forwarding
            forwarder.Tell(43, TestActor);
            probe.ExpectMsg(42);
            Assert.Equal(TestActor, probe.LastSender);
        }

        [Fact]
        public void MultipleProbes()
        {
            var worker = CreateTestProbe("worker");
            var aggregator = CreateTestProbe("aggregator");

            Assert.True(worker.Ref.Path.Name.StartsWith("worker"));
            Assert.True(aggregator.Ref.Path.Name.StartsWith("aggregator"));
        }

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

    }
}