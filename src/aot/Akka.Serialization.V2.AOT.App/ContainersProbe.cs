// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Serialization;
using Akka.Serialization;
using AkkaSerialization = Akka.Serialization.Serialization;


namespace Akka.AOT.SerializationV2;

internal static class ContainersProbe
{
    internal static async Task<int> RunAsync()
    {
        // Frozen fixture data is generated below from the reviewed JIT captures before native publication.
        var fixtures = ContainerFrozenFixtures.Cases;
        var cases = 0;
        var nativeWrites = 0;
        var actorRefPayloads = 0;
        var rejected = 0;
        try
        {
            Require(!RuntimeFeature.IsDynamicCodeSupported, "A native executable is required.");
            Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
                "Dynamic type loading must be explicitly disabled.");
            foreach (var mode in new[] { (Name: "off", Id: 6), (Name: "on", Id: 46), (Name: "pinned", Id: 6) })
            {
                var settings = $"akka.actor.serialization-v2 = {(mode.Name == "off" ? "off" : "on")}";
                if (mode.Name == "pinned")
                    settings += """

                        akka.actor.serialization-bindings {
                          "Akka.Actor.ActorSelectionMessage, Akka" = akka-containers
                        }
                        """;
                var watchdog = new LogWatchdogFilter();
                var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(settings)),
                    new LogFilterSetup([watchdog]));
                var system = ActorSystem.Create("containers-native-" + mode.Name, setup);
                try
                {
                    // Ordinary public legacy API anchors the Remote assembly; no private codec, friend
                    // assembly identity, blanket trimmer roots or reflective bootstrap hooks are used.
                    Require(new MessageContainerSerializer((ExtendedActorSystem)system).Identifier == 6, "Legacy public serializer id changed.");
                    watchdog.ThrowIfAnyProblems(mode.Name, "startup");
                    foreach (var fixture in fixtures)
                    {
                        var expected = Create(fixture.Name);
                        var legacy = system.Serialization.Deserialize(Convert.FromHexString(fixture.LegacyHex), 6, "");
                        var modern = system.Serialization.Deserialize(Convert.FromHexString(fixture.V2Hex), 46, "a");
                        Compare(expected, legacy);
                        Compare(expected, modern);
                        var writer = system.Serialization.FindSerializerFor(expected);
                        Require(writer.Identifier == mode.Id, $"{mode.Name}/{fixture.Name}: wrong writer.");
                        var manifest = AkkaSerialization.ManifestFor(writer, expected);
                        Require(manifest == (mode.Id == 6 ? "" : "a"), "Writer used the wrong manifest contract.");
                        var bytes = writer.ToBinary(expected);
                        Compare(expected, system.Serialization.Deserialize(bytes, mode.Id, manifest));
                        // Global-on also moves the nested selection's own writer. Every case still
                        // round-trips; nonnested cases additionally retain exact frozen writer bytes.
                        if (fixture.Name != "nested-selection")
                            Require(bytes.AsSpan().SequenceEqual(Convert.FromHexString(mode.Id == 6 ? fixture.LegacyHex : fixture.V2Hex)), "Frozen writer bytes changed.");
                        if (mode.Id == 46)
                        {
                            var native = writer as SerializerV2 ?? throw new InvalidOperationException("Expected native buffer writes.");
                            var buffer = new ArrayBufferWriter<byte>();
                            Require(native.Serialize(expected, buffer) == bytes.Length && buffer.WrittenCount == bytes.Length, "Native byte count differs.");
                            Require(buffer.WrittenSpan.SequenceEqual(bytes), "Native writer bytes differ.");
                            Compare(expected, native.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), "a"));
                            nativeWrites++;
                        }
                        cases++;
                    }

                    foreach (var actorRef in new IActorRef[] { ActorRefs.Nobody, system.DeadLetters })
                    {
                        var wrapper = new ActorSelectionMessage(actorRef, [new SelectChildName("refs")]);
                        var writer = system.Serialization.FindSerializerFor(wrapper);
                        Require(writer.Identifier == mode.Id, "Actor-ref wrapper binding changed.");
                        var expectedPayloadId = mode.Name == "off" ? 16 : 56;
                        Require(system.Serialization.FindSerializerFor(actorRef).Identifier == expectedPayloadId,
                            "Actor-ref payload must follow its own Misc binding, independently of the container pin.");
                        Compare(wrapper, system.Serialization.Deserialize(writer.ToBinary(wrapper), mode.Id, AkkaSerialization.ManifestFor(writer, wrapper)));
                        actorRefPayloads++;
                    }

                    // All modes read the new true flag; only the V2 writer can preserve it.
                    var fanOut = new ActorSelectionMessage("hello", [new SelectChildPattern("worker-*")], true);
                    var patternFixture = fixtures.Single(f => f.Name == "child-pattern");
                    var trueBytes = Convert.FromHexString(patternFixture.V2Hex);
                    Require(trueBytes[^1] == 0xc2, "Expected the frozen false flag.");
                    trueBytes[^1] = 0xc3;
                    Compare(fanOut, system.Serialization.Deserialize(trueBytes, 46, "a"));
                    if (mode.Id == 46)
                    {
                        var writer = (SerializerV2)system.Serialization.FindSerializerFor(fanOut);
                        Require(writer.ToBinary(fanOut).AsSpan().SequenceEqual(trueBytes), "V2 fan-out field changed.");
                        var buffer = new ArrayBufferWriter<byte>();
                        var recursive = new ActorSelectionMessage("recursive", [new SelectChildName("user"), SelectChildRecursive.Instance]);
                        try
                        {
                            writer.Serialize(recursive, buffer);
                            throw new InvalidOperationException("Unsupported recursive selector was accepted.");
                        }
                        catch (ArgumentException)
                        {
                            Require(buffer.WrittenCount == 0, "Unsupported recursive selector partially wrote output.");
                            rejected++;
                        }
                    }
                    Reject(() => system.Serialization.Deserialize(Convert.FromHexString(fixtures[0].V2Hex), 46, ""));
                    Reject(() => system.Serialization.Deserialize(Array.Empty<byte>(), int.MaxValue, "a"));
                    rejected += 2;
                    watchdog.ThrowIfAnyProblems(mode.Name, "serialization");
                    Console.WriteLine($"[containers-native] mode={mode.Name} writer={mode.Id} fixtures=10 actorref-payloads=2 flag-read=true");
                }
                finally
                {
                    await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
                }
                watchdog.ThrowIfAnyProblems(mode.Name, "shutdown");
            }
            Require(cases == 30 && nativeWrites == 10 && actorRefPayloads == 6 && rejected == 7, "Incomplete native corpus.");
            Console.WriteLine("[containers-native] OK cases=30 native-writes=10 actorref-payloads=6 rejected=7");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("[containers-native] FAILED: " + error);
            return 1;
        }

        static ActorSelectionMessage Create(string name) => name switch
        {
            "empty-path" => new ActorSelectionMessage("hello", []),
            "null-payload" => new ActorSelectionMessage(null!, []),
            "empty-payload" => new ActorSelectionMessage("", [new SelectChildName("user")]),
            "child-names" => new ActorSelectionMessage("hello", [new SelectChildName("user"), new SelectChildName("target")]),
            "child-pattern" => new ActorSelectionMessage("hello", [new SelectChildPattern("worker-*")]),
            "parent" => new ActorSelectionMessage("hello", [new SelectParent()]),
            "mixed-path" => new ActorSelectionMessage("hello", [new SelectChildName("user"), new SelectChildName("target"), new SelectParent(), new SelectChildPattern("worker-*"), new SelectChildName("leaf")]),
            "binary-payload" => new ActorSelectionMessage(new byte[] { 0, 1, 127, 255 }, [new SelectChildName("bytes")]),
            "integer-payload" => new ActorSelectionMessage(123456, [new SelectChildName("integer")]),
            "nested-selection" => new ActorSelectionMessage(new ActorSelectionMessage("inner", [new SelectChildName("nested")]), [new SelectChildName("outer")]),
            _ => throw new InvalidOperationException("Unknown fixture " + name)
        };
    }
    static void Compare(ActorSelectionMessage expected, object actual)
    {
        Require(actual is ActorSelectionMessage, "Wrong container runtime type.");
        var selection = (ActorSelectionMessage)actual;
        Require(expected.WildCardFanOut == selection.WildCardFanOut, "Wildcard flag differs.");
        Require(expected.Elements.Length == selection.Elements.Length, "Selection element count differs.");
        for (var index = 0; index < expected.Elements.Length; index++)
            Require(expected.Elements[index].Equals(selection.Elements[index]), "Selection element differs.");
        if (expected.Message is ActorSelectionMessage nested)
            Compare(nested, selection.Message);
        else if (expected.Message is byte[] bytes)
            Require(selection.Message is byte[] restored && bytes.AsSpan().SequenceEqual(restored), "Byte payload differs.");
        else
            Require(Equals(expected.Message, selection.Message), "Nested payload differs.");
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
        throw new InvalidOperationException("Expected serialization rejection did not occur.");
    }
}
