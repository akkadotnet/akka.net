// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.Routing;
using Akka.Remote.Serialization;
using Akka.Routing;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>Twenty-four cases comparing actual off/on bindings for payload, fieldless, exception and routing shapes.</summary>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
public class MiscMessageSerializerBenchmarks
{
    private ExtendedActorSystem _legacySystem = null!;
    private ExtendedActorSystem _v2System = null!;
    private Serializer _legacy = null!;
    private SerializerV2 _adapter = null!;
    private SerializerV2 _native = null!;
    private object _message = null!;
    private string _manifest = null!;
    private byte[] _legacyBytes = null!;
    private byte[] _nativeBytes = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new(4096);

    [Params("ActorIdentity", "Heartbeat", "Failure", "RemoteRouter")]
    public string Shape { get; set; } = "ActorIdentity";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("misc-wire-bench", Settings(false));
        _v2System = (ExtendedActorSystem)ActorSystem.Create("misc-wire-bench", Settings(true));
        var target = _legacySystem.Provider.ResolveActorRef("akka.tcp://wire@127.0.0.1:2552/user/target#123");
        var cause = new InvalidOperationException("failed", new TimeoutException("inner"))
        {
            Source = "benchmark-source", HelpLink = "https://example.com/help"
        };
        cause.Data["state"] = 42;
        _message = Shape switch
        {
            "ActorIdentity" => new ActorIdentity("benchmark-id", target),
            "Heartbeat" => RemoteWatcher.Heartbeat.Instance,
            "Failure" => new Status.Failure(cause, new byte[] { 0, 1, 127, 255 }),
            "RemoteRouter" => new RemoteRouterConfig(new RoundRobinPool(5,
                    new DefaultResizer(2, 10, 3, 0.4, 0.2, 0.1, 17), Pool.DefaultSupervisorStrategy, "custom-dispatcher", true),
                [new Address("akka.tcp", "first", "127.0.0.1", 2552), new Address("akka", "second", "::1", null)]),
            _ => throw new InvalidOperationException($"Unknown benchmark shape [{Shape}].")
        };
        _ = new MiscMessageSerializer(_legacySystem);
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _native = (SerializerV2)_v2System.Serialization.FindSerializerFor(_message);
        _adapter = new SerializerV1Adapter(_legacySystem, _legacy);
        _manifest = Akka.Serialization.Serialization.ManifestFor(_legacy, _message);
        if (_legacy.Identifier != 16 || _native.Identifier != 56 || _native.Manifest(_message) != _manifest)
            throw new InvalidOperationException("Misc benchmark must use real off/on bindings with unchanged manifests.");
        _legacyBytes = _legacy.ToBinary(_message);
        _nativeBytes = _native.ToBinary(_message);
        Validate(_legacy.FromBinary(_legacyBytes, _manifest), legacy: true);
        Validate(_native.FromBinary(_nativeBytes, _manifest), legacy: false);
        Validate(_legacySystem.Serialization.Deserialize(_legacyBytes, 16, _manifest), legacy: true);
        Validate(_legacySystem.Serialization.Deserialize(_nativeBytes, 56, _manifest), legacy: false);
        Validate(_v2System.Serialization.Deserialize(_legacyBytes, 16, _manifest), legacy: true);
        Validate(_v2System.Serialization.Deserialize(_nativeBytes, 56, _manifest), legacy: false);
        Console.WriteLine($"[misc-wire] {Shape}: legacy16={_legacyBytes.Length}B native56={_nativeBytes.Length}B");
    }

    private static Config Settings(bool enabled) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = remote
        akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
        akka.remote.dot-netty.tcp.port = 0
        akka.loglevel = OFF
        akka.actor.serialization-v2 = {{(enabled ? "on" : "off")}}
        """);

    private void Validate(object actual, bool legacy)
    {
        Require(actual.GetType() == _message.GetType(), "Root type differs.");
        switch (_message)
        {
            case ActorIdentity value:
                var identity = (ActorIdentity)actual;
                Require(Equals(value.MessageId, identity.MessageId), "Correlation payload differs.");
                Require(value.Subject.Path.ToSerializationFormat() == identity.Subject.Path.ToSerializationFormat()
                    && value.Subject.Path.Uid == identity.Subject.Path.Uid && value.Subject.Equals(identity.Subject), "Actor identity differs.");
                break;
            case RemoteWatcher.Heartbeat:
                Require(ReferenceEquals(actual, RemoteWatcher.Heartbeat.Instance), "Heartbeat singleton differs.");
                break;
            case Status.Failure value:
                var failure = (Status.Failure)actual;
                Require(failure.State is byte[] state && ((byte[])value.State).AsSpan().SequenceEqual(state), "Failure payload differs.");
                Cause(value.Cause, failure.Cause, legacy);
                break;
            case RemoteRouterConfig value:
                var expectedView = (RemoteRouterConfig.RemoteRouterConfigSurrogate)value.ToSurrogate(_legacySystem);
                var actualView = (RemoteRouterConfig.RemoteRouterConfigSurrogate)((RemoteRouterConfig)actual).ToSurrogate(_legacySystem);
                Require(expectedView.Nodes.SequenceEqual(actualView.Nodes), "Router node protocol/system/host/port/order differs.");
                var expectedPool = expectedView.Local;
                var pool = actualView.Local;
                Require(pool.GetType() == expectedPool.GetType() && pool.NrOfInstances == expectedPool.NrOfInstances
                    && pool.RouterDispatcher == expectedPool.RouterDispatcher && pool.UsePoolDispatcher == expectedPool.UsePoolDispatcher
                    && ReferenceEquals(pool.SupervisorStrategy, Pool.DefaultSupervisorStrategy), "Pool options differ.");
                Require(pool.Resizer is DefaultResizer, "Default resizer was lost.");
                var e = (DefaultResizer)expectedPool.Resizer;
                var a = (DefaultResizer)pool.Resizer;
                Require((e.LowerBound, e.UpperBound, e.PressureThreshold, e.RampupRate, e.BackoffThreshold, e.BackoffRate, e.MessagesPerResize)
                    == (a.LowerBound, a.UpperBound, a.PressureThreshold, a.RampupRate, a.BackoffThreshold, a.BackoffRate, a.MessagesPerResize),
                    "Resizer fields differ.");
                break;
            default:
                throw new InvalidOperationException("Missing validation shape.");
        }
    }

    private static void Cause(Exception? expected, Exception? actual, bool legacy)
    {
        if (expected is null)
        {
            Require(actual is null, "Null cause differs.");
            return;
        }
        var decoded = actual ?? throw new InvalidOperationException("Exception cause was lost.");
        Require(expected.GetType() == decoded.GetType() && expected.Message == decoded.Message && expected.StackTrace == decoded.StackTrace
            && expected.Source == decoded.Source, "Exception type/message/stack/source differs.");
        Require(decoded.HResult == (legacy ? int.MinValue : expected.HResult) && decoded.HelpLink == (legacy ? "" : expected.HelpLink),
            "Exception format-specific HResult/HelpLink differs.");
        Require(decoded.Data.Count == (legacy ? 0 : expected.Data.Count), "Exception Data count differs.");
        if (!legacy)
        {
            foreach (var key in expected.Data.Keys)
                Require(Equals(expected.Data[key], decoded.Data[key]), "Exception Data value differs.");
        }
        Cause(expected.InnerException, decoded.InnerException, legacy);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    [Benchmark]
    public byte[] LegacyToBinary() => _legacy.ToBinary(_message);
    [Benchmark]
    public byte[] NativeToBinary() => _native.ToBinary(_message);
    [Benchmark]
    public int LegacyToWriter() => Write(_adapter);
    [Benchmark]
    public int NativeToWriter() => Write(_native);
    [Benchmark]
    public object LegacyFromBinary() => _legacy.FromBinary(_legacyBytes, _manifest);
    [Benchmark]
    public object NativeFromBinary() => _native.FromBinary(_nativeBytes, _manifest);

    private int Write(SerializerV2 serializer)
    {
        _buffer.Clear();
        return serializer.Serialize(_message, _buffer);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await Task.WhenAll(_legacySystem.Terminate(), _v2System.Terminate()).WaitAsync(TimeSpan.FromSeconds(10));
    }
}
