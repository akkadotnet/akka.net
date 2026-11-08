#nullable enable
using System.Buffers;
using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.Shared;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Remote;
using Akka.Remote.Routing;
using Akka.Remote.Serialization;
using Akka.Routing;
using Akka.Serialization;

Require(!RuntimeFeature.IsDynamicCodeSupported, "Actual native execution is required.");
Require(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var dynamicLoading) && !dynamicLoading, "Dynamic loading must be disabled.");
var nativeCases = 0;
var legacyControls = 0;
var writerChecks = 0;
var rejected = 0;
var lateCalls = 0;
var lateBytes = 0;
foreach (var mode in new[] { (Name: "off", Id: 16), (Name: "on", Id: 56), (Name: "pinned", Id: 16) })
{
    var config = $"akka.actor.serialization-v2 = {(mode.Name == "off" ? "off" : "on")}";
    if (mode.Name == "pinned")
    {
        Type[] bindings = [typeof(Identify), typeof(ActorIdentity), typeof(IActorRef), typeof(PoisonPill), typeof(IntentionalRestart),
            typeof(Kill), typeof(Status.Failure), typeof(Status.Success), typeof(RemoteScope), typeof(FromConfig), typeof(DefaultResizer),
            typeof(RoundRobinPool), typeof(BroadcastPool), typeof(RandomPool), typeof(ScatterGatherFirstCompletedPool), typeof(TailChoppingPool),
            typeof(ConsistentHashingPool), typeof(Config), typeof(RemoteWatcher.Heartbeat), typeof(RemoteWatcher.HeartbeatRsp), typeof(RemoteRouterConfig)];
        config += "\nakka.actor.serialization-bindings {\n" + string.Join("\n", bindings.Select(type =>
            $"\"{type.FullName}, {type.Assembly.GetName().Name}\" = akka-misc")) + "\n}";
    }
    LatePayloadSerializer? late = null;
    var watchdog = new LogWatchdogFilter();
    var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(config)), new LogFilterSetup([watchdog]),
        SerializationSetup.Create(node => ImmutableHashSet.Create(SerializerDetails.Create("late-native-payload", late = new LatePayloadSerializer(node),
            ImmutableHashSet.Create(typeof(UnsupportedPayload), typeof(UnsupportedResizer))))));
    var system = ActorSystem.Create("misc-native-" + mode.Name, setup);
    try
    {
        // Ordinary public API anchors the module; no private codec or assembly roots.
        var legacy = new MiscMessageSerializer((ExtendedActorSystem)system);
        Require(legacy.Identifier == 16, "Legacy id changed.");
        var modern = system.Serialization.FindSerializerForType(typeof(UnboundMarker), "akka-misc-v2") as SerializerV2
            ?? throw new InvalidOperationException("Registered native row not found.");
        Require(modern.Identifier == 56, "Native id changed.");
        watchdog.ThrowIfAnyProblems(mode.Name, "startup");
        var samples = BuildCases(system).Concat(RichCases(system)).Append(Sample(LocalScope.Instance, "LS", "local-scope"))
            .Append(Sample(new FromConfig(null!, Pool.DefaultSupervisorStrategy, ""), "FC", "from-config-empty-dispatcher")).ToArray();
        Require(samples.Length == 46, "Incomplete manifest corpus.");
        foreach (var sample in samples)
        {
            Require(modern.Manifest(sample.Message) == sample.Manifest, "Native manifest differs.");
            var bytes = modern.ToBinary(sample.Message);
            var actual = system.Serialization.Deserialize(bytes, 56, sample.Manifest);
            CompareSample(sample.Message, actual, system);
            var buffer = new ArrayBufferWriter<byte>();
            Require(modern.Serialize(sample.Message, buffer) == bytes.Length && buffer.WrittenCount == bytes.Length, "Native byte count differs.");
            Require(buffer.WrittenSpan.SequenceEqual(bytes), "Native bytes differ.");
            CompareSample(sample.Message, modern.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), sample.Manifest), system);
            nativeCases++;
            if (sample.Message is not Status.Failure { Cause: not null })
            {
                CompareSample(sample.Message, system.Serialization.Deserialize(legacy.ToBinary(sample.Message), 16, sample.Manifest), system);
                legacyControls++;
            }
            if (sample.Message is LocalScope)
            {
                try { system.Serialization.FindSerializerFor(LocalScope.Instance); throw new InvalidOperationException("LocalScope unexpectedly acquired a native default binding."); }
                catch (SerializationException) { rejected++; }
                continue;
            }
            var writer = system.Serialization.FindSerializerFor(sample.Message);
            Require(writer.Identifier == mode.Id, "Selected writer differs.");
            Require(Akka.Serialization.Serialization.ManifestFor(writer, sample.Message) == sample.Manifest, "Selected manifest differs.");
            var selectedBytes = writer.ToBinary(sample.Message);
            if (mode.Id == 56)
                CompareSample(sample.Message, system.Serialization.Deserialize(selectedBytes, mode.Id, sample.Manifest), system);
            else
                Require(selectedBytes.AsSpan().SequenceEqual(legacy.ToBinary(sample.Message)), "Legacy selected writer bytes differ.");
            writerChecks++;
        }
        var invalid = UnsupportedMessages(system.DeadLetters);
        Require(invalid.Length == 13, "Incomplete unsupported-state corpus.");
        foreach (var value in invalid.Append(new Status.Success(new UnregisteredState())))
        {
            var buffer = new ArrayBufferWriter<byte>();
            try { modern.Serialize(value, buffer); throw new InvalidOperationException("Unsupported state was accepted."); }
            catch (SerializationException) { Require(buffer.WrittenCount == 0, "Failure partially wrote caller output."); rejected++; }
        }
        var writerEvidence = late ?? throw new InvalidOperationException("Native payload writer was not registered.");
        Require(writerEvidence.Calls == 12 && writerEvidence.Written == 12 * 8192, "Late payload sensitivity checks did not all execute.");
        lateCalls += writerEvidence.Calls;
        lateBytes += writerEvidence.Written;
        watchdog.ThrowIfAnyProblems(mode.Name, "serialization");
    }
    finally { await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10)); }
    watchdog.ThrowIfAnyProblems(mode.Name, "shutdown");
}
Require(nativeCases == 138 && legacyControls == 132 && writerChecks == 135 && rejected == 45, "Coverage counters differ.");
Console.WriteLine($"[misc-native] BOUNDED codec proof: native-cases={nativeCases} legacy-control-reads={legacyControls} selected-writer-checks={writerChecks} rejected={rejected} late-payload-calls={lateCalls} late-payload-bytes={lateBytes}; legacy-exception-native-reads=UNSUPPORTED");

