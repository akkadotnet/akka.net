#nullable enable
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Remote.Serialization;
using Akka.Serialization;

Require(!RuntimeFeature.IsDynamicCodeSupported, "Actual native execution required.");
Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading,
    "Dynamic loading must be disabled.");
var reads = 0;
var writes = 0;
var rejected = 0;
var legacyControls = 0;
foreach (var mode in new[] { (Name: "off", Id: 22), (Name: "on", Id: 62), (Name: "pinned", Id: 22) })
{
    var config = $"akka.actor.serialization-v2 = {(mode.Name == "off" ? "off" : "on")}";
    if (mode.Name == "pinned")
        config += "\nakka.actor.serialization-bindings { \"Akka.Dispatch.SysMsg.SystemMessage, Akka\" = akka-system-msg }";
    var watchdog = new LogWatchdogFilter();
    var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(config)),
        new LogFilterSetup([watchdog]));
    var system = ActorSystem.Create("system-native-" + mode.Name, setup);
    try
    {
        // Public legacy constructor anchors the module. No private codecs, friend identity or
        // blanket roots participate in this ordinary consumer.
        var old = new SystemMessageSerializer((ExtendedActorSystem)system);
        Require(old.Identifier == 22, "Legacy id changed.");
        var legacy = old;
        // The public fallback-alias overload resolves a read-only row without changing
        // SystemMessage bindings, using an otherwise unbound consumer marker type.
        var modern = system.Serialization.FindSerializerForType(typeof(UnboundMarker), "akka-system-msg-v2");
        Require(modern.Identifier == 62, "Native read-only alias is not registered.");
        watchdog.ThrowIfAnyProblems(mode.Name, "startup");
        foreach (var value in Cases(system.DeadLetters))
        {
            var manifest = $"Akka.Dispatch.SysMsg.{value.GetType().Name}, Akka";
            // Every one of the 18 original samples exercises the registered native reader.
            var modernBytes = modern.ToBinary(value);
            Compare(value, system.Serialization.Deserialize(modernBytes, 62, manifest), false);
            reads++;
            // The unchanged legacy exception reader needs reflective constructor metadata.
            // Its separately retained native failure is not counted as supported coverage.
            if (!HasCause(value))
            {
                Compare(value, system.Serialization.Deserialize(legacy.ToBinary(value), 22, manifest), true);
                legacyControls++;
            }
            var writer = system.Serialization.FindSerializerFor(value);
            Require(writer.Identifier == mode.Id, "Wrong selected writer.");
            Require(Akka.Serialization.Serialization.ManifestFor(writer, value) == manifest, "Manifest changed.");
            var selectedBytes = writer.ToBinary(value);
            if (writer.Identifier == 62 || !HasCause(value))
                Compare(value, system.Serialization.Deserialize(selectedBytes, writer.Identifier, manifest), writer.Identifier == 22);
            if (writer is SerializerV2 native)
            {
                var buffer = new ArrayBufferWriter<byte>();
                Require(native.Serialize(value, buffer) == selectedBytes.Length, "Native byte count differs.");
                Require(buffer.WrittenSpan.SequenceEqual(selectedBytes), "Native output differs.");
                Compare(value, native.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), manifest), false);
            }
            writes++;
        }
        var modernWriter = (SerializerV2)modern;
        object[] invalid = [new NoMessage(), new DerivedWatch((IInternalActorRef)system.DeadLetters, (IInternalActorRef)system.DeadLetters), new Recreate(new UnknownException()), new Resume(new UnknownException()),
            new Failed(system.DeadLetters, new UnknownException(), -1),
            new Create(new ActorInitializationException(system.DeadLetters, "unknown", new UnknownException()))];
        foreach (var value in invalid)
        {
            var buffer = new ArrayBufferWriter<byte>();
            try
            {
                modernWriter.Serialize(value, buffer);
                throw new InvalidOperationException("Expected unsupported state rejection.");
            }
            catch (Exception error) when (error is ArgumentException or SerializationException)
            {
                Require(buffer.WrittenCount == 0, "Unsupported state partially wrote caller output.");
                rejected++;
            }
        }
        watchdog.ThrowIfAnyProblems(mode.Name, "serialization");
    }
    finally
    {
        await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
    }
    watchdog.ThrowIfAnyProblems(mode.Name, "shutdown");
}
Console.WriteLine($"[system-native] BOUNDED codec proof: native-cases={reads} legacy-control-reads={legacyControls} selected-writer-checks={writes} rejected={rejected} dynamic-code=false; legacy-exception-native-reads=UNSUPPORTED");

