//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.AOT.SerializationV2;
using Akka.Event;
using Akka.Configuration;
using Akka.Serialization;
using AkkaSerialization = Akka.Serialization.Serialization;

const int LegacyId = 14;
const int V2Id = 54;
string[] manifests = ["A", "B", "C", "D"];
byte[] v2Bytes = [0x80]; // An AllowEmpty message is a MessagePack empty map; legacy payloads are empty.
var cases = 0;
var nativeWriterCases = 0;
var rejectedCases = 0;
var selectedIds = new HashSet<int>();

try
{
    Require(!RuntimeFeature.IsDynamicCodeSupported, "This probe must run as a native executable.");
    Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
        "Dynamic type loading must be explicitly disabled.");

    (string Name, string Settings, bool Enabled, int WriterId)[] modes =
    [
        ("off", "akka.actor.serialization-v2 = off", false, LegacyId),
        ("on", "akka.actor.serialization-v2 = on", true, V2Id),
        ("pinned", """
            akka.actor.serialization-v2 = on
            akka.actor.serialization-bindings {
                "Akka.Cluster.Tools.Singleton.IClusterSingletonMessage, Akka.Cluster.Tools" = akka-singleton
            }
            """, true, LegacyId)
    ];

    foreach (var mode in modes)
    {
        var config = ConfigurationFactory.ParseString(mode.Settings)
            .WithFallback(ConfigurationFactory.ParseString("akka.actor.provider = local"));
        var watchdog = new LogWatchdogFilter();
        var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), new LogFilterSetup([watchdog]));
        var system = ActorSystem.Create($"singleton-native-{mode.Name}", setup);
        try
        {
            watchdog.ThrowIfAnyProblems(mode.Name, "startup");
            Require(system.Settings.Config.GetBoolean("akka.actor.serialization-v2") == mode.Enabled,
                $"{mode.Name}: the intended switch setting did not take effect.");

            foreach (var manifest in manifests)
            {
                // Both ids must resolve through the real module table. No test assembly, private-type
                // reference, explicit serializer registration, or GetSerializerById reflection is involved.
                var legacyMessage = system.Serialization.Deserialize(Array.Empty<byte>(), LegacyId, manifest);
                var v2Message = system.Serialization.Deserialize(v2Bytes, V2Id, manifest);
                Require(ReferenceEquals(legacyMessage, v2Message),
                    $"{mode.Name}/{manifest}: the two readers did not return the same domain singleton instance.");

                var writer = system.Serialization.FindSerializerFor(legacyMessage);
                Require(writer.Identifier == mode.WriterId,
                    $"{mode.Name}/{manifest}: expected writer {mode.WriterId}, got {writer.Identifier}.");
                selectedIds.Add(writer.Identifier);
                Require(AkkaSerialization.ManifestFor(writer, legacyMessage) == manifest,
                    $"{mode.Name}/{manifest}: the writer changed the manifest.");

                var expectedBytes = mode.WriterId == LegacyId ? Array.Empty<byte>() : v2Bytes;
                var bytes = writer.ToBinary(legacyMessage);
                Require(bytes.AsSpan().SequenceEqual(expectedBytes),
                    $"{mode.Name}/{manifest}: unexpected writer bytes.");
                Require(ReferenceEquals(writer.FromBinary(bytes, manifest), legacyMessage),
                    $"{mode.Name}/{manifest}: the direct reader lost singleton identity.");
                Require(ReferenceEquals(system.Serialization.Deserialize(bytes, writer.Identifier, manifest), legacyMessage),
                    $"{mode.Name}/{manifest}: registered read-back lost singleton identity.");

                if (mode.WriterId == V2Id)
                {
                    Require(writer is SerializerV2, "The V2 row must expose the native buffer-writing contract.");
                    var nativeWriter = (SerializerV2)writer;
                    var buffer = new ArrayBufferWriter<byte>();
                    var written = nativeWriter.Serialize(legacyMessage, buffer);
                    Require(written == buffer.WrittenCount && written == 1,
                        $"{manifest}: native writer reported the wrong byte count.");
                    Require(buffer.WrittenSpan.SequenceEqual(v2Bytes), $"{manifest}: native writer changed the wire bytes.");
                    Require(ReferenceEquals(nativeWriter.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), manifest), legacyMessage),
                        $"{manifest}: native buffer read-back lost singleton identity.");
                    nativeWriterCases++;
                }

                cases++;
            }

            RequireRejected(() => system.Serialization.Deserialize(Array.Empty<byte>(), LegacyId, "unknown"),
                $"{mode.Name}: legacy accepted an unknown manifest.");
            RequireRejected(() => system.Serialization.Deserialize(v2Bytes, V2Id, "unknown"),
                $"{mode.Name}: V2 accepted an unknown manifest.");
            RequireRejected(() => system.Serialization.Deserialize(v2Bytes, int.MaxValue, "A"),
                $"{mode.Name}: an unregistered serializer id was accepted.");
            rejectedCases += 3;
            watchdog.ThrowIfAnyProblems(mode.Name, "serialization");
            Console.WriteLine($"[canary-v2] mode={mode.Name} writer={mode.WriterId} manifests=4");
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
        watchdog.ThrowIfAnyProblems(mode.Name, "shutdown");
    }

    Require(cases == 12 && nativeWriterCases == 4 && rejectedCases == 9,
        $"Incomplete probe: cases={cases}, native-writes={nativeWriterCases}, rejected={rejectedCases}.");
    Require(selectedIds.SetEquals([LegacyId, V2Id]), "The probe did not exercise both distinct serializer ids.");
    var metricsCases = await MetricsProbe.RunAsync();
    var pubSubCases = await PubSubProbe.RunAsync();
    Require(metricsCases == 24 && pubSubCases == 51, "Incomplete module corpus.");
    Require(await ClientProbe.RunAsync() == 0, "Cluster Client native corpus failed.");
    Require(await ContainersProbe.RunAsync() == 0, "Actor-selection native corpus failed.");
    Require(await SnapshotProbe.RunAsync() == 0, "Snapshot native corpus failed.");
    Require(await PersistenceMessageProbe.RunAsync() == 0, "Persistence-message native corpus failed.");
    Require(await StreamsProbe.RunAsync() == 0, "Stream protocol native corpus failed.");
    Console.WriteLine("[canary-v2] OK cases=12 metrics=24 pubsub=51 client=48 containers=30 snapshot=15 persistence=33 streams=36 native-writes=4 rejected=9");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"[canary-v2] FAILED: {exception}");
    return 1;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void RequireRejected(Action action, string message)
{
    try
    {
        action();
    }
    catch (SerializationException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
