// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Serialization;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>Twenty-four cases: real legacy/native byte-array writes, buffer writes and reads for four protocol shapes.</summary>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
public class SystemMessageSerializerBenchmarks
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

    [Params("Suspend", "Watch", "DeathWatch", "Failed")]
    public string Shape { get; set; } = "Watch";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("system-wire-bench", Settings(false));
        _v2System = (ExtendedActorSystem)ActorSystem.Create("system-wire-bench", Settings(true));
        var target = (IInternalActorRef)_legacySystem.Provider.ResolveActorRef("akka.tcp://wire@127.0.0.1:2552/user/target#123");
        var watcher = (IInternalActorRef)_legacySystem.Provider.ResolveActorRef("akka.tcp://wire@127.0.0.1:2552/user/watcher#456");
        var cause = new InvalidOperationException("failed", new TimeoutException("inner"))
        {
            Source = "benchmark-source", HelpLink = "https://example.com/help"
        };
        cause.Data["state"] = 42;
        _message = Shape switch
        {
            "Suspend" => new Suspend(),
            "Watch" => new Watch(target, watcher),
            "DeathWatch" => new DeathWatchNotification(target, true, true),
            "Failed" => new Failed(target, cause, long.MinValue),
            _ => throw new InvalidOperationException($"Unknown benchmark shape [{Shape}].")
        };
        // The public legacy constructor anchors Remote; no friend assembly or private codec.
        _ = new SystemMessageSerializer(_legacySystem);
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _native = (SerializerV2)_v2System.Serialization.FindSerializerFor(_message);
        _adapter = new SerializerV1Adapter(_legacySystem, _legacy);
        _manifest = Akka.Serialization.Serialization.ManifestFor(_legacy, _message);
        if (_legacy.Identifier != 22 || _native.Identifier != 62 || _native.Manifest(_message) != _manifest)
            throw new InvalidOperationException("System benchmark must use real off/on bindings with unchanged manifests.");
        _legacyBytes = _legacy.ToBinary(_message);
        _nativeBytes = _native.ToBinary(_message);
        Validate(_legacy.FromBinary(_legacyBytes, _manifest), legacy: true);
        Validate(_native.FromBinary(_nativeBytes, _manifest), legacy: false);
        Validate(_legacySystem.Serialization.Deserialize(_legacyBytes, 22, _manifest), legacy: true);
        Validate(_legacySystem.Serialization.Deserialize(_nativeBytes, 62, _manifest), legacy: false);
        Validate(_v2System.Serialization.Deserialize(_legacyBytes, 22, _manifest), legacy: true);
        Validate(_v2System.Serialization.Deserialize(_nativeBytes, 62, _manifest), legacy: false);
        Console.WriteLine($"[system-wire] {Shape}: legacy22={_legacyBytes.Length}B native62={_nativeBytes.Length}B");
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
            case Suspend:
                break;
            case Watch value:
                var watch = (Watch)actual;
                Actor(value.Watchee, watch.Watchee);
                Actor(value.Watcher, watch.Watcher);
                break;
            case DeathWatchNotification value:
                var death = (DeathWatchNotification)actual;
                Actor(value.Actor, death.Actor);
                Require(value.ExistenceConfirmed == death.ExistenceConfirmed && value.AddressTerminated == death.AddressTerminated,
                    "DeathWatch flags differ.");
                break;
            case Failed value:
                var failed = (Failed)actual;
                Actor(value.Child, failed.Child);
                Require(value.Uid == failed.Uid, "Signed failure UID differs.");
                Cause(value.Cause, failed.Cause, legacy);
                break;
            default:
                throw new InvalidOperationException("Missing validation shape.");
        }
    }

    private static void Actor(IActorRef expected, IActorRef actual)
    {
        Require(expected.Path.ToSerializationFormat() == actual.Path.ToSerializationFormat() && expected.Path.Uid == actual.Path.Uid,
            "Actor path/UID differs.");
        Require(expected.Equals(actual), "Public actor identity differs.");
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