static SystemMessage[] Cases(IActorRef actor) =>
[
    new Create(), new Create(new ActorInitializationException(actor, "initialization")),
    new Recreate(null!), new Recreate(new InvalidOperationException("restart", new TimeoutException("inner"))),
    new Suspend(), new Resume(null!), new Resume(new InvalidOperationException("resume")), new Terminate(),
    new Supervise(actor, false), new Supervise(actor, true), new Watch((IInternalActorRef)actor, (IInternalActorRef)actor),
    new Unwatch((IInternalActorRef)actor, (IInternalActorRef)actor), new Failed(actor, new InvalidOperationException("failed"), long.MinValue),
    new Failed(actor, null!, long.MaxValue), new DeathWatchNotification(actor, false, false),
    new DeathWatchNotification(actor, false, true), new DeathWatchNotification(actor, true, false), new DeathWatchNotification(actor, true, true)
];
static bool HasCause(object value) => GetCause(value) is not null;
static Exception? GetCause(object value) => value switch
{
    Create m => m.Failure, Recreate m => m.Cause, Resume m => m.CausedByFailure, Failed m => m.Cause, _ => null
};
static void Compare(object expected, object actual, bool legacy)
{
    Require(expected.GetType() == actual.GetType(), "Runtime type differs.");
    switch (expected)
    {
        case Create v: CompareException(v.Failure, ((Create)actual).Failure, legacy); break;
        case Recreate v: CompareException(v.Cause, ((Recreate)actual).Cause, legacy); break;
        case Resume v: CompareException(v.CausedByFailure, ((Resume)actual).CausedByFailure, legacy); break;
        case Supervise v:
            Require(v.Child.Equals(((Supervise)actual).Child) && v.Async == ((Supervise)actual).Async, "Supervise differs."); break;
        case Watch v:
            Require(v.Watchee.Equals(((Watch)actual).Watchee) && v.Watcher.Equals(((Watch)actual).Watcher), "Watch differs."); break;
        case Unwatch v:
            Require(v.Watchee.Equals(((Unwatch)actual).Watchee) && v.Watcher.Equals(((Unwatch)actual).Watcher), "Unwatch differs."); break;
        case Failed v:
            var f = (Failed)actual;
            Require(v.Child.Equals(f.Child) && v.Uid == f.Uid, "Failed actor/UID differs."); CompareException(v.Cause, f.Cause, legacy); break;
        case DeathWatchNotification v:
            var d = (DeathWatchNotification)actual;
            Require(v.Actor.Equals(d.Actor) && v.ExistenceConfirmed == d.ExistenceConfirmed && v.AddressTerminated == d.AddressTerminated,
                "DeathWatch flags/ref differ."); break;
        case Suspend or Terminate: break;
        default: throw new InvalidOperationException("Missing comparison family.");
    }
}
static void CompareException(Exception? expected, Exception? actual, bool legacy)
{
    if (expected is null) { Require(actual is null, "Expected null cause."); return; }
    Require(actual is not null && expected.GetType() == actual.GetType(), $"Exception type differs: expected={expected.GetType().FullName}, actual={actual?.GetType().FullName}, message={actual?.Message}.");
    Require(expected.Message == actual!.Message && expected.Source == actual.Source && expected.StackTrace == actual.StackTrace,
        "Exception message/source/stack differs.");
    Require(actual.HResult == (legacy ? int.MinValue : expected.HResult), "Exception HResult differs.");
    Require(actual.HelpLink == (legacy ? "" : expected.HelpLink), "Exception HelpLink differs.");
    Require(actual.Data.Count == (legacy ? 0 : expected.Data.Count), "Exception Data count differs.");
    if (!legacy)
        foreach (var key in expected.Data.Keys) Require(Equals(expected.Data[key], actual.Data[key]), "Exception Data differs.");
    if (expected is ActorInitializationException a)
        Require(a.Actor.Equals(((ActorInitializationException)actual).Actor), "Initialization actor differs.");
    CompareException(expected.InnerException, actual.InnerException, legacy);
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
sealed class UnknownException : Exception;

sealed class UnboundMarker;

sealed class DerivedWatch(IInternalActorRef watchee, IInternalActorRef watcher) : Watch(watchee, watcher);
