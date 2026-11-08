// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Cluster.Tools.Client;
using Akka.Configuration;
using Akka.Event;
using Akka.Serialization;
using ClientProtocol = Akka.Cluster.Tools.Client.ClusterClient;
using AkkaSerialization = Akka.Serialization.Serialization;


namespace Akka.AOT.SerializationV2;

internal static class ClientProbe
{
    internal static async Task<int> RunAsync()
    {
        (string Name, string Manifest, string LegacyHex, string V2Hex, bool Singleton)[] samples =
        [
            ("contacts-empty", "A", "", "810190", false),
            ("contacts", "A", "0a3a616b6b612e7463703a2f2f636c69656e742d70726f6f66403132372e302e302e313a323535322f73797374656d2f726563657074696f6e697374", "810191d93a616b6b612e7463703a2f2f636c69656e742d70726f6f66403132372e302e302e313a323535322f73797374656d2f726563657074696f6e697374", false),
            ("get-contacts", "B", "", "80", true),
            ("heartbeat-response", "D", "", "80", true),
            ("heartbeat-tick", "AE", "", "80", true),
            ("heartbeat", "C", "", "80", true),
            ("publish", "AC", "0a05746f7069631a320a077061796c6f616410111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962", "8201a5746f7069630283011102d92553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696203c4077061796c6f6164", false),
            ("receptionist-shutdown", "E", "", "80", true),
            ("reconnect-timeout", "AF", "", "80", true),
            ("refresh-contacts", "AD", "", "80", true),
            ("send-local-affinity", "AA", "0a0c2f757365722f776f726b657210011a320a077061796c6f616410111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962", "8301ac2f757365722f776f726b65720283011102d92553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696203c4077061796c6f616403c3", false),
            ("send-to-all", "AB", "0a0c2f757365722f776f726b65721a320a077061796c6f616410111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962", "8201ac2f757365722f776f726b65720283011102d92553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696203c4077061796c6f6164", false),
            ("send", "AA", "0a0c2f757365722f776f726b65721a320a077061796c6f616410111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962", "8301ac2f757365722f776f726b65720283011102d92553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696203c4077061796c6f616403c2", false),
            ("publish-null", "AC", "0a05746f7069631a00", "8201a5746f70696302c0", false),
            ("send-null", "AA", "0a0c2f757365722f776f726b657210011a00", "8301ac2f757365722f776f726b657202c003c3", false),
            ("send-to-all-null", "AB", "0a0c2f757365722f776f726b65721a00", "8201ac2f757365722f776f726b657202c0", false),
        ];
        var cases = 0;
        var nativeWrites = 0;
        var userPayloads = 0;
        var rejected = 0;
        try
        {
            Require(!RuntimeFeature.IsDynamicCodeSupported, "Native execution is required.");
            Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
                "Dynamic type loading must be explicitly disabled.");
            foreach (var mode in new[] { (Name: "off", Id: 15), (Name: "on", Id: 55), (Name: "pinned", Id: 15) })
            {
                var settings = $"akka.actor.serialization-v2 = {(mode.Name == "off" ? "off" : "on")}\nakka.cluster.client.use-legacy-serialization = off";
                if (mode.Name == "pinned")
                    settings += """
        
                        akka.actor.serialization-bindings {
                          "Akka.Cluster.Tools.Client.IClusterClientMessage, Akka.Cluster.Tools" = akka-cluster-client
                          "Akka.Cluster.Tools.Client.IClusterClientProtocolMessage, Akka.Cluster.Tools" = akka-cluster-client
                        }
                        """;
                var watchdog = new LogWatchdogFilter();
                var setup = ActorSystemSetup.Create(
                    BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(settings)),
                    new LogFilterSetup([watchdog]),
                    SerializationSetup.Create(system => ImmutableHashSet.Create(SerializerDetails.Create(
                        "client-native-payload", new NativePayloadSerializer(system), ImmutableHashSet.Create(typeof(NativePayload))))));
                var system = ActorSystem.Create($"client-native-{mode.Name}", setup);
                try
                {
                    ClusterClientSettings.Create(system);
                    Require(!system.Settings.Config.GetBoolean("akka.cluster.client.use-legacy-serialization"), "Client protocol binding was removed.");
                    Require(system.Settings.Config.GetBoolean("akka.actor.serialization-v2") == (mode.Name != "off"), "Wrong global setting.");
                    watchdog.ThrowIfAnyProblems(mode.Name, "startup");
                    foreach (var sample in samples)
                    {
                        var legacy = system.Serialization.Deserialize(Convert.FromHexString(sample.LegacyHex), 15, sample.Manifest);
                        var v2 = system.Serialization.Deserialize(Convert.FromHexString(sample.V2Hex), 55, sample.Manifest);
                        Require(Equals(legacy, v2), $"{mode.Name}/{sample.Name}: readers disagree.");
                        if (sample.Singleton)
                            Require(ReferenceEquals(legacy, v2), $"{sample.Name}: original singleton identity lost.");
                        var writer = system.Serialization.FindSerializerFor(legacy);
                        Require(writer.Identifier == mode.Id, $"{mode.Name}/{sample.Name}: wrong writer id.");
                        Require(AkkaSerialization.ManifestFor(writer, legacy) == sample.Manifest, $"{sample.Name}: wrong manifest.");
                        var expected = Convert.FromHexString(mode.Id == 15 ? sample.LegacyHex : sample.V2Hex);
                        var bytes = writer.ToBinary(legacy);
                        Require(bytes.AsSpan().SequenceEqual(expected), $"{mode.Name}/{sample.Name}: frozen bytes differ.");
                        Require(Equals(system.Serialization.Deserialize(bytes, mode.Id, sample.Manifest), legacy), $"{sample.Name}: roundtrip differs.");
                        if (mode.Id == 55)
                        {
                            Require(writer is SerializerV2, "The V2 row did not provide native buffer writes.");
                            var native = (SerializerV2)writer;
                            var buffer = new ArrayBufferWriter<byte>();
                            Require(native.Serialize(legacy, buffer) == expected.Length && buffer.WrittenCount == expected.Length,
                                $"{sample.Name}: incorrect native byte count.");
                            Require(buffer.WrittenSpan.SequenceEqual(expected), $"{sample.Name}: native bytes differ.");
                            Require(Equals(native.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), sample.Manifest), legacy),
                                $"{sample.Name}: native read differs.");
                            nativeWrites++;
                        }
                        cases++;
                    }
                    var payload = new NativePayload("registered-user-codec");
                    object[] envelopes = [new ClientProtocol.Send("/user/worker", payload, true),
                        new ClientProtocol.SendToAll("/user/worker", payload), new ClientProtocol.Publish("topic", payload)];
                    foreach (var envelope in envelopes)
                    {
                        var writer = system.Serialization.FindSerializerFor(envelope);
                        Require(writer.Identifier == mode.Id, "User envelope selected wrong writer.");
                        var bytes = writer.ToBinary(envelope);
                        Require(Equals(system.Serialization.Deserialize(bytes, mode.Id, AkkaSerialization.ManifestFor(writer, envelope)), envelope),
                            "Registered native user payload did not roundtrip.");
                        userPayloads++;
                    }
                    Require(system.Serialization.FindSerializerFor(payload).Identifier == 55555, "User payload binding was replaced.");
                    Reject(() => system.Serialization.Deserialize(Array.Empty<byte>(), 15, "unknown"));
                    Reject(() => system.Serialization.Deserialize(new byte[] { 0x80 }, 55, "unknown"));
                    Reject(() => system.Serialization.Deserialize(Array.Empty<byte>(), int.MaxValue, "A"));
                    rejected += 3;
                    watchdog.ThrowIfAnyProblems(mode.Name, "serialization");
                    Console.WriteLine($"[client-native] mode={mode.Name} writer={mode.Id} fixtures=16 user-payloads=3");
                }
                finally
                {
                    await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                }
                watchdog.ThrowIfAnyProblems(mode.Name, "shutdown");
            }
            Require(cases == 48 && nativeWrites == 16 && userPayloads == 9 && rejected == 9,
                $"Incomplete corpus: {cases}/{nativeWrites}/{userPayloads}/{rejected}.");
            Console.WriteLine("[client-native] OK cases=48 native-writes=16 user-payloads=9 rejected=9");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[client-native] FAILED: {exception}");
            return 1;
        }
    }
    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
    static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (SerializationException)
        {
            return;
        }
        throw new InvalidOperationException("Expected rejection did not occur.");
    }
    sealed record NativePayload(string Value);
    sealed class NativePayloadSerializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 55555;
        public override string Manifest(object obj) => "p";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = Encoding.UTF8.GetBytes(((NativePayload)obj).Value);
            writer.Write(bytes);
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            if (manifest != "p")
                throw new SerializationException("Unknown native payload manifest.");
            return new NativePayload(Encoding.UTF8.GetString(bytes.ToArray()));
        }
    }
}
