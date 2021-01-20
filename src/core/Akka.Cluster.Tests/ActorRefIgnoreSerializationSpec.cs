//-----------------------------------------------------------------------
// <copyright file="ActorRefIgnoreSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using FluentAssertions;
using Xunit.Abstractions;
using Akka.Util;

namespace Akka.Cluster.Tests
{
    public class ActorRefIgnoreSerializationSpec : AkkaSpec
    {
        const string Config = @"
            akka {
                loglevel = debug
                actor.provider = cluster
            }
            akka.remote.log-remote-lifecycle-events = off
            akka.remote.dot-netty.tcp.port = 0";

        private ActorSystem system1;
        private ActorSystem system2;


        public ActorRefIgnoreSerializationSpec(ITestOutputHelper output)
            : base(Config, output)
        {
            system1 = ActorSystem.Create("sys1", Config);
            system2 = ActorSystem.Create("sys2", Config);
        }


        protected override void AfterAll()
        {
            base.AfterAll();
            system1.Terminate();
            system2.Terminate();
        }

        [Fact]
        public void ActorSystem_IgnoreRef_should_return_a_serializable_ActorRef_that_can_be_sent_between_two_ActorSystems_using_remote()
        {
            var ignoreRef = system1.IgnoreRef;
            var remoteRefStr = ignoreRef.Path.ToSerializationFormatWithAddress(system1.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress);

            remoteRefStr.Should().Be(IgnoreActorRef.StaticPath.ToString(), "check ActorRef path stays untouched, ie: /local/ignore");

            var providerSys2 = system2.AsInstanceOf<ExtendedActorSystem>().Provider;
            var deserRef = providerSys2.ResolveActorRef(remoteRefStr);
            deserRef.Path.Should().Be(IgnoreActorRef.StaticPath, "check ActorRef path stays untouched when deserialized by another actor system");
            deserRef.Should().BeSameAs(system2.IgnoreRef, "check ActorRef path stays untouched when deserialized by another actor system");

            var surrogate = ignoreRef.ToSurrogate(system1);
            var fromSurrogate1 = surrogate.FromSurrogate(system1);
            fromSurrogate1.Should().BeOfType<IgnoreActorRef>();
            ((IgnoreActorRef)fromSurrogate1).Path.Should().Be(system1.IgnoreRef.Path);
        }

        [Fact]
        public void ActorSystem_IgnoreRef_should_return_same_instance_when_deserializing_it_twice_IgnoreActorRef_is_cached()
        {
            var ignoreRef = system1.IgnoreRef;
            var remoteRefStr = ignoreRef.Path.ToSerializationFormat();
            var providerSys1 = system1.AsInstanceOf<ExtendedActorSystem>().Provider;

            var deserRef1 = providerSys1.ResolveActorRef(remoteRefStr);
            var deserRef2 = providerSys1.ResolveActorRef(remoteRefStr);
            deserRef1.Should().BeSameAs(deserRef2);
        }
    }
}

