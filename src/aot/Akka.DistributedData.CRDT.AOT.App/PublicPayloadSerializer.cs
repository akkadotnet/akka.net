// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Buffers;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.DistributedData.CRDT.AOT.App;

internal sealed record Payload(string Value);

internal sealed class PublicPayloadSerializer(ExtendedActorSystem system) : SerializerV2(system)
{
    public override int Identifier => 777;
    public override string Manifest(object obj) => obj is string ? "s" : "p";
    internal int Reads { get; set; }
    internal int Writes { get; set; }
    internal bool FailRead { get; set; }
    internal bool FailWrite { get; set; }

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        Writes++;
        var value = obj is string text ? text : ((Payload)obj).Value;
        var bytes = Encoding.UTF8.GetBytes("application-owned:" + value);
        writer.Write(bytes);
        if (FailWrite)
            throw new SerializationException("Intentional late application payload failure.");
        return bytes.Length;
    }

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
        Reads++;
        if (FailRead)
            throw new SerializationException("A cold guarded application payload reader must not run.");
        const string prefix = "application-owned:";
        var text = Encoding.UTF8.GetString(bytes.ToArray());
        Proof.Require(text.StartsWith(prefix, StringComparison.Ordinal), "Application payload reader owns its bytes");
        var value = text[prefix.Length..];
        return manifest == "s" ? value : new Payload(value);
    }
}