static WireSample Sample(object message, string manifest, string name) => new(message, manifest, name);
static WireSample[] BuildCases(ActorSystem system)
    {
        var actor = system.DeadLetters;
        var resizer = new DefaultResizer(2, 10, 3, 0.4, 0.2, 0.1, 17);
        var node = new Address("akka.tcp", "other-system", "127.0.0.1", 2552);
        return
        [
            Sample(new Identify("hello"), "ID", "identify-string"),
            Sample(new Identify(int.MinValue), "ID", "identify-int"),
            Sample(new Identify(long.MaxValue), "ID", "identify-long"),
            Sample(new Identify(null!), "ID", "identify-null"),
            Sample(new Identify(new byte[] { 0, 1, 127, 255 }), "ID", "identify-bytes"),
            Sample(new ActorIdentity("id", actor), "AID", "actor-identity"),
            Sample(new ActorIdentity(null!, actor), "AID", "actor-identity-null-correlation"),
            Sample(new ActorIdentity("id", null!), "AID", "actor-identity-null-subject"),
            Sample(actor, "AR", "actor-ref"),
            Sample(ActorRefs.Nobody, "AR", "nobody"),
            Sample(PoisonPill.Instance, "PP", "poison-pill"),
            Sample(Kill.Instance, "K", "kill"),
            Sample(IntentionalRestart.Instance, "IR", "intentional-restart"),
            Sample(RemoteWatcher.Heartbeat.Instance, "RWHB", "heartbeat"),
            Sample(new RemoteWatcher.HeartbeatRsp(long.MinValue), "RWHR", "heartbeat-negative-uid"),
            Sample(new RemoteWatcher.HeartbeatRsp(long.MaxValue), "RWHR", "heartbeat-positive-uid"),
            Sample(new RemoteScope(node), "RS", "remote-scope"),
            Sample(new RemoteScope(new Address("akka", "ipv6-system", "::1", null)), "RS", "remote-scope-null-port"),
            Sample(Config.Empty, "CF", "config-empty"),
            Sample(ConfigurationFactory.ParseString("nested { value = 42, text = hello }"), "CF", "config"),
            Sample(FromConfig.Instance, "FC", "from-config-singleton"),
            Sample(new FromConfig(), "FC", "from-config-default-instance"),
            Sample(FromConfig.Instance.WithResizer(resizer).WithDispatcher("custom-dispatcher"), "FC", "from-config-resizer"),
            Sample(resizer, "DR", "default-resizer"),
            Sample(new RoundRobinPool(3), "RORRP", "round-robin"),
            Sample(new RoundRobinPool(5, resizer, Pool.DefaultSupervisorStrategy, "custom-dispatcher", true), "RORRP", "round-robin-options"),
            Sample(new BroadcastPool(3), "ROBP", "broadcast"),
            Sample(new BroadcastPool(5, resizer, Pool.DefaultSupervisorStrategy, "custom-dispatcher", true), "ROBP", "broadcast-options"),
            Sample(new RandomPool(3), "RORP", "random"),
            Sample(new RandomPool(5, resizer, Pool.DefaultSupervisorStrategy, "custom-dispatcher", true), "RORP", "random-options"),
            Sample(new ScatterGatherFirstCompletedPool(3, TimeSpan.FromTicks(12345678)), "ROSGP", "scatter-gather"),
            Sample(new ScatterGatherFirstCompletedPool(5, resizer, TimeSpan.FromTicks(12345678), Pool.DefaultSupervisorStrategy,
                "custom-dispatcher", true), "ROSGP", "scatter-gather-options"),
            Sample(new TailChoppingPool(3, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(17)), "ROTCP", "tail-chopping"),
            Sample(new TailChoppingPool(5, resizer, Pool.DefaultSupervisorStrategy, "custom-dispatcher", TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(17), true), "ROTCP", "tail-chopping-options"),
            Sample(new ConsistentHashingPool(3), "ROCHP", "consistent-hashing"),
            Sample(new ConsistentHashingPool(5, resizer, Pool.DefaultSupervisorStrategy, "custom-dispatcher", true), "ROCHP", "consistent-hashing-options"),
            Sample(new RemoteRouterConfig(new RoundRobinPool(5), [node,
                new Address("akka", "second-system", "::1", null)]), "RORRC", "remote-router"),
            Sample(new Status.Failure(null!), "STF", "failure-null"),
            Sample(new Status.Failure(null!, "state"), "STF", "failure-null-cause-state"),
            Sample(new Status.Success(null!), "STS", "success-null"),
            Sample(new Status.Success("state"), "STS", "success-string"),
            Sample(new Status.Success(new byte[] { 0, 1, 127, 255 }), "STS", "success-bytes")
        ];
    }

    static void Compare(object expected, object actual, ActorSystem system)
    {
        Equal(expected.GetType(), actual.GetType());
        switch (expected)
        {
            case Identify v:
                AssertPayload(v.MessageId, ((Identify)actual).MessageId);
                break;
            case ActorIdentity v:
                AssertPayload(v.MessageId, ((ActorIdentity)actual).MessageId);
                Equal(v.Subject, ((ActorIdentity)actual).Subject);
                break;
            case IActorRef v:
                Equal(v, actual);
                break;
            case RemoteWatcher.HeartbeatRsp v:
                Equal(v.AddressUid, ((RemoteWatcher.HeartbeatRsp)actual).AddressUid);
                break;
            case RemoteScope v:
                Equal(v.Address, ((RemoteScope)actual).Address);
                break;
            case Config v:
                Equal(v.IsEmpty, ((Config)actual).IsEmpty);
                if (!v.IsEmpty) Equal(v.Root.ToString(), ((Config)actual).Root.ToString());
                break;
            case DefaultResizer v:
                AssertResizer(v, (Resizer)actual);
                break;
            case RemoteRouterConfig v:
                AssertPool(v, (Pool)actual);
                var expectedView = As<RemoteRouterConfig.RemoteRouterConfigSurrogate>(v.ToSurrogate(system));
                var actualView = As<RemoteRouterConfig.RemoteRouterConfigSurrogate>(((RemoteRouterConfig)actual).ToSurrogate(system));
                AssertPool(expectedView.Local, actualView.Local);
                Equal(expectedView.Nodes, actualView.Nodes);
                break;
            case Pool v:
                AssertPool(v, (Pool)actual);
                if (v is FromConfig)
                    Equal(ReferenceEquals(v, FromConfig.Instance), ReferenceEquals(actual, FromConfig.Instance));
                break;
            case Status.Failure v:
                RequireNull(v.Cause);
                RequireNull(((Status.Failure)actual).Cause);
                AssertPayload(v.State, ((Status.Failure)actual).State);
                break;
            case Status.Success v:
                AssertPayload(v.Status, ((Status.Success)actual).Status);
                break;
            case PoisonPill or Kill or IntentionalRestart or RemoteWatcher.Heartbeat or LocalScope:
                break;
            default:
                throw new ArgumentException($"Missing assertion family [{expected.GetType()}].");
        }
    }

    static void AssertPool(Pool expected, Pool actual)
    {
        Equal(expected.GetType(), actual.GetType());
        Equal(expected.NrOfInstances, actual.NrOfInstances);
        Equal(expected.RouterDispatcher, actual.RouterDispatcher);
        Equal(expected.UsePoolDispatcher, actual.UsePoolDispatcher);
        Same(Pool.DefaultSupervisorStrategy, actual.SupervisorStrategy);
        AssertResizer(expected.Resizer, actual.Resizer);
        if (expected is ScatterGatherFirstCompletedPool scatter)
            Equal(scatter.Within, ((ScatterGatherFirstCompletedPool)actual).Within);
        if (expected is TailChoppingPool tail)
        {
            Equal(tail.Within, ((TailChoppingPool)actual).Within);
            Equal(tail.Interval, ((TailChoppingPool)actual).Interval);
        }
        if (expected is ConsistentHashingPool hashing)
            Equal(hashing.VirtualNodesFactor, ((ConsistentHashingPool)actual).VirtualNodesFactor);
    }

    static void AssertResizer(Resizer? expected, Resizer? actual)
    {
        if (expected is null) { RequireNull(actual); return; }
        var e = As<DefaultResizer>(expected);
        var a = As<DefaultResizer>(actual);
        Equal((e.LowerBound, e.UpperBound, e.PressureThreshold, e.RampupRate, e.BackoffThreshold, e.BackoffRate, e.MessagesPerResize),
            (a.LowerBound, a.UpperBound, a.PressureThreshold, a.RampupRate, a.BackoffThreshold, a.BackoffRate, a.MessagesPerResize));
    }

    static void AssertPayload(object? expected, object? actual)
    {
        if (expected is byte[] bytes) Equal(bytes, As<byte[]>(actual));
        else Equal(expected, actual);
    }


