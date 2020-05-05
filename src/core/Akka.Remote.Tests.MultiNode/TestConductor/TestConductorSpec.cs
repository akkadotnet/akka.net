//-----------------------------------------------------------------------
// <copyright file="TestConductorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;

namespace Akka.Remote.Tests.MultiNode.TestConductor
{
    public class TestConductorSpecConfig : MultiNodeConfig
    {
        public RoleName Master { get; private set; }

        public RoleName Slave { get; private set; }

        public TestConductorSpecConfig()
        {
            Master = Role("master");
            Slave = Role("slave");
            CommonConfig = DebugConfig(true);
            TestTransport = true;
        }
    }

    public class TestConductorSpec : MultiNodeSpec
    {
        private TestConductorSpecConfig _config;

        public TestConductorSpec() : this(new TestConductorSpecConfig()) { }

        protected TestConductorSpec(TestConductorSpecConfig config) : base(config, typeof(TestConductorSpec))
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory
        {
            get
            {
                return 2;
            } 
        }

        private IActorRef _echo;

        protected IActorRef GetEchoActorRef()
        {
            if (_echo == null)
            {
                Sys.ActorSelection(Node(_config.Master).Root / "user" / "echo").Tell(new Identify(null));
                _echo = ExpectMsg<ActorIdentity>().Subject;
            }
            return _echo;
        }

        [MultiNodeFact]
        public void ATestConductorMust()
        {
            Enter_a_Barrier();
            Support_Throttling_of_Network_Connections();
        }

        public void Enter_a_Barrier()
        {
            RunOn(() =>
            {
                Sys.ActorOf(c => c.ReceiveAny((m, ctx) =>
                {
                    TestActor.Tell(m);
                    ctx.Sender.Tell(m);
                }), "echo");
            }, _config.Master);

            EnterBarrier("name");
        }

        public void Support_Throttling_of_Network_Connections()
        {
            RunOn(() =>
            {
                // start remote network connection so that it can be throttled
                GetEchoActorRef().Tell("start");
            }, _config.Slave);

            ExpectMsg("start");

            RunOn(() =>
            {
                TestConductor.Throttle(_config.Slave, _config.Master, ThrottleTransportAdapter.Direction.Send, 0.01f).Wait();
            }, _config.Master);

            EnterBarrier("throttled_send");

            RunOn(() =>
            {
                foreach(var i in Enumerable.Range(0, 10))
                {
                    GetEchoActorRef().Tell(i);
                }
            }, _config.Slave);

            // fudged the value to 0.5,since messages are a different size in Akka.NET
            Within(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2), () =>
            {
                ExpectMsg(0, TimeSpan.FromMilliseconds(500));
                ReceiveN(9).ShouldOnlyContainInOrder(Enumerable.Range(1,9).Cast<object>().ToArray());
            });

            EnterBarrier("throttled_send2");
            RunOn(() =>
            {
                TestConductor.Throttle(_config.Slave, _config.Master, ThrottleTransportAdapter.Direction.Send, -1).Wait();
                TestConductor.Throttle(_config.Slave, _config.Master, ThrottleTransportAdapter.Direction.Receive, 0.01F).Wait();
            }, _config.Master);

            EnterBarrier("throttled_recv");

            RunOn(() =>
            {
                foreach (var i in Enumerable.Range(10, 10))
                {
                    GetEchoActorRef().Tell(i);
                }
            }, _config.Slave);

            var minMax = IsNode(_config.Master)
                ? (TimeSpan.Zero, TimeSpan.FromMilliseconds(500))
                : (TimeSpan.FromSeconds(0.3), TimeSpan.FromSeconds(3));

            Within(minMax.Item1, minMax.Item2, () =>
            {
                ExpectMsg(10, TimeSpan.FromMilliseconds(500));
                ReceiveN(9).ShouldOnlyContainInOrder(Enumerable.Range(11, 9).Cast<object>().ToArray());
            });

            EnterBarrier("throttled_recv2");

            RunOn(() =>
            {
                TestConductor.Throttle(_config.Slave, _config.Master, ThrottleTransportAdapter.Direction.Receive, -1).Wait();
            }, _config.Master);

            EnterBarrier("after");
        }
    }
}
