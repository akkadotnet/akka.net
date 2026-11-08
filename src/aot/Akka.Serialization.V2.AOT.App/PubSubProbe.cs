// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Buffers;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Serialization;
using Akka.Configuration;
using Akka.Event;
using Akka.Serialization;

namespace Akka.AOT.SerializationV2;

/// <summary>Ordinary consumer: no test assembly identity, internal type references or explicit module registration.</summary>
internal static class PubSubProbe
{
    private sealed record Fixture(string Name, string Manifest, string LegacyHex);

    // Frozen pre-port legacy records, serializer source blob 7e53c8ec40dc06800ec0aad807a93e557e2ac595.
    // Re-encoding through the unchanged legacy writer verifies every field without reflective inspection
    // or references that root the private Status/Delta/Subscriber domain types in this consumer.
    private static readonly Fixture[] Fixtures =
    [
        new("DeltaEmpty", "B", ""),
        new("DeltaTombstone", "B", "0a360a200a0670756273756212093132372e302e302e3118f8132208616b6b612e74637010031a100a0a2f757365722f676f6e6512020807"),
        new("Publish", "E", "0a05746f7069631a300a0568656c6c6f10111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
        new("PublishEachGroup", "E", "0a05746f7069631a300a0568656c6c6f10111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c69622001"),
        new("Send", "C", "0a0c2f757365722f7461726765741a300a0568656c6c6f10111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
        new("SendLocal", "C", "0a0c2f757365722f74617267657410011a300a0568656c6c6f10111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
        new("SendToAll", "D", "0a0c2f757365722f7461726765741a300a0568656c6c6f10111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
        new("SendToAllExcludeSelf", "D", "0a0c2f757365722f74617267657410011a300a0568656c6c6f10111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
        new("SendToOneSubscriber", "F", "0a300a0568656c6c6f10111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
        new("StatusEmpty", "A", ""),
        new("StatusReply", "A", "0a240a200a0670756273756212093132372e302e302e3118f8132208616b6b612e74637010111001"),
        new("PublishNull", "E", "0a05746f7069631a002001"),
        new("SendNull", "C", "0a0c2f757365722f74617267657410011a00"),
        new("SendToAllNull", "D", "0a0c2f757365722f74617267657410011a00"),
        new("SendToOneSubscriberNull", "F", "0a00"),
    ];

    internal static async Task<int> RunAsync()
    {
        var cases = 0;
        var nativeWrites = 0;
        foreach (var mode in new[] { "off", "on", "pinned" })
        {
            var pins = mode == "pinned" ? """
                akka.actor.serialization-bindings {
                  "Akka.Cluster.Tools.PublishSubscribe.IDistributedPubSubMessage, Akka.Cluster.Tools" = akka-pubsub
                  "Akka.Cluster.Tools.PublishSubscribe.Internal.SendToOneSubscriber, Akka.Cluster.Tools" = akka-pubsub
                }
                """ : "";
            var config = ConfigurationFactory.ParseString(
                $"akka.actor.serialization-v2 = {(mode == "off" ? "off" : "on")}\n{pins}");
            var watchdog = new LogWatchdogFilter();
            var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), new LogFilterSetup([watchdog]));
            var system = ActorSystem.Create("pubsub-native-" + mode, setup);
            try
            {
                watchdog.ThrowIfAnyProblems(mode, "startup");
                var legacy = new DistributedPubSubMessageSerializer((ExtendedActorSystem)system);
                foreach (var fixture in Fixtures)
                {
                    var historical = Convert.FromHexString(fixture.LegacyHex);
                    var message = system.Serialization.Deserialize(historical, 9, fixture.Manifest);
                    nativeWrites += Verify(message, historical, fixture.Manifest, fixture.Name, legacy, system, mode);
                    cases++;
                }

                // Additional public shapes exercise binary and live actor-reference payloads.
                var actor = system.ActorOf(Props.Empty, "registered");
                foreach (var payload in new object[] { new byte[] { 0, 1, 127, 255 }, actor })
                {
                    var message = new Send("/user/target", payload, true);
                    nativeWrites += Verify(message, legacy.ToBinary(message), "C", "public-payload", legacy, system, mode);
                    cases++;
                }
            }
            finally
            {
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
            }
            watchdog.ThrowIfAnyProblems(mode, "shutdown");
        }
        Require(cases == 51 && nativeWrites == 17, "Incomplete PubSub corpus or native writes.");
        Console.WriteLine("[pubsub-native] OK cases=51 native-writes=17");
        return cases;
    }

    private static int Verify(object message, byte[] historical, string manifest, string name,
        DistributedPubSubMessageSerializer legacy, ActorSystem system, string mode)
    {
        var writer = system.Serialization.FindSerializerFor(message);
        Require(writer.Identifier == (mode == "on" ? 49 : 9), $"{name}: writer selection.");
        Require(legacy.ToBinary(message).AsSpan().SequenceEqual(historical), $"{name}: historical read lost fields.");
        var bytes = writer.ToBinary(message);
        var restored = system.Serialization.Deserialize(bytes, writer.Identifier, manifest);
        Require(restored.GetType() == message.GetType(), $"{name}: runtime type.");
        Require(legacy.ToBinary(restored).AsSpan().SequenceEqual(historical), $"{name}: round-trip lost fields.");
        if (mode == "on")
        {
            var v2 = writer as SerializerV2 ?? throw new InvalidOperationException("PubSub id49 requires native buffer writes.");
            var buffer = new ArrayBufferWriter<byte>();
            Require(v2.Serialize(message, buffer) == buffer.WrittenCount, $"{name}: written count.");
            Require(buffer.WrittenSpan.SequenceEqual(bytes), $"{name}: native writer bytes.");
            var fromSequence = v2.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), manifest);
            Require(fromSequence.GetType() == message.GetType(), $"{name}: sequence runtime type.");
            Require(legacy.ToBinary(fromSequence).AsSpan().SequenceEqual(historical), $"{name}: native reader lost fields.");
            return 1;
        }
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
