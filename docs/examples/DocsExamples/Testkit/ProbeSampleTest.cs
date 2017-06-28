// <copyright file="ProbeSampleTest.cs" company="Copaco B.V.">
//        Copyright (c) 2015 - 2017 All Right Reserved
//        Author: Arjen Smits
// </copyright>

using Akka.Actor;
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


    }
}