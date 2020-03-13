//-----------------------------------------------------------------------
// <copyright file="MiscMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Remote.Configuration;
using Akka.Remote.Routing;
using Akka.Remote.Serialization;
using Akka.Routing;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class MiscMessageSerializerSpec : AkkaSpec
    {
        private Config TestConfiguration { get; } = @"
            akka {
                string-config = ""1.3.0""
                boolean-config = on
                int-config = 1244
                long-config = 4353454
                float-config = 46.5
                timespan-config = 10s
                string-list-config = [""Akka.Event.DefaultLogger"", ""Akka.Event.TraceLogger""]
                actor {
                    provider = ""Akka.Actor.LocalActorRefProvider""
                    creation-timeout = 20s
                }
                substitution-config = ${akka.string-config}
                substitution-concat-config = Hello ${akka.string-config}
            }";

        public MiscMessageSerializerSpec() : base(RemoteConfigFactory.Default())
        {
        }

        [Fact]
        public void Can_serialize_IdentifyWithString()
        {
            var identify = new Identify("message");
            AssertEqual(identify);
        }

        [Fact]
        public void Can_serialize_IdentifyWithInt32()
        {
            var identify = new Identify(50);
            AssertEqual(identify);
        }

        [Fact]
        public void Can_serialize_IdentifyWithInt64()
        {
            var identify = new Identify(50L);
            AssertEqual(identify);
        }

        [Fact]
        public void Can_serialize_IdentifyWithNull()
        {
            var identify = new Identify(null);
            AssertEqual(identify);
        }

        [Fact]
        public void Can_serialize_ActorIdentity()
        {
            var actorRef = ActorOf<BlackHoleActor>();
            var actorIdentity = new ActorIdentity("message", actorRef);
            AssertEqual(actorIdentity);
        }

        [Fact]
        public void Can_serialize_ActorIdentityWithoutMessage()
        {
            var actorRef = ActorOf<BlackHoleActor>();
            var actorIdentity = new ActorIdentity(null, actorRef);
            AssertEqual(actorIdentity);
        }

        [Fact]
        public void Can_serialize_ActorIdentityWithoutActorRef()
        {
            var actorIdentity = new ActorIdentity("message", null);
            AssertEqual(actorIdentity);
        }

        [Fact]
        public void Can_serialize_ActorRefRepointable()
        {
            var actorRef = Sys.ActorOf(Props.Empty, "hello");
            AssertEqual(actorRef);
        }

        [Fact]
        public void Can_serialize_ActorRefNoBody()
        {
            var actorRef = ActorRefs.Nobody;
            AssertEqual(actorRef);
        }

        [Fact]
        public void Can_serialize_ActorRefRemote()
        {
            var remoteSystem = ActorSystem.Create("remote", ConfigurationFactory.ParseString("akka.actor.provider = remote"));

            var address = new Address("akka.tcp", "TestSys", "localhost", 23423);
            var props = Props.Create<BlackHoleActor>().WithDeploy(new Deploy(new RemoteScope(address)));
            var actorRef = remoteSystem.ActorOf(props, "hello");

            var serializer = remoteSystem.Serialization.FindSerializerFor(actorRef).AsInstanceOf<SerializerWithStringManifest>();
            var serializedBytes = serializer.ToBinary(actorRef);
            var deserialized = serializer.FromBinary(serializedBytes, serializer.Manifest(actorRef));
            deserialized.Should().Be(actorRef);
        }

        [Fact]
        public void Can_serialize_Kill()
        {
            var kill = Kill.Instance;
            AssertEqual(kill);
        }

        [Fact]
        public void Can_serialize_PoisonPill()
        {
            var poisonPill = PoisonPill.Instance;
            AssertEqual(poisonPill);
        }

        [Fact(Skip = "Not implemented yet")]
        public void Can_serialize_LocalScope()
        {
            var localScope = LocalScope.Instance;
            AssertEqual(localScope);
        }

        [Fact]
        public void Can_serialize_Config()
        {
            var message = TestConfiguration;
            var actual = AssertAndReturn(message);
            actual.ToString(true).Should().BeEquivalentTo(message.ToString(true));
        }

        [Fact]
        public void Can_serialize_EmptyConfig()
        {
            var message = ConfigurationFactory.Empty;
            var actual = AssertAndReturn(message);
            actual.ToString(true).Should().BeEquivalentTo(message.ToString(true));
        }

        //
        // Routers
        //

        [Fact]
        public void Can_serialize_FromConfigSingleton()
        {
            var fromConfig = FromConfig.Instance;
            AssertEqual(fromConfig);
        }

        [Fact]
        public void Can_serialize_FromConfigWithResizerAndDispatcher()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5D, 0.3D, 0.1D, 55);
            var fromConfig = FromConfig.Instance
                .WithResizer(defaultResizer)
                .WithDispatcher("my-own-dispatcher");
            AssertEqual(fromConfig);
        }

        [Fact]
        public void Can_serialize_DefaultResizer()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5D, 0.3D, 0.1D, 55);
            AssertEqual(defaultResizer);
        }

        [Fact]
        public void Can_serialize_RoundRobinPool()
        {
            var message = new RoundRobinPool(nrOfInstances: 25);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RoundRobinPoolWithCustomResizer()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5, 0.2, 0.1, 55);
            var message = new RoundRobinPool(nrOfInstances: 25, resizer: defaultResizer);

            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RoundRobinPoolWithCustomDispatcher()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5, 0.2, 0.1, 55);

            var message = new RoundRobinPool(
                nrOfInstances: 25,
                resizer: defaultResizer,
                supervisorStrategy: Pool.DefaultSupervisorStrategy,
                routerDispatcher: "my-dispatcher",
                usePoolDispatcher: true);

            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_BroadcastPool()
        {
            var message = new BroadcastPool(nrOfInstances: 25);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_BroadcastPoolWithDispatcherAndResizer()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5D, 0.3D, 0.1D, 55);
            var message = new BroadcastPool(
                nrOfInstances: 25,
                routerDispatcher: "my-dispatcher",
                usePoolDispatcher: true,
                resizer: defaultResizer,
                supervisorStrategy: SupervisorStrategy.DefaultStrategy);

            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RandomPool()
        {
            var message = new RandomPool(nrOfInstances: 25);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RandomPoolWithDispatcherAndResizer()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5, 0.4, 0.1, 55);
            var message = new RandomPool(
                nrOfInstances: 25,
                routerDispatcher: "my-dispatcher",
                usePoolDispatcher: true,
                resizer: defaultResizer,
                supervisorStrategy: SupervisorStrategy.DefaultStrategy);

            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_ScatterGatherFirstCompletedPool()
        {
            var message = new ScatterGatherFirstCompletedPool(nrOfInstances: 25, within: 3.Seconds());
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_TailChoppingPool()
        {
            var message = new TailChoppingPool(nrOfInstances: 25, within: 3.Seconds(), interval: 3.Seconds());
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_ConsistentHashingPool()
        {
            var message = new ConsistentHashingPool(nrOfInstances: 25);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_ConsistentHashingPoolWithDispatcherAndResizer()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5D, 0.3D, 0.1D, 55);
            var message = new ConsistentHashingPool(
                nrOfInstances: 25,
                routerDispatcher: "my-dispatcher",
                usePoolDispatcher: true,
                resizer: defaultResizer,
                supervisorStrategy: SupervisorStrategy.DefaultStrategy);

            AssertEqual(message);
        }

        //
        // Remote Messages
        //

        [Fact]
        public void Can_serialize_RemoteRouterConfig()
        {
            var message = new RemoteRouterConfig(
                local: new RandomPool(25),
                nodes: new List<Address> { new Address("akka.tcp", "TestSys", "localhost", 23423) });
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RemoteWatcher_Hearthbeat()
        {
            var heartbeat = RemoteWatcher.Heartbeat.Instance;
            AssertEqual(heartbeat);
        }

        [Fact]
        public void Can_serialize_RemoteWatcher_HearthbeatRsp()
        {
            var heartbeatRsp = new RemoteWatcher.HeartbeatRsp(34);
            AssertAndReturn(heartbeatRsp).AddressUid.Should().Be(heartbeatRsp.AddressUid); //TODO: add Equals to RemoteWatcher.HeartbeatRsp
        }

        [Fact]
        public void Can_serialize_RemoteScope()
        {
            var address = new Address("akka.tcp", "TestSys", "localhost", 23423);
            var remoteScope = new RemoteScope(address);
            AssertEqual(remoteScope);
        }

        //
        // Serializer tests
        //

        [Fact]
        public void Serializer_must_reject_invalid_manifest()
        {
            var serializer = new MiscMessageSerializer(Sys.AsInstanceOf<ExtendedActorSystem>());
            Action comparison = () => serializer.Manifest("INVALID");
            comparison.ShouldThrow<ArgumentException>();
        }

        [Fact]
        public void Serializer_must_reject_deserialization_with_invalid_manifest()
        {
            var serializer = new MiscMessageSerializer(Sys.AsInstanceOf<ExtendedActorSystem>());
            Action comparison = () => serializer.FromBinary(new byte[0], "INVALID");
            comparison.ShouldThrow<SerializationException>();
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<MiscMessageSerializer>();
            var serializedBytes = serializer.ToBinary(message);

            if (serializer is SerializerWithStringManifest)
            {
                var serializerManifest = (SerializerWithStringManifest)serializer;
                return (T)serializerManifest.FromBinary(serializedBytes, serializerManifest.Manifest(message));
            }
            return (T)serializer.FromBinary(serializedBytes, typeof(T));
        }

        private void AssertEqual<T>(T message)
        {
            var deserialized = AssertAndReturn(message);
            Assert.Equal(message, deserialized);
        }
    }
}
