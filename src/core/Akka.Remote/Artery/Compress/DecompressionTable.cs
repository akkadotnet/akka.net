using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using Akka.Util.Internal;

namespace Akka.Remote.Artery.Compress
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal sealed class DecompressionTable<T>
    {
        public const byte DisabledVersion = unchecked((byte)-1);
        public static DecompressionTable<T> Empty = new DecompressionTable<T>(0, 0, ImmutableArray<T>.Empty);
        public static DecompressionTable<T> Disabled = new DecompressionTable<T>(0, DisabledVersion, ImmutableArray<T>.Empty);

        public long OriginUid { get; }

        /// <summary>
        /// Either -1 for disabled or a version between 0 and 127
        /// </summary>
        public byte Version { get; }
        public ImmutableArray<T> Table { get; }
        public int Length => Table.Length;

        public DecompressionTable(long originUid, byte version, ImmutableArray<T> table)
        {
            OriginUid = originUid;
            Version = version;
            Table = table;
        }

        public DecompressionTable<T> Copy(long? originUid = null, byte? version = null, ImmutableArray<T>? table = null)
            => new DecompressionTable<T>(
                originUid ?? OriginUid,
                version ?? Version,
                table ?? Table);

        public T this[int idx]
        {
            get
            {
                if (idx < 0)
                    throw new IndexOutOfRangeException();
                if (idx >= Table.Length)
                    throw new IndexOutOfRangeException($"Attempted decompression of unknown id: [{idx}]! " +
                                                       $"Only {Table.Length} ids allocated in table version [{Version}] for origin [{OriginUid}].");
                return Table[idx];
            }
        }

        public T Get(int idx) => this[idx];

        public CompressionTable<T> Invert()
            => new CompressionTable<T>(OriginUid, Version, Table.ZipWithIndex().ToImmutableDictionary());

        /// <summary>
        /// Writes complete table as String (heavy operation)
        /// </summary>
        /// <returns></returns>
        public override string ToString()
            => $"DecompressionTable({OriginUid}, {Version}, Map({string.Join(",", Table)}";
    }
}
