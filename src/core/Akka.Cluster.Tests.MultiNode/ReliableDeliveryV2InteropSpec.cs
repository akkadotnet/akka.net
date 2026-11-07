//-----------------------------------------------------------------------
// <copyright file="ReliableDeliveryV2InteropSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Configuration;
using Akka.Configuration;
using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode;

public class ReliableDeliveryV2InteropConfig : MultiNodeConfig
{
    public RoleName Legacy { get; }
    public RoleName V2 { get; }

    public ReliableDeliveryV2InteropConfig() : this(false)
    {
    }

    protected ReliableDeliveryV2InteropConfig(bool artery)
    {
        Legacy = Role("legacy");
        V2 = Role("v2");
        CommonConfig = ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = {{(artery ? "on" : "off")}}
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            """).WithFallback(ClusterConfigFactory.Default());
        NodeConfig(new[] { Legacy }, new[] { ConfigurationFactory.ParseString("akka.actor.serialization-v2 = off") });
        NodeConfig(new[] { V2 }, new[] { ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on") });
    }
}

public sealed class ReliableDeliveryV2ArteryInteropConfig : ReliableDeliveryV2InteropConfig
{
    public ReliableDeliveryV2ArteryInteropConfig() : base(true)
    {
    }
}

public sealed class ReliableDeliveryV2ClassicInteropSpec : ReliableDeliveryV2InteropSpec
{
    public ReliableDeliveryV2ClassicInteropSpec() : this(new ReliableDeliveryV2InteropConfig())
    {
    }

    private ReliableDeliveryV2ClassicInteropSpec(ReliableDeliveryV2InteropConfig config)
        : base(config, typeof(ReliableDeliveryV2ClassicInteropSpec))
    {
    }
}

public sealed class ReliableDeliveryV2ArteryInteropSpec : ReliableDeliveryV2InteropSpec
{
    public ReliableDeliveryV2ArteryInteropSpec() : this(new ReliableDeliveryV2ArteryInteropConfig())
    {
    }

    private ReliableDeliveryV2ArteryInteropSpec(ReliableDeliveryV2ArteryInteropConfig config)
        : base(config, typeof(ReliableDeliveryV2ArteryInteropSpec))
    {
    }
}

/// <summary>
/// Two processes with the same candidate binaries and opposing write bindings exchange every
/// Delivery manifest. This establishes process/transport interoperability, not mixed-release support.
/// MultiNodeSpec captures node logs through the adapter and constructs specs without an output helper.
/// </summary>
public abstract class ReliableDeliveryV2InteropSpec : MultiNodeSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ReliableDeliveryV2InteropConfig _config;

    protected ReliableDeliveryV2InteropSpec(ReliableDeliveryV2InteropConfig config, Type specType)
        : base(config, specType)
    {
        _config = config;
    }

    protected override int InitialParticipantsValueFactory => 2;

    [MultiNodeFact(DisplayName = "Should_ExchangeAllDeliveryManifests_When_ProcessesUseOpposingWriteBindings")]
    public async Task Should_ExchangeAllDeliveryManifests_When_ProcessesUseOpposingWriteBindings()
    {
        Sys.ActorOf(Props.Create(() => new Echo()), "echo");
        await EnterBarrierAsync("echo-ready");

        // Each process initiates an exchange, so both writers and both readers participate.
        var other = Myself.Equals(_config.Legacy) ? _config.V2 : _config.Legacy;
        var expectedWriter = Myself.Equals(_config.Legacy) ? 36 : 76;
        var remoteEcho = await Sys.ActorSelection(await NodeAsync(other) / "user" / "echo").ResolveOne(Timeout);
        remoteEcho.Should().BeOfType<RemoteActorRef>();
        remoteEcho.Tell(Environment.ProcessId, TestActor);
        (await ExpectMsgAsync<int>(Timeout)).Should().NotBe(Environment.ProcessId,
            "the peer must execute in a different operating-system process");

        foreach (var message in Corpus(TestActor))
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Identifier.Should().Be(expectedWriter);
            Log.Info("Delivery interop: process [{0}], writer [{1}], type [{2}]", Environment.ProcessId,
                serializer.Identifier, message.GetType());
            remoteEcho.Tell(message, TestActor);
            var received = await ExpectMsgAsync<IDeliverySerializable>(Timeout);
            received.GetType().Should().Be(message.GetType());
            received.Should().BeEquivalentTo((object)message);
        }

        await EnterBarrierAsync("corpus-exchanged");
    }

    private static IEnumerable<IDeliverySerializable> Corpus(IActorRef controller)
    {
        yield return new ConsumerController.SequencedMessage<string>("producer", 3, "payload", true, true)
        {
            ProducerController = controller
        };
        yield return new ProducerController.Ack(3);
        yield return new ProducerController.Request(3, 8, true, false);
        yield return new ProducerController.Resend(3);
        yield return new ProducerController.RegisterConsumer<string>(controller);
        var sent = new DurableProducerQueue.MessageSent<string>(3, "payload", true, "q", 123456);
        yield return sent;
        yield return new DurableProducerQueue.Confirmed(2, "q", 123455);
        yield return new DurableProducerQueue.State<string>(4, 2,
            ImmutableDictionary<string, (long, long)>.Empty.Add("q", (2, 123455)),
            ImmutableList<DurableProducerQueue.MessageSent<string>>.Empty.Add(sent));
        yield return new DurableProducerQueue.Cleanup(ImmutableHashSet.Create("q"));
        var chunk = new ChunkedMessage(new byte[] { 9, 8, 7 }.AsMemory(), true, false, 17, "C");
        yield return ConsumerController.SequencedMessage<string>.FromChunkedMessage("producer", 5, chunk,
            true, false, controller);
        yield return DurableProducerQueue.MessageSent<string>.FromChunked(5, chunk, true, "q", 123457);
    }

    private sealed class Echo : ReceiveActor
    {
        public Echo()
        {
            Receive<int>(_ => Sender.Tell(Environment.ProcessId, Self));
            Receive<IDeliverySerializable>(message => Sender.Tell(message, Self));
        }
    }
}
