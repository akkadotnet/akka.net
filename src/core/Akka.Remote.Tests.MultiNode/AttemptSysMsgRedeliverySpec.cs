//-----------------------------------------------------------------------
// <copyright file="AttemptSysMsgRedeliverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }
        public RoleName Third { get; private set; }

        public class Echo : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }
    }

    public class AttemptSysMsgRedeliveryMultiNetSpec1 : AttemptSysMsgRedeliverySpec
    {
    }

    public class AttemptSysMsgRedeliveryMultiNetSpec2 : AttemptSysMsgRedeliverySpec
    {
    }

    public class AttemptSysMsgRedeliveryMultiNetSpec3 : AttemptSysMsgRedeliverySpec
    {
    }

    public class AttemptSysMsgRedeliverySpec : MultiNodeSpec
    {
        private readonly AttemptSysMsgRedeliveryMultiNetSpec _config;

        public AttemptSysMsgRedeliverySpec() : this(new AttemptSysMsgRedeliveryMultiNetSpec())
        {
        }

        public AttemptSysMsgRedeliverySpec(AttemptSysMsgRedeliveryMultiNetSpec config) : base(config)
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