//-----------------------------------------------------------------------
// <copyright file="PiercingShouldKeepQuarantineSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;

namespace Akka.Remote.Tests.MultiNode
{
    public class PiercingShouldKeepQuarantineMultiNodeConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public PiercingShouldKeepQuarantineMultiNodeConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(true)
                .WithFallback(ConfigurationFactory.ParseString(@"
    akka.loglevel = INFO
    akka.remote.log-remote-lifecycle-events = INFO
    akka.remote.retry-gate-closed-for = 10s
                "));
        }
    }

    public class PiercingShouldKeepQuarantineSpec : MultiNodeSpec
    {
        private readonly PiercingShouldKeepQuarantineMultiNodeConfig _config;

        public PiercingShouldKeepQuarantineSpec() : this(new PiercingShouldKeepQuarantineMultiNodeConfig())
        {
        }

        protected PiercingShouldKeepQuarantineSpec(PiercingShouldKeepQuarantineMultiNodeConfig config) : base(config, typeof(PiercingShouldKeepQuarantineSpec))
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        public class Subject : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message.Equals("getuid"))
                    Sender.Tell(AddressUidExtension.Uid(Context.System));
            }
        }

        [MultiNodeFact]
        public void PiercingShouldKeepQuarantineSpecs()
        {
            While_probing_through_the_quarantine_remoting_must_not_lose_existing_quarantine_marker();
        }

        public void While_probing_through_the_quarantine_remoting_must_not_lose_existing_quarantine_marker()
        {
            RunOn(() =>
            {
                EnterBarrier("actors-started");

                // Communicate with second system
                Sys.ActorSelection(Node(_config.Second) / "user" / "subject").Tell("getuid");
                var uid = ExpectMsg<int>(TimeSpan.FromSeconds(10));
                EnterBarrier("actor-identified");

                // Manually Quarantine the other system
                RARP.For(Sys).Provider.Transport.Quarantine(Node(_config.Second).Address, uid);

                // Quarantining is not immediate
                Thread.Sleep(1000);

                // Quarantine is up - Should not be able to communicate with remote system any more
                for (var i = 1; i <= 4; i++)
                {
                    Sys.ActorSelection(Node(_config.Second) / "user" / "subject").Tell("getuid");

                    ExpectNoMsg(TimeSpan.FromSeconds(2));
                }

                EnterBarrier("quarantine-intact");

            }, _config.First);

            RunOn(() =>
            {
                Sys.ActorOf<Subject>("subject");
                EnterBarrier("actors-started");
                EnterBarrier("actor-identified");
                EnterBarrier("quarantine-intact");
            }, _config.Second);
        }
    }
}
