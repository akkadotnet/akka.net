// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using VerifyXunit;
using Xunit;

namespace Akka.Remote.Tests.Serialization;

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class SystemMessageMessagePackSerializerSpec : V2PortSpec
{
    internal static readonly Config PortConfig = ConfigurationFactory.ParseString("""
        akka.actor.provider = remote
        akka.remote.dot-netty.tcp.port = 0
        """);

    private readonly V2PortCase[] _cases;

    public SystemMessageMessagePackSerializerSpec(ITestOutputHelper output) : base(output, PortConfig)
    {
        _cases = CreateCases((ExtendedActorSystem)Sys);
    }

    protected override Serializer CreateLegacy(ExtendedActorSystem system) => new SystemMessageSerializer(system);
    protected override SerializerV2 CreateV2(ExtendedActorSystem system) => new SystemMessageMessagePackSerializer(system);
    // Non-null exceptions have intentionally richer V2 state. They retain their original
    // inputs and frozen bytes, with explicit per-writer assertions below instead of false parity.
    protected override IReadOnlyList<V2PortCase> Cases => _cases.Where(c => !IsRich(c)).ToArray();
    private static bool IsRich(V2PortCase c) => c.Name is "create-failed" or "recreate" or "resume" or "failed-negative-uid";

    internal static V2PortCase[] CreateCases(ExtendedActorSystem system)
    {
        var child = (IInternalActorRef)system.Provider.ResolveActorRef("akka.tcp://fixture@127.0.0.1:2552/user/child#123");
        var watcher = (IInternalActorRef)system.Provider.ResolveActorRef("akka.tcp://fixture@127.0.0.1:2552/user/watcher#456");
        return
        [
            Sample(new Create(), "create-null"),
            Sample(new Create(new ActorInitializationException(child, "initialization")), "create-failed"),
            Sample(new Recreate(null!), "recreate-null"),
            Sample(new Recreate(new InvalidOperationException("restart", new TimeoutException("inner"))), "recreate"),
            Sample(new Suspend(), "suspend"),
            Sample(new Resume(null!), "resume-null"),
            Sample(new Resume(new InvalidOperationException("resume")), "resume"),
            Sample(new Terminate(), "terminate"),
            Sample(new Supervise(child, false), "supervise-sync"),
            Sample(new Supervise(child, true), "supervise-async"),
            Sample(new Watch(child, watcher), "watch"),
            Sample(new Unwatch(child, watcher), "unwatch"),
            Sample(new Failed(child, new InvalidOperationException("failed"), long.MinValue), "failed-negative-uid"),
            Sample(new Failed(child, null!, long.MaxValue), "failed-null-positive-uid"),
            Sample(new DeathWatchNotification(child, false, false), "deathwatch-ff"),
            Sample(new DeathWatchNotification(child, false, true), "deathwatch-ft"),
            Sample(new DeathWatchNotification(child, true, false), "deathwatch-tf"),
            Sample(new DeathWatchNotification(child, true, true), "deathwatch-tt")
        ];
    }

    private static V2PortCase Sample(SystemMessage message, string name) => new(message,
        $"Akka.Dispatch.SysMsg.{message.GetType().Name}, Akka", name) { AssertEquivalent = AssertMessage };

    internal static void AssertMessage(object expected, object actual) => AssertMessageCore(expected, actual, legacy: false);

    private static void AssertMessageCore(object expected, object actual, bool legacy)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        switch (expected)
        {
            case Create value:
                AssertException(value.Failure, ((Create)actual).Failure, legacy);
                break;
            case Recreate value:
                AssertException(value.Cause, ((Recreate)actual).Cause, legacy);
                break;
            case Resume value:
                AssertException(value.CausedByFailure, ((Resume)actual).CausedByFailure, legacy);
                break;
            case Supervise value:
                var supervise = (Supervise)actual;
                Assert.Equal(value.Child, supervise.Child);
                Assert.Equal(value.Async, supervise.Async);
                break;
            case Watch value:
                var watch = (Watch)actual;
                Assert.Equal(value.Watchee, watch.Watchee);
                Assert.Equal(value.Watcher, watch.Watcher);
                break;
            case Unwatch value:
                var unwatch = (Unwatch)actual;
                Assert.Equal(value.Watchee, unwatch.Watchee);
                Assert.Equal(value.Watcher, unwatch.Watcher);
                break;
            case Failed value:
                var failed = (Failed)actual;
                Assert.Equal(value.Child, failed.Child);
                Assert.Equal(value.Uid, failed.Uid);
                AssertException(value.Cause, failed.Cause, legacy);
                break;
            case DeathWatchNotification value:
                var notification = (DeathWatchNotification)actual;
                Assert.Equal(value.Actor, notification.Actor);
                Assert.Equal(value.ExistenceConfirmed, notification.ExistenceConfirmed);
                Assert.Equal(value.AddressTerminated, notification.AddressTerminated);
                break;
            case Suspend or Terminate:
                break;
            default:
                throw new ArgumentException($"Unknown assertion type {expected.GetType()}.");
        }
    }

    private static void AssertException(Exception? expected, Exception? actual, bool legacy)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }
        Assert.NotNull(actual);
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(expected.StackTrace, actual.StackTrace);
        Assert.Equal(expected.Source, actual.Source);
        Assert.Equal(legacy ? int.MinValue : expected.HResult, actual.HResult);
        Assert.Equal(legacy ? string.Empty : expected.HelpLink, actual.HelpLink);
        Assert.Equal(legacy ? 0 : expected.Data.Count, actual.Data.Count);
        if (!legacy)
        {
            foreach (var key in expected.Data.Keys)
                Assert.Equal(expected.Data[key], actual.Data[key]);
        }
        AssertException(expected.InnerException, actual.InnerException, legacy);
        if (expected is ActorInitializationException initialization)
            Assert.Equal(initialization.Actor, ((ActorInitializationException)actual).Actor);
    }

    [Fact(DisplayName = "Should_ExchangeBothFormats_When_GlobalSwitchAndExplicitLegacyPinDiffer")]
    public async Task Should_ExchangeBothFormats_When_GlobalSwitchAndExplicitLegacyPinDiffer()
    {
        var enabled = ConfigurationFactory.ParseString("akka.actor.serialization-v2 = on").WithFallback(PortConfig);
        var pinned = MixedBindingPair.BindTo("akka-system-msg", typeof(SystemMessage)).WithFallback(enabled);
        await using var pair = MixedBindingPair.Create("system-messages-mixed", enabled, pinned);
        pair.AssertRoundTripsBothWays(Cases, 62, 22);
        foreach (var sample in _cases.Where(IsRich))
        {
            foreach (var (writer, reader, id) in new[]
                     { (pair.V2Node, pair.LegacyNode, 62), (pair.LegacyNode, pair.V2Node, 22) })
            {
                var serializer = writer.Serialization.FindSerializerFor(sample.Message);
                Assert.Equal(id, serializer.Identifier);
                Assert.Equal(sample.Manifest, Akka.Serialization.Serialization.ManifestFor(serializer, sample.Message));
                AssertMessageCore(sample.Message,
                    reader.Serialization.Deserialize(serializer.ToBinary(sample.Message), id, sample.Manifest), legacy: id == 22);
            }
        }
    }

    [Theory(DisplayName = "Should_AssertHistoricalLossAndFullNativeState_When_ExceptionMessagesUseEitherWriter")]
    [InlineData(false, "create-failed")]
    [InlineData(false, "recreate")]
    [InlineData(false, "resume")]
    [InlineData(false, "failed-negative-uid")]
    [InlineData(true, "create-failed")]
    [InlineData(true, "recreate")]
    [InlineData(true, "resume")]
    [InlineData(true, "failed-negative-uid")]
    public async Task Should_AssertHistoricalLossAndFullNativeState_When_ExceptionMessagesUseEitherWriter(bool native, string name)
    {
        var serializer = native ? (Serializer)CreateV2((ExtendedActorSystem)Sys) : CreateLegacy((ExtendedActorSystem)Sys);
        foreach (var sample in _cases.Where(c => c.Name == name))
        {
            var bytes = serializer.ToBinary(sample.Message);
            var actual = Sys.Serialization.Deserialize(bytes, serializer.Identifier, sample.Manifest);
            AssertMessageCore(sample.Message, actual, legacy: !native);
            var dump = HexDumpFormatter.Format(sample.Name, sample.Message.GetType().Name, sample.Manifest, serializer.Identifier, bytes);
            await Verifier.Verify(dump).UseDirectory("Snapshots")
                .UseFileName($"SystemMessageMessagePackSerializerSpec.{sample.Name}.{(native ? "v2" : "legacy")}");
        }
    }

    [Fact(DisplayName = "Should_RetainOriginalExceptionState_When_DynamicLoadingIsDisabled")]
    public void Should_RetainOriginalExceptionState_When_DynamicLoadingIsDisabled()
    {
        var hadSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var previous);
        AppContext.SetSwitch("Akka.DynamicTypeLoading", false);
        try
        {
            var serializer = CreateV2((ExtendedActorSystem)Sys);
            foreach (var sample in _cases.Where(IsRich))
                AssertMessage(sample.Message,
                    Sys.Serialization.Deserialize(serializer.ToBinary(sample.Message), 62, sample.Manifest));
        }
        finally
        {
            AppContext.SetSwitch("Akka.DynamicTypeLoading", !hadSwitch || previous);
        }
    }

    [Fact(DisplayName = "Should_RejectNoMessageBeforeCallerOutput_When_ItIsNotAProtocolMessage")]
    public void Should_RejectNoMessageBeforeCallerOutput_When_ItIsNotAProtocolMessage()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var output = new ArrayBufferWriter<byte>();
        Assert.Throws<ArgumentException>(() => serializer.Serialize(new NoMessage(), output));
        Assert.Equal(0, output.WrittenCount);
    }
    [Fact(DisplayName = "Should_RejectDerivedWatchBeforeCallerOutput_When_ItHasNoProtocolManifest")]
    public void Should_RejectDerivedWatchBeforeCallerOutput_When_ItHasNoProtocolManifest()
    {
        var message = new DerivedWatch((IInternalActorRef)TestActor, (IInternalActorRef)TestActor);
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var output = new ArrayBufferWriter<byte>();
        Assert.Throws<ArgumentException>(() => serializer.Manifest(message));
        Assert.Throws<ArgumentException>(() => serializer.Serialize(message, output));
        Assert.Equal(0, output.WrittenCount);
    }

    [Fact(DisplayName = "Should_KeepCallerOutputEmpty_When_AnUnsupportedNestedExceptionFails")]
    public void Should_KeepCallerOutputEmpty_When_AnUnsupportedNestedExceptionFails()
    {
        var hadSwitch = AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var previous);
        AppContext.SetSwitch("Akka.DynamicTypeLoading", false);
        try
        {
            var serializer = CreateV2((ExtendedActorSystem)Sys);
            var unknown = new UnsupportedException("unsupported");
            object[] messages =
            [
                new Create(new ActorInitializationException(TestActor, "initialization", unknown)),
                new Recreate(unknown), new Resume(unknown), new Failed(TestActor, unknown, long.MinValue)
            ];
            foreach (var message in messages)
            {
                var output = new ArrayBufferWriter<byte>();
                Assert.Throws<System.Runtime.Serialization.SerializationException>(() => serializer.Serialize(message, output));
                Assert.Equal(0, output.WrittenCount);
            }
        }
        finally
        {
            AppContext.SetSwitch("Akka.DynamicTypeLoading", !hadSwitch || previous);
        }
    }

    [Fact(DisplayName = "Should_MatchLegacyActorReferenceSentinels_When_ReadingEachMessageFamily")]
    public void Should_MatchLegacyActorReferenceSentinels_When_ReadingEachMessageFamily()
    {
        var legacy = CreateLegacy((ExtendedActorSystem)Sys);
        var native = CreateV2((ExtendedActorSystem)Sys);
        IActorRef?[] actors = [null, ActorRefs.Nobody, Sys.DeadLetters];
        foreach (var actor in actors)
        {
            object[] messages =
            [
                new Supervise(actor!, true), new Watch((IInternalActorRef)actor!, (IInternalActorRef)actor!),
                new Unwatch((IInternalActorRef)actor!, (IInternalActorRef)actor!), new Failed(actor!, null!, -1),
                new DeathWatchNotification(actor!, true, false)
            ];
            foreach (var message in messages)
            {
                var manifest = Akka.Serialization.Serialization.ManifestFor(legacy, message);
                var expected = legacy.FromBinary(legacy.ToBinary(message), manifest);
                AssertMessage(expected, native.FromBinary(native.ToBinary(message), manifest));
            }
        }
    }

    [Fact(DisplayName = "Should_PreserveOriginalRichExceptionState_When_V2WrapsEachExceptionFamily")]
    public void Should_PreserveOriginalRichExceptionState_When_V2WrapsEachExceptionFamily()
    {
        var serializer = CreateV2((ExtendedActorSystem)Sys);
        var cause = new InvalidOperationException("original", new TimeoutException("inner"))
        {
            Source = "original-source", HelpLink = "https://example.com/help"
        };
        cause.Data["custom"] = "retained";
        var initialization = new ActorInitializationException(TestActor, "initialization", cause)
        {
            Source = "initialization-source", HelpLink = "https://example.com/initialization"
        };
        initialization.Data["state"] = 42;
        object[] messages = [new Create(initialization), new Recreate(cause), new Resume(cause), new Failed(TestActor, cause, long.MinValue)];
        foreach (var message in messages)
            AssertMessage(message, serializer.FromBinary(serializer.ToBinary(message), serializer.Manifest(message)));
    }

    private sealed class DerivedWatch(IInternalActorRef watchee, IInternalActorRef watcher) : Watch(watchee, watcher);

    private sealed class UnsupportedException(string message) : Exception(message);

}