static WireSample[] RichCases(ActorSystem system)
{
    var simple = new ApplicationException("application", new InvalidOperationException("inner")) { Source = "fixture-source", HelpLink = "https://example.com/help" };
    simple.Data["state"] = 42;
    var rich = new ActorInitializationException(system.DeadLetters, "initialization", new TimeoutException("inner")) { Source = "fixture-source", HelpLink = "https://example.com/initialization" };
    rich.Data["rich"] = "retained";
    return [Sample(new Status.Failure(simple, "state"), "STF", "failure-original-state"),
        Sample(new Status.Failure(rich, new byte[] { 0, 1, 127, 255 }), "STF", "failure-rich-actor")];
}
static void CompareSample(object expected, object actual, ActorSystem system)
{
    if (expected is IActorRef actor)
    {
        Require(actual is IActorRef, "AR did not decode an actor reference.");
        var decoded = (IActorRef)actual;
        var legacy = new MiscMessageSerializer((ExtendedActorSystem)system);
        var canonical = (IActorRef)system.Serialization.Deserialize(legacy.ToBinary(actor), 16, "AR");
        // Provider-specific implementations can canonicalize during a wire read. Keep exact
        // historical runtime representation plus full actor identity for this AR contract.
        Equal(canonical.GetType(), decoded.GetType());
        Equal(canonical.Path.ToSerializationFormat(), decoded.Path.ToSerializationFormat());
        Equal(canonical.Path.Uid, decoded.Path.Uid);
        Equal(canonical, decoded);
        Equal(actor, decoded);
        if (ReferenceEquals(actor, canonical)) Same(actor, decoded);
    }
    else if (expected is FromConfig { Resizer: null, RouterDispatcher: "" })
    {
        Same(FromConfig.Instance, actual);
        AssertPool(FromConfig.Instance, As<FromConfig>(actual));
    }
    else if (expected is Status.Failure { Cause: not null } failure)
    {
        var decoded = As<Status.Failure>(actual);
        AssertPayload(failure.State, decoded.State);
        CompareException(failure.Cause, decoded.Cause);
    }
    else Compare(expected, actual, system);
}
static void CompareException(Exception? expected, Exception? actual)
{
    if (expected is null) { RequireNull(actual); return; }
    var decoded = actual ?? throw new InvalidOperationException("Cause was lost.");
    Equal(expected.GetType(), decoded.GetType()); Equal(expected.Message, decoded.Message); Equal(expected.Source, decoded.Source);
    Equal(expected.StackTrace, decoded.StackTrace); Equal(expected.HResult, decoded.HResult); Equal(expected.HelpLink, decoded.HelpLink);
    Equal(expected.Data.Count, decoded.Data.Count);
    foreach (var key in expected.Data.Keys) Equal(expected.Data[key], decoded.Data[key]);
    CompareException(expected.InnerException, decoded.InnerException);
    if (expected is ActorInitializationException initialization) Equal(initialization.Actor, ((ActorInitializationException)decoded).Actor);
}
static object[] UnsupportedMessages(IActorRef actor)
{
    var unknown = new UnsupportedPayload(); var resizer = new UnsupportedResizer();
    return [new Identify(unknown), new ActorIdentity(unknown, actor), new Status.Success(unknown), new Status.Failure(new InvalidOperationException("known"), unknown),
        new Status.Failure(new UnsupportedException(), null!), FromConfig.Instance.WithResizer(resizer), new RoundRobinPool(3, resizer),
        new BroadcastPool(3, resizer, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId), new RandomPool(3, resizer, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId),
        new ScatterGatherFirstCompletedPool(3, resizer, TimeSpan.FromSeconds(1), Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId),
        new TailChoppingPool(3, resizer, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)),
        new ConsistentHashingPool(3, resizer, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId),
        new RemoteRouterConfig(new RoundRobinPool(3, resizer), [new Address("akka.tcp", "fixture", "127.0.0.1", 2552)])];
}
static void Equal<T>(T expected, T actual)
{
    if (expected is IEnumerable left && actual is IEnumerable right && expected is not string)
    {
        Require(left.Cast<object?>().SequenceEqual(right.Cast<object?>()), "Sequence fields differ."); return;
    }
    Require(Equals(expected, actual), $"Field differs: expected={expected}, actual={actual}.");
}
static T As<T>(object? value) { Require(value is T && value.GetType() == typeof(T), $"Expected exact {typeof(T)}, got {value?.GetType()}."); return (T)value!; }
static void RequireNull(object? value) => Require(value is null, "Expected null field.");
static void Same(object expected, object actual) => Require(ReferenceEquals(expected, actual), "Singleton reference differs.");
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
sealed record WireSample(object Message, string Manifest, string Name);
sealed class UnboundMarker;
sealed class UnsupportedPayload;
sealed class UnregisteredState;
sealed class UnsupportedException : Exception;
sealed class UnsupportedResizer : Resizer
{
    public override bool IsTimeForResize(long messageCounter) => false;
    public override int Resize(IEnumerable<Routee> currentRoutees) => 0;
}
sealed class LatePayloadSerializer(ExtendedActorSystem system) : SerializerV2(system)
{
    public int Calls { get; private set; }
    public int Written { get; private set; }
    public override int Identifier => 77756;
    public override string Manifest(object obj) => "late-native-payload";
    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        writer.GetSpan(8192)[..8192].Fill(0xac); writer.Advance(8192); Calls++; Written += 8192;
        throw new SerializationException("Intentional failure after writing native payload bytes.");
    }
    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => throw new NotSupportedException("Failing writer fixture.");
}
