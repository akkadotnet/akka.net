//-----------------------------------------------------------------------
// <copyright file="AttemptSysMsgRedeliverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class AttemptSysMsgRedeliverySpecConfig : MultiNodeConfig
    {
        internal class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(m => Sender.Tell(m));
            }
        }

        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public AttemptSysMsgRedeliverySpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class AttemptSysMsgRedeliverySpec : MultiNodeClusterSpec
    {
        private readonly AttemptSysMsgRedeliverySpecConfig _config;

        public AttemptSysMsgRedeliverySpec() : this(new AttemptSysMsgRedeliverySpecConfig())
        {
        }

        protected AttemptSysMsgRedeliverySpec(AttemptSysMsgRedeliverySpecConfig config) : base(config, typeof(AttemptSysMsgRedeliverySpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void AttemptSysMsgRedeliverySpecs()
        {
            AttemptSysMsgRedelivery_must_reach_initial_convergence();
            AttemptSysMsgRedelivery_must_redeliver_system_message_after_inactivity();
        }

        private void AttemptSysMsgRedelivery_must_reach_initial_convergence()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);
            EnterBarrier("after-1");
        }

        private void AttemptSysMsgRedelivery_must_redeliver_system_message_after_inactivity()
        {
            Sys.ActorOf(Props.Create<AttemptSysMsgRedeliverySpecConfig.Echo>(), "echo");
            EnterBarrier("echo-started");

            Sys.ActorSelection(Node(_config.First) / "user" / "echo").Tell(new Identify(null));
            var firstRef = ExpectMsg<ActorIdentity>().Subject;
            Sys.ActorSelection(Node(_config.First) / "user" / "echo").Tell(new Identify(null));
            var secondRef = ExpectMsg<ActorIdentity>().Subject;
            EnterBarrier("refs-retrieved");

            RunOn(() =>
            {
                TestConductor.Blackhole(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
            }, _config.First);
            EnterBarrier("blackhole");

            RunOn(() =>
            {
                Watch(secondRef);
            }, _config.First, _config.Third);

            RunOn(() =>
            {
                Watch(firstRef);
            }, _config.Second);
            EnterBarrier("watch-established");

            RunOn(() =>
            {
                TestConductor.PassThrough(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
            }, _config.First);
            EnterBarrier("pass-through");

            Sys.ActorSelection("/user/echo").Tell(PoisonPill.Instance);

            RunOn(() =>
            {
                ExpectTerminated(secondRef, 10.Seconds());
            }, _config.First, _config.Third);

            RunOn(() =>
            {
                ExpectTerminated(firstRef, 10.Seconds());
            }, _config.Second);

            EnterBarrier("done");
        }
    }
}
