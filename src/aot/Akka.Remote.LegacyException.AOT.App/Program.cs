// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote.Serialization;
using Akka.Serialization;

// Frozen original records from ExceptionSupport git blob ab1ac13716307407ad93b85c807c55ee07388e28.
// Expected raw fields come from those protobuf records; decorated BCL messages use the
// public getters to retain the old serialization-constructor behavior on this runtime.
var samples = LegacyFixtures.Samples;
var controls = LegacyFixtures.Controls;
var reads = 0;
var rejections = 0;
try
{
    Require(!RuntimeFeature.IsDynamicCodeSupported, "Actual native execution required.");
    Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
        "Dynamic type loading must be explicitly disabled.");
    // Public legacy type references anchor the real module without a friend assembly or roots.
    Require(typeof(SystemMessageSerializer).Assembly == typeof(MiscMessageSerializer).Assembly, "Wrong module reference.");
    foreach (var mode in new[] { "off", "on", "pinned" })
    {
        var config = $"akka.actor.serialization-v2 = {(mode == "off" ? "off" : "on")}";
        if (mode == "pinned") config += """

            akka.actor.serialization-bindings {
                "Akka.Dispatch.SysMsg.SystemMessage, Akka" = akka-system-msg
                "Akka.Actor.Status+Failure, Akka" = akka-misc
            }
            """;
        var watchdog = new LogWatchdogFilter();
        var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(config)),
            new LogFilterSetup([watchdog]));
        var system = ActorSystem.Create("legacy-exception-" + mode, setup);
        try
        {
            watchdog.ThrowIfAnyProblems(mode, "startup");
            foreach (var sample in samples)
            {
                if (sample.Name is "status-OpenCircuitException" or "status-json-state")
                {
                    var field = sample.Name == "status-json-state" ? "OptionalMessage" : "RemainingDuration";
                    Reject(() => Read(system, sample), "field [" + field + "]", "serializer [1]");
                    rejections++;
                    continue;
                }
                Console.WriteLine($"[legacy-exceptions-native] reading={mode}/{sample.Name}");
                var message = Read(system, sample);
                Compare(Cause(message), sample.Expected);
                if (message is Status.Failure failure) Require(failure.State is null, "Null status state changed.");
                reads++;
            }
            foreach (var control in controls)
            {
                Reject(() => system.Serialization.Deserialize(Convert.FromHexString(control.Hex), 16, "STF"),
                    control.Name == "missing-descriptor" ? "no exception type descriptor" : "no native factory", "Akka.DynamicTypeLoading");
                rejections++;
            }
            var empty = (Create)system.Serialization.Deserialize(new byte[] { 0x0a, 0x00 }, 22, "Akka.Dispatch.SysMsg.Create, Akka");
            Require(empty.Failure is null, "The old empty exception sentinel must remain null.");
            watchdog.ThrowIfAnyProblems(mode, "reads");
            Console.WriteLine($"[legacy-exceptions-native] mode={mode} reads=38 rejected=5");
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
        watchdog.ThrowIfAnyProblems(mode, "shutdown");
    }
    var overrides = 0;
    foreach (var protobuf in new[] { true, false })
    {
        var watchdog = new LogWatchdogFilter();
        var setup = ActorSystemSetup.Create(new LogFilterSetup([watchdog]), SerializationSetup.Create(system =>
            ImmutableHashSet.Create(SerializerDetails.Create(protobuf ? "proto" : "json",
                protobuf ? new ProtoFixtureReader(system) : new JsonFixtureReader(system), ImmutableHashSet<Type>.Empty))));
        var system = ActorSystem.Create(protobuf ? "legacy-proto-override" : "legacy-json-override", setup);
        try
        {
            if (protobuf)
            {
                var message = (Status.Failure)Read(system, samples.Single(s => s.Name == "status-PostRestartException"));
                Require(message.Cause is PostRestartException post && post.OriginalCause.Message == "application override",
                    "The actual protobuf subclass reader was bypassed.");
                overrides++;
            }
            else
            {
                var state = (Status.Failure)Read(system, samples.Single(s => s.Name == "status-json-state"));
                Require(state.Cause is PreRestartException pre && Equals(pre.OptionalMessage, new NativeState(17)),
                    "The explicitly registered historical JSON fixture reader was bypassed.");
                var duration = (Status.Failure)Read(system, samples.Single(s => s.Name == "status-OpenCircuitException"));
                Require(duration.Cause is OpenCircuitException open && open.RemainingDuration.Ticks == 1234567,
                    "The old string-valued duration constructor contract changed.");
                overrides += 2;
            }
            watchdog.ThrowIfAnyProblems("override", "reads");
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
        watchdog.ThrowIfAnyProblems("override", "shutdown");
    }
    Require(reads == 114 && rejections == 15 && overrides == 3, $"Incomplete corpus: {reads}/{rejections}/{overrides}.");
    Console.WriteLine("[legacy-exceptions-native] OK reads=114 rejected=15 overrides=3 empty-sentinels=3");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static object Read(ActorSystem system, Sample sample) =>
    system.Serialization.Deserialize(Convert.FromHexString(sample.Hex), sample.Id, sample.Manifest);

static Exception? Cause(object message) => message switch
{
    Status.Failure failure => failure.Cause,
    Create create => create.Failure,
    Recreate recreate => recreate.Cause,
    _ => throw new InvalidOperationException("Wrong packet type.")
};

static void Compare(Exception? actual, Expected? expected)
{
    if (expected is null)
    {
        Require(actual is null, "Unexpected exception cause.");
        return;
    }
    Require(actual is not null, "A historical exception cause was erased.");
    Require(actual!.GetType().FullName == expected.Type, "Exception type changed.");
    var message = expected.Type switch
    {
        "System.ArgumentException" => new ArgumentException(expected.RawMessage, expected.Parameter).Message,
        "System.ArgumentNullException" => new ArgumentNullException(expected.Parameter, expected.RawMessage).Message,
        "System.ArgumentOutOfRangeException" => new ArgumentOutOfRangeException(expected.Parameter, expected.ActualValue, expected.RawMessage).Message,
        _ => expected.RawMessage
    };
    Require(actual.Message == message, "Historical message semantics changed.");
    Require(actual.Source == expected.Source && actual.StackTrace == expected.Stack, "Exception source/stack changed.");
    Require(actual.HResult == int.MinValue && actual.HelpLink == "" && actual.Data.Count == 0,
        "The old format's HResult/Data/HelpLink losses changed.");
    Compare(actual.InnerException, expected.Inner);
    if (actual is ActorInitializationException actor)
        Require(actor.Actor?.Path.ToSerializationFormat() == ActorRefs.Nobody.Path.ToSerializationFormat(), "Actor field changed.");
    if (actual is DeathPactException death)
        Require(death.DeadActor?.Path.ToSerializationFormat() == ActorRefs.Nobody.Path.ToSerializationFormat(), "Dead actor field changed.");
    if (actual is PreRestartException pre)
    {
        Compare(pre.RestartException, expected.Rich);
        Require(Equals(pre.OptionalMessage, "optional"), "Optional message changed.");
    }
    if (actual is PostRestartException post) Compare(post.OriginalCause, expected.Rich);
    if (actual is ArgumentException argument) Require(argument.ParamName == expected.Parameter, "Parameter name changed.");
    if (actual is ArgumentOutOfRangeException range) Require(Equals(range.ActualValue, expected.ActualValue), "Actual value changed.");
    if (actual is FileNotFoundException file) Require(file.FileName == "file" && file.FusionLog is null, "File fields changed.");
}

static void Reject(Action action, string first, string second)
{
    try
    {
        action();
    }
    catch (SerializationException error) when (error.Message.Contains(first) && error.Message.Contains(second))
    {
        return;
    }
    throw new InvalidOperationException("Expected an explicit unsupported legacy exception error.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed record Sample(string Name, int Id, string Manifest, string Hex, Expected Expected);
internal sealed record Expected(string Type, string RawMessage, string? Source, string? Stack, Expected? Inner,
    Expected? Rich, string? Parameter = null, object? ActualValue = null);
internal sealed record NativeState(int Value);

internal sealed class ProtoFixtureReader(ExtendedActorSystem system) : ProtobufSerializer(system)
{
    public override int Identifier => 2;
    public override object FromBinary(byte[] bytes, string manifest)
    {
        if (manifest != "Akka.Remote.Serialization.Proto.Msg.ExceptionData, Akka.Remote" ||
            !bytes.AsSpan().SequenceEqual(Convert.FromHexString(FixtureBytes.OriginalCauseHex)))
            throw new SerializationException("Unexpected protobuf override input.");
        return new Exception("application override");
    }
}

// This explicitly registered test callback recognizes exactly two byte fixtures. It verifies
// application-reader ownership; it is not a JSON parser or a proposed production codec.
internal sealed class JsonFixtureReader(ExtendedActorSystem system) : SerializerWithStringManifest(system)
{
    public override int Identifier => 1;
    public override string Manifest(object obj) => "";
    public override byte[] ToBinary(object obj) => throw new NotSupportedException();
    public override object FromBinary(byte[] bytes, string manifest)
    {
        if (manifest != "") throw new SerializationException("Unexpected historical JSON manifest.");
        if (bytes.AsSpan().SequenceEqual(Convert.FromHexString(FixtureBytes.JsonStateHex))) return new NativeState(17);
        if (bytes.AsSpan().SequenceEqual(Convert.FromHexString(FixtureBytes.JsonDurationHex))) return "00:00:00.1234567";
        throw new SerializationException("Unknown JSON fixture bytes.");
    }
}
