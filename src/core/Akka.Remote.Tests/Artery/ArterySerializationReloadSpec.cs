//-----------------------------------------------------------------------
// <copyright file="ArterySerializationReloadSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public class ArterySerializationReloadSpec : AkkaSpec
    {
        public ArterySerializationReloadSpec(ITestOutputHelper output) : base(output)
        {
        }

        private sealed class ReloadedMessage
        {
            public ReloadedMessage(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private sealed class ReloadedSerializer : SerializerWithStringManifest
        {
            public static int SerializeCount;
            public static int DeserializeCount;

            public ReloadedSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 98765;

            public override string Manifest(object o) => "reloaded";

            public override byte[] ToBinary(object obj)
            {
                Interlocked.Increment(ref SerializeCount);
                return Encoding.UTF8.GetBytes(((ReloadedMessage)obj).Value);
            }

            public override object FromBinary(byte[] bytes, string manifest)
            {
                Interlocked.Increment(ref DeserializeCount);
                return new ReloadedMessage(Encoding.UTF8.GetString(bytes));
            }
        }

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        private static Config ArteryConfig(int inboundLanes = 1, int outboundLanes = 1) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = {{inboundLanes}}
            akka.remote.artery.advanced.outbound-lanes = {{outboundLanes}}
            """);

        private static Config ReloadedSerializerConfig()
        {
            var serializerType = typeof(ReloadedSerializer).AssemblyQualifiedName;
            var messageType = typeof(ReloadedMessage).AssemblyQualifiedName;
            return ConfigurationFactory.ParseString($$"""
                akka.actor {
                  serializers {
                    reloaded = "{{serializerType}}"
                  }
                  serialization-bindings {
                    "{{messageType}}" = reloaded
                  }
                }
                """);
        }

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string EchoSelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        [Theory(DisplayName = "Artery uses reloaded Serialization after InjectTopLevelFallback on an already-open association")]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        public async Task Should_Use_Reloaded_Serialization_On_Already_Open_Association(int inboundLanes, int outboundLanes)
        {
            Volatile.Write(ref ReloadedSerializer.SerializeCount, 0);
            Volatile.Write(ref ReloadedSerializer.DeserializeCount, 0);

            var systemA = ActorSystem.Create("ArteryReloadA", ArteryConfig(inboundLanes, outboundLanes));
            var systemB = ActorSystem.Create("ArteryReloadB", ArteryConfig(inboundLanes, outboundLanes));
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");

                // Open both A -> B and B -> A associations before injecting the new serializer config.
                var echoSelection = systemA.ActorSelection(EchoSelectionPath(systemB, "echo"));
                var echoRef = await echoSelection.ResolveOne(TimeSpan.FromSeconds(10));

                var probe = CreateTestProbe(systemA);
                echoRef.Tell("ping", probe.Ref);
                await probe.ExpectMsgAsync("ping", TimeSpan.FromSeconds(10));

                // Inject new serializer config on both systems (triggers Settings.InjectTopLevelFallback -> ReloadSerialization).
                var serializerConfig = ReloadedSerializerConfig();
                systemA.Settings.InjectTopLevelFallback(serializerConfig);
                systemB.Settings.InjectTopLevelFallback(serializerConfig);

                // Send a message whose serializer was only registered after the association was already open.
                echoRef.Tell(new ReloadedMessage("after-reload"), probe.Ref);
                var reply = await probe.ExpectMsgAsync<ReloadedMessage>(TimeSpan.FromSeconds(10));
                reply.Value.Should().Be("after-reload");

                // Both A -> B and B -> A must have encoded and decoded using ReloadedSerializer.
                Volatile.Read(ref ReloadedSerializer.SerializeCount).Should().BeGreaterOrEqualTo(2);
                Volatile.Read(ref ReloadedSerializer.DeserializeCount).Should().BeGreaterOrEqualTo(2);
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
