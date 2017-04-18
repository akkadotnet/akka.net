//-----------------------------------------------------------------------
// <copyright file="MiscMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Configuration;
using Akka.Remote.Routing;
using Akka.Remote.Serialization;
using Akka.Routing;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using FluentAssertions;
using FsCheck.Experimental;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Serialization
{
    public class MiscMessageSerializerSpec : AkkaSpec
    {
        #region actor
        public class Watchee : UntypedActor
        {
            protected override void OnReceive(object message)
            {

            }
        }

        public class Watcher : UntypedActor
        {
            protected override void OnReceive(object message)
            {

            }
        }
        #endregion

        public MiscMessageSerializerSpec() : base(ConfigurationFactory.ParseString("").WithFallback(RemoteConfigFactory.Default()))
        {
        }

        [Fact]
        public void Can_serialize_Identify()
        {
            var identify = new Identify("message");
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
        public void Can_serialize_ActorIdentityWithoutActorRef()
        {
            var actorIdentity = new ActorIdentity("message", null);
            AssertEqual(actorIdentity);
        }

        [Fact]
        public void Can_serialize_ActorRef()
        {
            var actorRef = ActorOf<BlackHoleActor>();
            AssertEqual(actorRef);
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
            AssertEqual(heartbeatRsp);
        }

        [Fact]
        public void Can_serialize_LocalScope()
        {
            var localScope = LocalScope.Instance;
            AssertEqual(localScope);
        }

        [Fact]
        public void Can_serialize_RemoteScope()
        {
            var address = new Address("akka.tcp", "TestSys", "localhost", 23423);
            var remoteScope = new RemoteScope(address);
            AssertEqual(remoteScope);
        }

        [Fact]
        public void Can_serialize_Config()
        {
            var message = ConfigurationFactory.Default();
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_EmptyConfig()
        {
            var message = ConfigurationFactory.Empty;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_FromConfig()
        {
            var fromConfig = FromConfig.Instance;
            AssertEqual(fromConfig);
        }

        [Fact]
        public void Can_serialize_DefaultResizer()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5D, 0.3D, 0.1D, 55);
            AssertEqual(defaultResizer);
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
        public void Can_serialize_RandomPoolWithDispatcher()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5D, 4D, 0.1D, 55);
            var message = new RandomPool(
                nrOfInstances: 25,
                routerDispatcher: "my-dispatcher",
                usePoolDispatcher: true,
                resizer: defaultResizer,
                supervisorStrategy: SupervisorStrategy.DefaultStrategy);

            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RoundRobinPool()
        {
            var message = new RoundRobinPool(nrOfInstances: 25);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RoundRobinPoolWithDispatcher()
        {
            var defaultResizer = new DefaultResizer(2, 4, 1, 0.5D, 4D, 0.1D, 55);
            var message = new RoundRobinPool(
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
        public void Can_serialize_RemoteRouterConfig()
        {
            var message = new RemoteRouterConfig(
                local: new RandomPool(25),
                nodes: new List<Address> { new Address("akka.tcp", "TestSys", "localhost", 23423) });
            AssertEqual(message);
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
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