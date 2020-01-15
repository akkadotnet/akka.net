//-----------------------------------------------------------------------
// <copyright file="RemoteQuarantinePiercingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteQuarantinePiercingSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

        public RemoteQuarantinePiercingSpecConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                  akka.loglevel = INFO
                  akka.remote.log-remote-lifecycle-events = INFO
                "));
        }

        public sealed class Subject : ReceiveActor
        {
            public Subject()
            {
                Receive<string>(str => str == "shutdown", c => Context.System.Terminate());
                Receive<string>(str => str == "identify", c =>
                {
                    Sender.Tell((AddressUidExtension.Uid(Context.System), Self));
                });
            }
        }
    }

    public class RemoteQuarantinePiercingSpec : MultiNodeSpec
    {
        private readonly RemoteQuarantinePiercingSpecConfig _specConfig;

        protected override int InitialParticipantsValueFactory { get; } = 2;

        public RemoteQuarantinePiercingSpec() : this(new RemoteQuarantinePiercingSpecConfig())
        {
        }

        protected RemoteQuarantinePiercingSpec(RemoteQuarantinePiercingSpecConfig specConfig) : base(specConfig, typeof(RemoteQuarantinePiercingSpec))
        {
            _specConfig = specConfig;
        }

        private (int, IActorRef) Identify(RoleName role, string actorName)
        {
            Sys.ActorSelection(Node(role) / "user" / actorName).Tell("identify");
            return ExpectMsg<(int, IActorRef)>();
        }

        [MultiNodeFact]
        public void RemoteQuarantinePiercingSpecs()
        {
            RemoteNodeShutdownAndComesBack_must_allow_piercing_through_the_quarantine_when_remote_UID_is_new();
        }

        private void RemoteNodeShutdownAndComesBack_must_allow_piercing_through_the_quarantine_when_remote_UID_is_new()
        {
            RunOn(() =>
            {
                var secondAddress = Node(_specConfig.Second).Address;
                EnterBarrier("actors-started");

                // Acquire ActorRef from first system
                var tuple = Identify(_specConfig.Second, "subject");
                int uidFirst = tuple.Item1;
                IActorRef subjectFirst = tuple.Item2;
                EnterBarrier("actor-identified");

                // Manually Quarantine the other system
                RARP.For(Sys).Provider.Transport.Quarantine(Node(_specConfig.Second).Address, uidFirst);

                // Quarantine is up -- Cannot communicate with remote system any more
                Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject").Tell("identify");
                ExpectNoMsg(2.Seconds());

                // Shut down the other system -- which results in restart (see runOn(second))
                TestConductor.Shutdown(_specConfig.Second).Wait(TimeSpan.FromSeconds(30));

                // Now wait until second system becomes alive again
                Within(30.Seconds(), () =>
                {
                    // retry because the Subject actor might not be started yet
                    AwaitAssert(() =>
                    {
                        Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject").Tell("identify");
                        var tuple2 = ExpectMsg<(int, IActorRef)>(TimeSpan.FromSeconds(1));
                        tuple2.Item1.Should().NotBe(uidFirst);
                        tuple2.Item2.Should().NotBe(subjectFirst);
                    });
                });

                // If we got here the Quarantine was successfully pierced since it is configured to last 1 day
                Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject").Tell("shutdown");
            }, _specConfig.First);

            RunOn(() =>
            {
                var addr = ((ExtendedActorSystem)Sys).Provider.DefaultAddress;
                Sys.ActorOf(Props.Create<RemoteQuarantinePiercingSpecConfig.Subject>(), "subject");
                EnterBarrier("actors-started");

                EnterBarrier("actor-identified");
                Sys.WhenTerminated.Wait(30.Seconds());

                var freshSystem = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString($@"
                    akka.remote.dot-netty.tcp.hostname = {addr.Host}
                    akka.remote.dot-netty.tcp.port = {addr.Port}
                ").WithFallback(Sys.Settings.Config));

                freshSystem.ActorOf(Props.Create<RemoteQuarantinePiercingSpecConfig.Subject>(), "subject");
                freshSystem.WhenTerminated.Wait(30.Seconds());
            }, _specConfig.Second);
        }
    }
}
