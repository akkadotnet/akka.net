//-----------------------------------------------------------------------
// <copyright file="GeneratedMessagePackSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Actor.Setup;
using Akka.Benchmarks.Configurations;
using Akka.Serialization;
using Akka.Serialization.V2;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
public class GeneratedMessagePackSerializerBenchmarks
{
    private ExtendedActorSystem _generatedSystem = null!;
    private ExtendedActorSystem _jsonSystem = null!;
    private SubmitOrder _message = null!;
    private Serializer _generatedSerializer = null!;
    private Serializer _jsonSerializer = null!;
    private byte[] _generatedBytes = null!;
    private byte[] _jsonBytes = null!;
    private string _generatedManifest = null!;
    private string _jsonManifest = null!;

    [GlobalSetup]
    public void Setup()
    {
        var generatedSetup = ActorSystemSetup.Create(OrderBenchmarkSerializer.CreateRegistration().CreateSetup());
        _generatedSystem = (ExtendedActorSystem)ActorSystem.Create("generated-messagepack-bench", generatedSetup);
        _jsonSystem = (ExtendedActorSystem)ActorSystem.Create("json-messagepack-bench");

        var replyTo = _generatedSystem.ActorOf(act => act.ReceiveAny((message, context) => context.Sender.Tell(message)), "replyTo");
        _message = new SubmitOrder(
            "order-12345",
            Guid.Parse("964e7a77-4112-4f75-8fa4-5a2d8af546bb"),
            12500042L,
            true,
            1337.42m,
            DateTimeOffset.FromUnixTimeMilliseconds(1_735_689_600_000),
            OrderPriority.High,
            replyTo);

        _generatedSerializer = _generatedSystem.Serialization.FindSerializerFor(_message);
        _jsonSerializer = _jsonSystem.Serialization.FindSerializerFor(_message);
        _generatedManifest = Akka.Serialization.Serialization.ManifestFor(_generatedSerializer, _message);
        _jsonManifest = Akka.Serialization.Serialization.ManifestFor(_jsonSerializer, _message);
        _generatedBytes = _generatedSystem.Serialization.Serialize(_message);
        _jsonBytes = _jsonSystem.Serialization.Serialize(_message);

        Console.WriteLine($"Generated MessagePack payload size: {_generatedBytes.Length} bytes");
        Console.WriteLine($"Newtonsoft JSON payload size: {_jsonBytes.Length} bytes");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await Task.WhenAll(_generatedSystem.Terminate(), _jsonSystem.Terminate());
    }

    [Benchmark]
    public byte[] Generated_MessagePack_serialize()
    {
        return _generatedSystem.Serialization.Serialize(_message);
    }

    [Benchmark]
    public object Generated_MessagePack_deserialize()
    {
        return _generatedSystem.Serialization.Deserialize(_generatedBytes, _generatedSerializer.Identifier, _generatedManifest);
    }

    [Benchmark(Baseline = true)]
    public byte[] NewtonsoftJson_serialize()
    {
        return _jsonSystem.Serialization.Serialize(_message);
    }

    [Benchmark]
    public object NewtonsoftJson_deserialize()
    {
        return _jsonSystem.Serialization.Deserialize(_jsonBytes, _jsonSerializer.Identifier, _jsonManifest);
    }

    public int Generated_MessagePack_payload_size()
    {
        return _generatedBytes.Length;
    }

    public int NewtonsoftJson_payload_size()
    {
        return _jsonBytes.Length;
    }
}

public interface IOrderBenchmarkProtocol
{
}

[AkkaSerializer(Name = "order-benchmark", SerializerId = 120001)]
public sealed partial class OrderBenchmarkSerializer : MessagePackSerializer<IOrderBenchmarkProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializable(Manifest = "submit-order-v1")]
public sealed record SubmitOrder(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] Guid CustomerId,
    [property: AkkaField(2)] long SequenceNr,
    [property: AkkaField(3)] bool Expedited,
    [property: AkkaField(4)] decimal Total,
    [property: AkkaField(5)] DateTimeOffset CreatedAt,
    [property: AkkaField(6)] OrderPriority Priority,
    [property: AkkaField(7)] IActorRef? ReplyTo) : IOrderBenchmarkProtocol;

public enum OrderPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}
