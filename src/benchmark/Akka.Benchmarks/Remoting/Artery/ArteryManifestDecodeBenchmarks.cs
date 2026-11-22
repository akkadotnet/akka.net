//-----------------------------------------------------------------------
// <copyright file="ArteryManifestDecodeBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using Akka.Benchmarks.Configurations;
using Akka.Remote.Artery;
using BenchmarkDotNet.Attributes;
using Codec = Akka.Remote.Artery.ArteryEnvelopeCodec;

namespace Akka.Benchmarks.Remoting.Artery;

/// <summary>
/// Production header/manifest decoding with reused immutable frames, not payload deserialization or
/// actor-ref resolution. Both arms use the same uncached closed-instance decoder delegate.
/// There is no optional decoder selection. Churn is a preallocated
/// 4096-manifest ring, not message/path construction or a claim about application hit rates.
/// </summary>
[Config(typeof(MicroBenchmarkConfig))]
public class ArteryManifestDecodeBenchmarks
{
    private delegate bool ManifestResolver(ArteryEnvelopeDecoded decoded, out string manifest);

    private ManifestResolver _resolve = null!;
    private ReadOnlySequence<byte> _hot;
    private ReadOnlySequence<byte> _unicode;
    private ReadOnlySequence<byte> _segmented;
    private ReadOnlySequence<byte> _empty;
    private ReadOnlySequence<byte>[] _alternating = null!;
    private ReadOnlySequence<byte>[] _churn = null!;
    private int _cursor;
    private ReadOnlySequence<byte> _headerSegmented;

    [GlobalSetup]
    public void Setup()
    {
        _resolve = new UncachedDecoder().TryGetManifest;
        if (_resolve.Target is null || _resolve.Method.IsStatic)
            throw new InvalidOperationException("Both decoder arms require a closed-instance delegate.");
        Console.WriteLine("Manifest decoder: uncached; delegate included in both arms.");
        _hot = Frame("message-v1");
        _unicode = Frame("型-Ω-😀");
        _segmented = SegmentFrame(_unicode.ToArray());
        _empty = Frame("");
        _alternating = new[] { _hot, Frame("message-v2") };
        _churn = new ReadOnlySequence<byte>[4096];
        for (var i = 0; i < _churn.Length; i++)
            _churn[i] = Frame($"m-{i:D8}");
        foreach (var frame in new[] { _hot, _unicode, _segmented, _empty, _alternating[1], _churn[0], _churn[4095] })
        {
            var decoded = Codec.Decode(frame);
            if (!Uncached(decoded, out var expected) || !_resolve(decoded, out var actual) || actual != expected)
                throw new InvalidOperationException("Manifest decoder setup validation failed.");
        }
        _cursor = 0;
        _headerSegmented = HeaderSegmentFrame(_unicode.ToArray());
        var headerDecoded = Codec.Decode(_headerSegmented);
        var literalStart = headerDecoded.Header.ManifestTag + ArteryEnvelopeHeader.LiteralLengthPrefixSize;
        var literalLength = headerDecoded.Header.PayloadOffset - literalStart;
        if (_headerSegmented.IsSingleSegment || !_headerSegmented.Slice(literalStart, literalLength).IsSingleSegment
            || !Uncached(headerDecoded, out var expectedHeader)
            || !_resolve(headerDecoded, out var actualHeader) || actualHeader != expectedHeader)
            throw new InvalidOperationException("Expected a split header with a contiguous unchanged manifest.");
    }

    [Benchmark] public int Hot() => Read(_hot);
    [Benchmark] public int Alternating() => Read(_alternating[_cursor++ & 1]);
    [Benchmark] public int Churning() => Read(_churn[_cursor++ & 4095]);
    [Benchmark] public int UnicodeHot() => Read(_unicode);
    [Benchmark] public int SegmentedHot() => Read(_segmented);
    [Benchmark] public int Empty() => Read(_empty);
    [Benchmark] public int HeaderSegmentedHot() => Read(_headerSegmented);

    private int Read(ReadOnlySequence<byte> frame)
    {
        if (!_resolve(Codec.Decode(frame), out var manifest))
            throw new InvalidOperationException("Expected a literal or absent manifest.");
        return manifest.Length;
    }

    private static bool Uncached(ArteryEnvelopeDecoded decoded, out string manifest) => decoded.TryGetManifest(out manifest);

    private sealed class UncachedDecoder
    {
        public bool TryGetManifest(ArteryEnvelopeDecoded decoded, out string manifest) => decoded.TryGetManifest(out manifest);
    }

    private static ReadOnlySequence<byte> Frame(string manifest)
    {
        var frame = new byte[Codec.MaxEncodedSize(null, null, manifest, 32)];
        Codec.Encode(frame, 42, 1, null, null, manifest, new byte[32]);
        return new ReadOnlySequence<byte>(frame, 4, frame.Length - 4);
    }

    private static ReadOnlySequence<byte> HeaderSegmentFrame(byte[] bytes)
    {
        var first = new Segment(bytes.AsMemory(0, 10));
        var last = first.Append(bytes.AsMemory(10));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private static ReadOnlySequence<byte> SegmentFrame(byte[] bytes)
    {
        var first = new Segment(bytes.AsMemory(0, 33)); // split the literal length prefix
        var middle = first.Append(bytes.AsMemory(33, 3)); // also split a UTF8 character
        var last = middle.Append(bytes.AsMemory(36));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
