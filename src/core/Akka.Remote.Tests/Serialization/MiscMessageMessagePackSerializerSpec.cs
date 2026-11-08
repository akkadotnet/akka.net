// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Remote.Routing;
using Akka.Remote.Serialization;
using Akka.Routing;
using Akka.Serialization;
using Akka.TestKit;
using VerifyXunit;
using Xunit;

namespace Akka.Remote.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class MiscMessageMessagePackSerializerSpec : V2PortSpec
{
    private static readonly Config PortConfig = ConfigurationFactory.ParseString("""
        akka.actor.provider = remote
        akka.remote.dot-netty.tcp.port = 0
        """);
    private readonly V2PortCase[] _cases;

    public MiscMessageMessagePackSerializerSpec(ITestOutputHelper output) : base(output, PortConfig)
    {
        _cases = BuildCases((ExtendedActorSystem)Sys);
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new MiscMessageSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new MiscMessageMessagePackSerializer(system);
    protected override IReadOnlyList<V2PortCase> Cases => _cases;

    internal static V2PortCase[] BuildCases(ExtendedActorSystem system)
    {
        var actor = system.Provider.ResolveActorRef(
            $"{RARP.For(system).Provider.DefaultAddress.Protocol}://fixture@127.0.0.1:2552/user/child#123");
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

    private static V2PortCase Sample(object message, string manifest, string name)
        => new(message, manifest, name) { AssertEquivalent = AssertMessage };

    internal static void AssertMessage(object expected, object actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        switch (expected)
        {
            case Identify v:
                AssertPayload(v.MessageId, ((Identify)actual).MessageId);
                break;
            case ActorIdentity v:
                AssertPayload(v.MessageId, ((ActorIdentity)actual).MessageId);
                Assert.Equal(v.Subject, ((ActorIdentity)actual).Subject);
                break;
            case IActorRef v:
                Assert.Equal(v, actual);
                break;
            case RemoteWatcher.HeartbeatRsp v:
                Assert.Equal(v.AddressUid, ((RemoteWatcher.HeartbeatRsp)actual).AddressUid);
                break;
            case RemoteScope v:
                Assert.Equal(v.Address, ((RemoteScope)actual).Address);
                break;
            case Config v:
                Assert.Equal(v.IsEmpty, ((Config)actual).IsEmpty);
                if (!v.IsEmpty) Assert.Equal(v.Root.ToString(), ((Config)actual).Root.ToString());
                break;
            case DefaultResizer v:
                AssertResizer(v, (Resizer)actual);
                break;
            case RemoteRouterConfig v:
                AssertPool(v, (Pool)actual);
                var router = (RemoteRouterConfig)actual;
                AssertPool(v.Local, router.Local);
                Assert.Equal(v.Nodes, router.Nodes);
                break;
            case Pool v:
                AssertPool(v, (Pool)actual);
                if (v is FromConfig)
                    Assert.Equal(ReferenceEquals(v, FromConfig.Instance), ReferenceEquals(actual, FromConfig.Instance));
                break;
            case Status.Failure v:
                Assert.Null(v.Cause);
                Assert.Null(((Status.Failure)actual).Cause);
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

    private static void AssertPool(Pool expected, Pool actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.NrOfInstances, actual.NrOfInstances);
        Assert.Equal(expected.RouterDispatcher, actual.RouterDispatcher);
        Assert.Equal(expected.UsePoolDispatcher, actual.UsePoolDispatcher);
        Assert.Same(Pool.DefaultSupervisorStrategy, actual.SupervisorStrategy);
        AssertResizer(expected.Resizer, actual.Resizer);
        if (expected is ScatterGatherFirstCompletedPool scatter)
            Assert.Equal(scatter.Within, ((ScatterGatherFirstCompletedPool)actual).Within);
        if (expected is TailChoppingPool tail)
        {
            Assert.Equal(tail.Within, ((TailChoppingPool)actual).Within);
            Assert.Equal(tail.Interval, ((TailChoppingPool)actual).Interval);
        }
        if (expected is ConsistentHashingPool hashing)
            Assert.Equal(hashing.VirtualNodesFactor, ((ConsistentHashingPool)actual).VirtualNodesFactor);
    }

    private static void AssertResizer(Resizer? expected, Resizer? actual)
    {
        if (expected is null) { Assert.Null(actual); return; }
        var e = Assert.IsType<DefaultResizer>(expected);
        var a = Assert.IsType<DefaultResizer>(actual);
        Assert.Equal((e.LowerBound, e.UpperBound, e.PressureThreshold, e.RampupRate, e.BackoffThreshold, e.BackoffRate, e.MessagesPerResize),
            (a.LowerBound, a.UpperBound, a.PressureThreshold, a.RampupRate, a.BackoffThreshold, a.BackoffRate, a.MessagesPerResize));
    }

    private static void AssertPayload(object? expected, object? actual)
    {
        if (expected is byte[] bytes) Assert.Equal(bytes, Assert.IsType<byte[]>(actual));
        else Assert.Equal(expected, actual);
    }

    [Theory(DisplayName = "Should_RetainLocalScopeCodecWithoutAddingADefaultBinding_When_EitherIdReadsIt")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_RetainLocalScopeCodecWithoutAddingADefaultBinding_When_EitherIdReadsIt(bool native)
    {
        var node = (ExtendedActorSystem)Sys;
        var serializer = native ? (Serializer)CreateV2(node) : CreateLegacy(node);
        var bytes = serializer.ToBinary(LocalScope.Instance);
        Assert.Same(LocalScope.Instance, serializer.FromBinary(bytes, "LS"));
        Assert.DoesNotContain(new RemoteSerializers().Create(node), row => row.UseFor.Contains(typeof(LocalScope)));
        var dump = HexDumpFormatter.Format("local-scope", nameof(LocalScope), "LS", serializer.Identifier, bytes);
        await Verifier.Verify(dump).UseDirectory("Snapshots")
            .UseFileName($"MiscMessageMessagePackSerializerSpec.local-scope.{(native ? "v2" : "legacy")}");
    }

    [Fact(DisplayName = "Should_ReadLocalScopeByBothIds_When_DynamicLoadingIsDisabled")]
    public void Should_ReadLocalScopeByBothIds_When_DynamicLoadingIsDisabled()
    {
        var hadSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var previous);
        AppContext.SetSwitch("Akka.DynamicTypeLoading", false);
        try
        {
            var node = (ExtendedActorSystem)Sys;
            var serialization = new Akka.Serialization.Serialization(node);
            foreach (var serializer in new Serializer[] { CreateLegacy(node), CreateV2(node) })
                Assert.Same(LocalScope.Instance,
                    serialization.Deserialize(serializer.ToBinary(LocalScope.Instance), serializer.Identifier, "LS"));
        }
        finally
        {
            AppContext.SetSwitch("Akka.DynamicTypeLoading", !hadSwitch || previous);
        }
    }

    private V2PortCase[] RichFailures()
    {
        var actor = ((ExtendedActorSystem)Sys).Provider.ResolveActorRef(
            $"{RARP.For(Sys).Provider.DefaultAddress.Protocol}://fixture@127.0.0.1:2552/user/child#123");
        var simple = new ApplicationException("application", new InvalidOperationException("inner"))
        {
            Source = "fixture-source", HelpLink = "https://example.com/help"
        };
        simple.Data["state"] = 42;
        var rich = new ActorInitializationException(actor, "initialization", new TimeoutException("inner"))
        {
            Source = "fixture-source", HelpLink = "https://example.com/initialization"
        };
        rich.Data["rich"] = "retained";
        return
        [
            new V2PortCase(new Status.Failure(simple, "state"), "STF", "failure-original-state"),
            new V2PortCase(new Status.Failure(rich, new byte[] { 0, 1, 127, 255 }), "STF", "failure-rich-actor")
        ];
    }

    [Theory(DisplayName = "Should_AssertHistoricalLossAndFullNativeState_When_StatusFailureUsesEitherWriter")]
    [InlineData(false, "failure-original-state")]
    [InlineData(false, "failure-rich-actor")]
    [InlineData(true, "failure-original-state")]
    [InlineData(true, "failure-rich-actor")]
    public async Task Should_AssertHistoricalLossAndFullNativeState_When_StatusFailureUsesEitherWriter(bool native, string name)
    {
        var sample = Assert.Single(RichFailures(), c => c.Name == name);
        var serializer = native ? (Serializer)CreateV2((ExtendedActorSystem)Sys) : CreateLegacy((ExtendedActorSystem)Sys);
        var bytes = serializer.ToBinary(sample.Message);
        AssertFailure((Status.Failure)sample.Message, Assert.IsType<Status.Failure>(serializer.FromBinary(bytes, "STF")), legacy: !native);
        var dump = HexDumpFormatter.Format(sample.Name, "Failure", "STF", serializer.Identifier, bytes);
        await Verifier.Verify(dump).UseDirectory("Snapshots")
            .UseFileName($"MiscMessageMessagePackSerializerSpec.{sample.Name}.{(native ? "v2" : "legacy")}");
    }

    private static void AssertFailure(Status.Failure expected, Status.Failure actual, bool legacy)
    {
        AssertPayload(expected.State, actual.State);
        AssertException(expected.Cause, actual.Cause, legacy);
    }

    private static void AssertException(Exception? expected, Exception? actual, bool legacy)
    {
        if (expected is null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(expected.StackTrace, actual.StackTrace);
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(legacy ? int.MinValue : expected.HResult, actual.HResult);
        Assert.Equal(legacy ? "" : expected.HelpLink, actual.HelpLink);
        Assert.Equal(legacy ? 0 : expected.Data.Count, actual.Data.Count);
        if (!legacy)
            foreach (var key in expected.Data.Keys) Assert.Equal(expected.Data[key], actual.Data[key]);
        AssertException(expected.InnerException, actual.InnerException, legacy);
        if (expected is ActorInitializationException initialization)
            Assert.Equal(initialization.Actor, ((ActorInitializationException)actual).Actor);
    }

    [Fact(DisplayName = "Should_RetainOriginalFailureState_When_DynamicLoadingIsDisabled")]
    public void Should_RetainOriginalFailureState_When_DynamicLoadingIsDisabled()
    {
        var samples = RichFailures();
        var hadSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var previous);
        AppContext.SetSwitch("Akka.DynamicTypeLoading", false);
        try
        {
            var serializer = CreateV2((ExtendedActorSystem)Sys);
            foreach (var sample in samples)
                AssertFailure((Status.Failure)sample.Message,
                    Assert.IsType<Status.Failure>(Sys.Serialization.Deserialize(serializer.ToBinary(sample.Message), 56, "STF")), legacy: false);
        }
        finally
        {
            AppContext.SetSwitch("Akka.DynamicTypeLoading", !hadSwitch || previous);
        }
    }

    private object[] UnsupportedMessages(object unknown, Resizer resizer)
    {
        var node = new Address("akka.tcp", "fixture", "127.0.0.1", 2552);
        return
        [
            new Identify(unknown), new ActorIdentity(unknown, TestActor), new Status.Success(unknown),
            new Status.Failure(new InvalidOperationException("known"), unknown),
            new Status.Failure(new UnsupportedException(), null!),
            FromConfig.Instance.WithResizer(resizer),
            new RoundRobinPool(3, resizer), new BroadcastPool(3, resizer, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId),
            new RandomPool(3, resizer, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId),
            new ScatterGatherFirstCompletedPool(3, resizer, TimeSpan.FromSeconds(1), Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId),
            new TailChoppingPool(3, resizer, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)),
            new ConsistentHashingPool(3, resizer, Pool.DefaultSupervisorStrategy, Dispatchers.DefaultDispatcherId),
            new RemoteRouterConfig(new RoundRobinPool(3, resizer), [node])
        ];
    }

    [Fact(DisplayName = "Should_KeepCallerOutputEmpty_When_AnyEnvelopeFamilyHasUnsupportedState")]
    public async Task Should_KeepCallerOutputEmpty_When_AnyEnvelopeFamilyHasUnsupportedState()
    {
        var messages = UnsupportedMessages(new UnsupportedPayload(), new UnsupportedResizer());
        var hadSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var previous);
        AppContext.SetSwitch("Akka.DynamicTypeLoading", false);
        try
        {
            // The feature is a startup switch. A fresh registry excludes JSON, like native
            // execution; an already-created JIT system retains its explicitly cached serializers.
            var fresh = ActorSystem.Create("misc-unsupported-state");
            try
            {
                var serializer = CreateV2((ExtendedActorSystem)fresh);
                Assert.Equal(13, messages.Length);
                foreach (var message in messages)
                {
                    var output = new ArrayBufferWriter<byte>();
                    Assert.Throws<System.Runtime.Serialization.SerializationException>(() => serializer.Serialize(message, output));
                    Assert.Equal(0, output.WrittenCount);
                }
            }
            finally
            {
                await fresh.Terminate();
            }
        }
        finally
        {
            AppContext.SetSwitch("Akka.DynamicTypeLoading", !hadSwitch || previous);
        }
    }

    [Fact(DisplayName = "Should_KeepCallerOutputEmpty_When_RegisteredPayloadWritesThenFailsInEachEnvelopeFamily")]
    public async Task Should_KeepCallerOutputEmpty_When_RegisteredPayloadWritesThenFailsInEachEnvelopeFamily()
    {
        var messages = UnsupportedMessages(new UnsupportedPayload(), new UnsupportedResizer())
            .Where(value => value is not Status.Failure { Cause: UnsupportedException }).ToArray();
        LatePayloadSerializer? late = null;
        var setup = ActorSystemSetup.Create(BootstrapSetup.Create(), SerializationSetup.Create(node =>
            ImmutableHashSet.Create(SerializerDetails.Create("late-native-payload", late = new LatePayloadSerializer(node),
                ImmutableHashSet.Create(typeof(UnsupportedPayload), typeof(UnsupportedResizer))))));
        var fresh = ActorSystem.Create("misc-late-payload", setup);
        try
        {
            var serializer = CreateV2((ExtendedActorSystem)fresh);
            Assert.Equal(12, messages.Length);
            foreach (var message in messages)
            {
                var output = new ArrayBufferWriter<byte>();
                Assert.Throws<System.Runtime.Serialization.SerializationException>(() => serializer.Serialize(message, output));
                Assert.Equal(0, output.WrittenCount);
            }
            Assert.NotNull(late);
            Assert.Equal(12, late.Calls);
            Assert.Equal(12 * 8192, late.Written);
        }
        finally
        {
            await fresh.Terminate();
        }
    }

    [Theory(DisplayName = "Should_RejectUnknownManifest_When_ReadingTheGeneratedFormat")]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("ID2")]
    public void Should_RejectUnknownManifest_When_ReadingTheGeneratedFormat(string manifest)
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        Assert.Throws<System.Runtime.Serialization.SerializationException>(() => serializer.FromBinary(serializer.ToBinary(new Identify("hello")), manifest));
    }

    private sealed class LatePayloadSerializer(ExtendedActorSystem system) : SerializerV2(system)
    {
        public int Calls { get; private set; }
        public int Written { get; private set; }
        public override int Identifier => 77756;
        public override string Manifest(object obj) => "late-native-payload";
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            writer.GetSpan(8192)[..8192].Fill(0xac);
            writer.Advance(8192);
            Calls++;
            Written += 8192;
            throw new System.Runtime.Serialization.SerializationException("Intentional failure after writing native payload bytes.");
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
            => throw new NotSupportedException("This test payload only exercises a failing writer.");
    }

    private sealed class UnsupportedPayload;
    private sealed class UnsupportedException : Exception;
    private sealed class UnsupportedResizer : Resizer
    {
        public override bool IsTimeForResize(long messageCounter) => false;
        public override int Resize(IEnumerable<Routee> currentRoutees) => 0;
    }

    [Fact(DisplayName = "Should_PreserveMessageSpecificActorReferenceSentinels_When_EitherFormatReadsThem")]
    public void Should_PreserveMessageSpecificActorReferenceSentinels_When_EitherFormatReadsThem()
    {
        var legacy = CreateLegacy((ExtendedActorSystem)Sys);
        var modern = CreateV2((ExtendedActorSystem)Sys);
        IActorRef?[] subjects = [null, ActorRefs.Nobody, Sys.DeadLetters, TestActor];
        foreach (var subject in subjects)
        {
            var message = new ActorIdentity("sentinel", subject!);
            // ActorIdentity uses legacy path resolution, which differs from the root AR
            // manifest's explicit Nobody sentinel. Preserve this distinction per message.
            var expected = legacy.FromBinary(legacy.ToBinary(message), "AID");
            foreach (var serializer in new Serializer[] { legacy, modern })
                AssertMessage(expected, Sys.Serialization.Deserialize(serializer.ToBinary(message), serializer.Identifier, "AID"));
        }
        foreach (var actor in new[] { ActorRefs.Nobody, Sys.DeadLetters, TestActor })
        {
            foreach (var serializer in new Serializer[] { legacy, modern })
                Assert.Equal(actor, Sys.Serialization.Deserialize(serializer.ToBinary(actor), serializer.Identifier, "AR"));
        }
    }

    [Theory(DisplayName = "Should_ReturnTheLegacySingleton_When_FreshFromConfigHasAnEmptyDispatcher")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_ReturnTheLegacySingleton_When_FreshFromConfigHasAnEmptyDispatcher(bool native)
    {
        var input = new FromConfig(null!, Pool.DefaultSupervisorStrategy, "");
        Assert.NotSame(FromConfig.Instance, input);
        Assert.Empty(CreateLegacy((ExtendedActorSystem)Sys).ToBinary(input));
        var serializer = native ? (Serializer)CreateV2((ExtendedActorSystem)Sys) : CreateLegacy((ExtendedActorSystem)Sys);
        var bytes = serializer.ToBinary(input);
        var actual = Sys.Serialization.Deserialize(bytes, serializer.Identifier, "FC");
        Assert.Same(FromConfig.Instance, actual);
        AssertPool(FromConfig.Instance, Assert.IsType<FromConfig>(actual));
        var dump = HexDumpFormatter.Format("from-config-empty-dispatcher", nameof(FromConfig), "FC", serializer.Identifier, bytes);
        await Verifier.Verify(dump).UseDirectory("Snapshots")
            .UseFileName($"MiscMessageMessagePackSerializerSpec.from-config-empty-dispatcher.{(native ? "v2" : "legacy")}");
    }

    [Fact(DisplayName = "Should_ExchangeAllBoundManifests_When_OneNodePinsTheWholeLegacyProtocol")]
    public async Task Should_ExchangeAllBoundManifests_When_OneNodePinsTheWholeLegacyProtocol()
    {
        var types = new RemoteSerializers().Create((ExtendedActorSystem)Sys).Single(row => row.Serializer.Identifier == 16).UseFor.ToArray();
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(PortConfig);
        await using var pair = MixedBindingPair.Create("misc-mixed", enabled,
            MixedBindingPair.BindTo("akka-misc", types).WithFallback(enabled));
        pair.AssertRoundTripsBothWays(_cases, 56, 16);
    }
}
