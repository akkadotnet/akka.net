//-----------------------------------------------------------------------
// <copyright file="RemoteDeathWatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Remote.Tests
{
    public class RemoteDeathWatchSpec : AkkaSpec
    {
        private static readonly Config _config = ConfigurationFactory.ParseString(@"
            akka {
                actor {
                    provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                    deployment {
                        /watchers.remote = ""akka.tcp://other@localhost:2666""
                    }
                }
                remote.retry-gate-closed-for = 1 s
                remote.initial-system-message-delivery-timeout = 3 s
                remote.dot-netty.tcp {
                    hostname = ""localhost""
                        port = 0
                }
            }
        ");

        private readonly ActorSystem _other;

        public RemoteDeathWatchSpec() : base(_config)
        {
            _other = ActorSystem.Create("other",
                ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.port=2666").WithFallback(_config));
        }

        protected override void BeforeTermination()
        {
            var mute = EventFilter.Warning(pattern: new Regex("received dead letter.*Disassociate")).Mute();
            Sys.EventStream.Publish(mute);
        }

        protected override void AfterTermination()
        {
            _other.Terminate().Wait(TimeSpan.FromSeconds(20));
        }

        [Fact]
        public void Must_receive_Terminated_when_system_of_deserialized_ActorRef_is_not_running()
        {
            var probe = CreateTestProbe();
            Sys.EventStream.Subscribe(probe.Ref, typeof(QuarantinedEvent));
            var rarp = RARP.For(Sys).Provider;
            // pick an unused port (not going to use a socket address generator here; just a port not used by either actor system)
            int port = rarp.DefaultAddress.Port.Value;
            while (port == rarp.DefaultAddress.Port.Value || port == 2666)
                port = ThreadLocalRandom.Current.Next(1, 65535);
            // simulate de-serialized ActorRef
            var @ref = rarp.ResolveActorRef($"akka.tcp://OtherSystem@localhost:{port}/user/foo/bar#1752527294");
            Action<IActorDsl> act = dsl =>
            {
                dsl.OnPreStart = context =>
                {
                    context.Watch(@ref);
                };
                dsl.Receive<Terminated>((t, ctx) =>
                {
                    TestActor.Tell(t.ActorRef);
                });
            };
            Sys.ActorOf(Props.Create(() => new Act(act)).WithDeploy(Deploy.Local));

            ExpectMsg(@ref, TimeSpan.FromSeconds(20));
            // we don't expect real quarantine when the UID is unknown, i.e. QuarantinedEvent is not published 
            probe.ExpectNoMsg(TimeSpan.FromSeconds(3));
            // The following verifies that re-delivery of Watch message is stopped.
            // It was observed as periodic logging of "address is now gated" when the gate was lifted.
            Sys.EventStream.Subscribe(probe.Ref, typeof(Warning));
            probe.ExpectNoMsg(TimeSpan.FromSeconds(rarp.RemoteSettings.RetryGateClosedFor.TotalSeconds*2));
        }

        [Fact]
        public void Must_receive_terminated_when_watched_node_is_unknown_host()
        {
            var path = new RootActorPath(new Address("akka.tcp", Sys.Name, "unknownhost", 2552)) / "user" / "subject";
            var rarp = RARP.For(Sys).Provider;
            Action<IActorDsl> act = dsl =>
            {
                dsl.OnPreStart = context =>
                {
                    context.Watch(rarp.ResolveActorRef(path));
                };
                dsl.Receive<Terminated>((t, ctx) =>
                {
                    TestActor.Tell(t.ActorRef.Path);
                });
            };

            Sys.ActorOf(Props.Create(() => new Act(act)).WithDeploy(Deploy.Local), "observer2");

            ExpectMsg(path, TimeSpan.FromSeconds(60));
        }

        [Fact]
        public void Must_receive_ActorIdentity_null_when_identified_node_is_unknown_host()
        {
            var path = new RootActorPath(new Address("akka.tcp", Sys.Name, "unknownhost2", 2552)) / "user" / "subject";
            Sys.ActorSelection(path).Tell(new Identify(path));
            var identify = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(60));
            identify.Subject.ShouldBe(null);
            identify.MessageId.ShouldBe(path);
        }

        [Fact]
        public void Must_quarantine_systems_after_unsuccessful_system_message_delivery_if_have_not_communicated_before()
        {
            // Synthesize an ActorRef to a remote system this one has never talked to before.
            // This forces ReliableDeliverySupervisor to start with unknown remote system UID.
            var rarp = RARP.For(Sys).Provider;
            int port = rarp.DefaultAddress.Port.Value;
            while (port == rarp.DefaultAddress.Port.Value || port == 2666)
                port = ThreadLocalRandom.Current.Next(1, 65535);

            var extinctPath = new RootActorPath(new Address("akka.tcp", "extinct-system", "localhost", port)) / "user" / "noone";
            var transport = rarp.Transport;
            var extinctRef = new RemoteActorRef(transport, transport.LocalAddressForRemote(extinctPath.Address), 
                extinctPath, ActorRefs.Nobody, Props.None, Deploy.None);

            var probe = CreateTestProbe();
            probe.Watch(extinctRef);
            probe.Unwatch(extinctRef);

            probe.ExpectNoMsg(TimeSpan.FromSeconds(5));
            Sys.EventStream.Subscribe(probe.Ref, typeof(Warning));
            probe.ExpectNoMsg(TimeSpan.FromSeconds(rarp.RemoteSettings.RetryGateClosedFor.TotalSeconds * 2));
        }
    }
}

