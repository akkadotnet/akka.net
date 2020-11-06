using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Akka.Remote.Artery.Utils;
using Akka.Util;

namespace Akka.Remote.Artery.Compress
{
    internal sealed class CompressionTable<T> : IEquatable<CompressionTable<T>>
    {
        public const int NotCompressedId = -1;
        public static CompressionTable<T> Empty { get; } = new CompressionTable<T>(0, 0, ImmutableDictionary<T, int>.Empty);

        public long OriginUid { get; }
        public byte Version { get; }

        public ImmutableDictionary<T, int> Dictionary { get; }

        public CompressionTable(long originUid, byte version, ImmutableDictionary<T, int> dictionary)
        {
            OriginUid = originUid;
            Version = version;
            Dictionary = dictionary;
        }

        public int Compress(T value) => Dictionary[value];

        public DecompressionTable<T> Invert()
        {
            if(Dictionary.IsEmpty)
                return DecompressionTable.Empty<T>().Copy(originUid:OriginUid, version:Version);

            // TODO: these are some expensive sanity checks, about the numbers being consecutive, without gaps
            // TODO: we can remove them, make them re-map (not needed I believe though)
            var minValue = Dictionary.Values.Min();
            minValue.Requiring(v => v == 0, $"Compression table should start allocating from 0, yet lowest allocated id was {minValue}");

            var expectedGaplessSum = (Dictionary.Count * (Dictionary.Count + 1) / 2); // Dirichlet
            var sumValue = Dictionary.Values.Sum() + Dictionary.Count;
            sumValue.Requiring(v => v == expectedGaplessSum, "Given compression map does not seem to be gap-less and starting from zero, " +
                                                         $"which makes compressing it into an Array difficult, bailing out! Map was: {Dictionary.DebugString()}");

            var sDict = Dictionary.ToImmutableSortedDictionary(kvp => kvp.Value, kvp => kvp.Key);

            var ts = new T[Dictionary.Count];
            foreach (var kvp in sDict)
            {
                ts[kvp.Key] = kvp.Value;
            }

            return new DecompressionTable<T>(OriginUid, Version, ts.ToImmutableArray());
        }

        /// <summary>
        /// Writes complete table as String (heavy operation)
        /// </summary>
        /// <returns></returns>
        public override string ToString()
            => $"CompressionTable({OriginUid}, {Version}, {Dictionary.DebugString()})";

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = OriginUid.GetHashCode();
                hashCode = (hashCode * 397) ^ Version.GetHashCode();
                hashCode = (hashCode * 397) ^ (Dictionary != null ? Dictionary.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || obj is CompressionTable<T> other && Equals(other);
        }

        public bool Equals(CompressionTable<T> other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return OriginUid == other.OriginUid && Version == other.Version && Equals(Dictionary, other.Dictionary);
        }
    }
}
