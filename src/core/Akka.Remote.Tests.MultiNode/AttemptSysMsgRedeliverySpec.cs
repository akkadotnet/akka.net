//-----------------------------------------------------------------------
// <copyright file="AttemptSysMsgRedeliverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;

namespace Akka.Remote.Tests.MultiNode
{
    public class AttemptSysMsgRedeliveryMultiNetSpec : MultiNodeConfig
    {
        public AttemptSysMsgRedeliveryMultiNetSpec()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(true);

            TestTransport = true;
        }

        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public class Echo : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }
    }

    public class AttemptSysMsgRedeliverySpec : MultiNodeSpec
    {
        private readonly AttemptSysMsgRedeliveryMultiNetSpec _config;

        public AttemptSysMsgRedeliverySpec() : this(new AttemptSysMsgRedeliveryMultiNetSpec())
        {
        }

        protected AttemptSysMsgRedeliverySpec(AttemptSysMsgRedeliveryMultiNetSpec config) : base(config, typeof(AttemptSysMsgRedeliverySpec))
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        [MultiNodeFact()]
        public void AttemptSysMsgRedelivery()
        {
            RedeliverSystemMessageAfterInactivity();
        }

        public void RedeliverSystemMessageAfterInactivity()
        {
            var echo = ActorOf<AttemptSysMsgRedeliveryMultiNetSpec.Echo>("echo");

            EnterBarrier("echo-started");

            Sys.ActorSelection(Node(_config.First)/"user"/"echo").Tell(new Identify(null));
            var firstRef = ExpectMsg<ActorIdentity>().Subject;

            Sys.ActorSelection(Node(_config.Second)/"user"/"echo").Tell(new Identify(null));
            var secondRef = ExpectMsg<ActorIdentity>().Subject;

            EnterBarrier("refs-retrieved");

            RunOn(() =>
                TestConductor.Blackhole(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both)
                             .Wait(),
                _config.First);

            EnterBarrier("blackhole");

            RunOn(() => Watch(secondRef),
                _config.First, _config.Third);

            RunOn(() => Watch(firstRef),
                _config.Second);

            EnterBarrier("watch-established");

            RunOn(() =>
                TestConductor.PassThrough(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both)
                             .Wait(),
                _config.First);

            EnterBarrier("pass-through");

            Sys.ActorSelection("/user/echo").Tell(PoisonPill.Instance);

            RunOn(() => ExpectTerminated(secondRef, TimeSpan.FromSeconds(10)),
                _config.First, _config.Third);

            RunOn(() => ExpectTerminated(firstRef, TimeSpan.FromSeconds(10)),
                _config.Second);

            EnterBarrier("done");
        }
    }
}
