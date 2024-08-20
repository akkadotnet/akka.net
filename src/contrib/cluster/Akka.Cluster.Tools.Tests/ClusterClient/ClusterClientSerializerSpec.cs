//-----------------------------------------------------------------------
// <copyright file="ClusterClientSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.Client.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.ClusterClient;

public class ClusterClientSerializerLegacySpec : ClusterClientSerializerSpecBase
{
    public ClusterClientSerializerLegacySpec(ITestOutputHelper output) : base(output, true)
    {
    }
}

public class ClusterClientSerializerSpec : ClusterClientSerializerSpecBase
{
    public ClusterClientSerializerSpec(ITestOutputHelper output) : base(output, false)
    {
    }
}

public abstract class ClusterClientSerializerSpecBase : AkkaSpec
{
    private static Config Config(bool useLegacy)
        => ConfigurationFactory.ParseString($"""
akka.extensions = ["Akka.Cluster.Tools.Client.ClusterClientReceptionistExtensionProvider, Akka.Cluster.Tools"]
akka.log-serializer-override-on-start = on
akka.actor.provider = cluster
akka.remote.dot-netty.tcp.port = 0
akka.cluster.client.use-legacy-serialization = {(useLegacy ? "on" : "off")}
""")
            .WithFallback(ClusterClientReceptionist.DefaultConfig());

    private readonly bool _useLegacy;
    private readonly ActorPath _actorPath;
    
    protected ClusterClientSerializerSpecBase(ITestOutputHelper output, bool useLegacy) : base(Config(useLegacy), output)
    {
        _useLegacy = useLegacy;
        _actorPath = ActorPath.Parse(Cluster.Get(Sys).SelfAddress.ToString()) / "system" / "receptionist";
    }

    [Theory(DisplayName = "ClusterClient using settings from actor system should use proper serializer")]
    [InlineData(typeof(Client.ClusterClient.Send))]
    [InlineData(typeof(Client.ClusterClient.SendToAll))]
    [InlineData(typeof(Client.ClusterClient.Publish))]
    [InlineData(typeof(Client.ClusterClient.RefreshContactsTick))]
    [InlineData(typeof(Client.ClusterClient.HeartbeatTick))]
    [InlineData(typeof(Client.ClusterClient.ReconnectTimeout))]
    public void SettingsFromActorSystemTest(Type messageType)
    {
        var settings = ClusterClientSettings.Create(Sys)
            .WithInitialContacts(new []{_actorPath}.ToImmutableHashSet());
        var clusterClient = Sys.ActorOf(Client.ClusterClient.Props(settings));
        
        var serializer = Sys.Serialization.FindSerializerForType(messageType);
        if (_useLegacy)
        {
            serializer.Should().BeOfType<NewtonSoftJsonSerializer>();
        }
        else
        {
            serializer.Should().BeOfType<ClusterClientMessageSerializer>();
        }
    }
    
    [Theory(DisplayName = "ClusterClient using settings from config should use proper serializer")]
    [InlineData(typeof(Client.ClusterClient.Send))]
    [InlineData(typeof(Client.ClusterClient.SendToAll))]
    [InlineData(typeof(Client.ClusterClient.Publish))]
    [InlineData(typeof(Client.ClusterClient.RefreshContactsTick))]
    [InlineData(typeof(Client.ClusterClient.HeartbeatTick))]
    [InlineData(typeof(Client.ClusterClient.ReconnectTimeout))]
    public async Task SettingsFromConfigTest(Type messageType)
    {
        var config = Sys.Settings.Config.GetConfig("akka.cluster.client");
        var settings = ClusterClientSettings.Create(config)
            .WithInitialContacts(new []{_actorPath}.ToImmutableHashSet());
        var clusterClient = Sys.ActorOf(Client.ClusterClient.Props(settings));

        // need to wait until the ClusterClient actor is up to make sure that serializer mapping was updated correctly.
        await clusterClient.Ask<ActorIdentity>(new Identify(""));
        
        var serializer = Sys.Serialization.FindSerializerForType(messageType);
        if (_useLegacy)
        {
            serializer.Should().BeOfType<NewtonSoftJsonSerializer>();
        }
        else
        {
            serializer.Should().BeOfType<ClusterClientMessageSerializer>();
        }
    }
    
    [Theory(DisplayName = "ClusterClient messages should be serialized properly")]
    [MemberData(nameof(MessageGenerator))]
    public void SerializeMessageTypeType(IClusterClientProtocolMessage message)
    {
        var settings = ClusterClientSettings.Create(Sys)
            .WithInitialContacts(new []{_actorPath}.ToImmutableHashSet());
        var clusterClient = Sys.ActorOf(Client.ClusterClient.Props(settings));
        
        var serializer = Sys.Serialization.FindSerializerFor(message);
        var serialized = serializer.ToBinary(message);
        
        IClusterClientProtocolMessage deserialized;
        if (_useLegacy)
        {
            deserialized = serializer.FromBinary<IClusterClientProtocolMessage>(serialized);
        }
        else
        {
            var manifestSerializer = (SerializerWithStringManifest)serializer;
            var manifest = manifestSerializer.Manifest(message);
            deserialized = (IClusterClientProtocolMessage) manifestSerializer.FromBinary(serialized, manifest);
        }

        deserialized.Should().Be(message);
    }

    public static IEnumerable<object[]> MessageGenerator()
    {
        yield return new object[] { new Client.ClusterClient.Send("path", "message") };
        yield return new object[] { new Client.ClusterClient.SendToAll("path", "message") };
        yield return new object[] { new Client.ClusterClient.Publish("topic", "message") };
        yield return new object[] { Client.ClusterClient.RefreshContactsTick.Instance };
        yield return new object[] { Client.ClusterClient.HeartbeatTick.Instance };
        yield return new object[] { Client.ClusterClient.ReconnectTimeout.Instance };
    }
}
