//-----------------------------------------------------------------------
// <copyright file="ClusterClientV2RemotingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Configuration;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.Client.Serialization;
using Akka.Configuration;
using Akka.Remote;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using ActorTestKit = Akka.TestKit.Xunit.TestKit;

namespace Akka.Cluster.Tools.Tests.ClusterClient;

/// <summary>
/// Genuine TCP exchange of all client manifests with opposite writer bindings. Both systems
/// run the same binaries in one process; this is not mixed-release or native cluster proof.
/// </summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class ClusterClientV2RemotingSpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ITestOutputHelper _output;

    public ClusterClientV2RemotingSpec(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    [Theory(DisplayName = "Should_ExchangeEveryClientManifest_When_RemoteSystemsUseOpposingWriteBindings")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_ExchangeEveryClientManifest_When_RemoteSystemsUseOpposingWriteBindings(bool artery)
    {
        var transport = artery ? "artery" : "classic";
        await using var client = new ActorTestKit(RemoteConfig(artery, v2: false), $"client-{transport}-legacy", _output);
        try
        {
            await using var server = new ActorTestKit(RemoteConfig(artery, v2: true), $"client-{transport}-v2", _output);
            try
            {
                ClusterClientSettings.Create(client.Sys);
                ClusterClientSettings.Create(server.Sys);
                server.Sys.ActorOf(Props.Create(() => new Echo()), "echo");
                var clientAddress = ((ExtendedActorSystem)client.Sys).Provider.DefaultAddress;
                var serverAddress = ((ExtendedActorSystem)server.Sys).Provider.DefaultAddress;
                clientAddress.Port.Should().BeGreaterThan(0);
                serverAddress.Port.Should().BeGreaterThan(0);
                serverAddress.Should().NotBe(clientAddress);
                var remoteEcho = await client.Sys.ActorSelection(new RootActorPath(serverAddress) / "user" / "echo").ResolveOne(Timeout);
                remoteEcho.Should().BeOfType<RemoteActorRef>();
                var probe = client.CreateTestProbe();

                foreach (var node in new[] { client.Sys, server.Sys })
                {
                    node.Serialization.GetSerializerById(15).Should().BeOfType<ClusterClientMessageSerializer>();
                    node.Serialization.GetSerializerById(55).Should().BeOfType<ClusterClientMessagePackSerializer>();
                }

                foreach (var sample in ClusterClientV2SerializerSpec.Corpus)
                {
                    _output.WriteLine($"{transport}: {sample.Name}");
                    client.Sys.Serialization.FindSerializerFor(sample.Message).Identifier.Should().Be(15, sample.Name);
                    server.Sys.Serialization.FindSerializerFor(sample.Message).Identifier.Should().Be(55, sample.Name);
                    // Outgoing id15 and echo reply id55 exercise both readers over actual TCP.
                    remoteEcho.Tell(sample.Message, probe.Ref);
                    var received = await probe.ExpectMsgAsync<object>(Timeout);
                    received.Should().Be(sample.Message, sample.Name);
                }
            }
            finally
            {
                await server.ShutdownAsync(Timeout, verifySystemShutdown: true);
            }
        }
        finally
        {
            await client.ShutdownAsync(Timeout, verifySystemShutdown: true);
        }
    }

    private static Config RemoteConfig(bool artery, bool v2) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
        akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
        akka.cluster.client.use-legacy-serialization = off
        akka.remote.dot-netty.tcp {
            hostname = "127.0.0.1"
            port = 0
        }
        akka.remote.artery {
            enabled = {{(artery ? "on" : "off")}}
            canonical.hostname = "127.0.0.1"
            canonical.port = 0
        }
        """).WithFallback(ClusterClientReceptionist.DefaultConfig()).WithFallback(ClusterConfigFactory.Default());

    private sealed class Echo : ReceiveActor
    {
        public Echo()
        {
            ReceiveAny(message => Sender.Tell(message, Self));
        }
    }
}
