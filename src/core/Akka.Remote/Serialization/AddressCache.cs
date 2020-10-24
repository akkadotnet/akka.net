//-----------------------------------------------------------------------
// <copyright file="AddressCache.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.Util;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class AddressThreadLocalCache : ExtensionIdProvider<AddressThreadLocalCache>, IExtension
    {
        public AddressThreadLocalCache()
        {
            _current = new ThreadLocal<AddressCache>(() => new AddressCache());
            _currentAS = new ThreadLocal<AddressCacheFast>(()=> new AddressCacheFast());
        }

        public override AddressThreadLocalCache CreateExtension(ExtendedActorSystem system)
        {
            return new AddressThreadLocalCache();
        }

        private readonly ThreadLocal<AddressCache> _current;
        private readonly ThreadLocal<AddressCacheFast> _currentAS;
        public AddressCache Cache => _current.Value;
        public AddressCacheFast CacheAS=>_currentAS.Value;

        public static AddressThreadLocalCache For(ActorSystem system)
        {
            return system.WithExtension<AddressThreadLocalCache, AddressThreadLocalCache>();
        }
    }
    internal sealed class AddressCacheFast : LruBoundedCache<HeldSegment, Address>
    {
        public AddressCacheFast(int capacity = 1024, int evictAgeThreshold = 600) : base(capacity, evictAgeThreshold)
        {
        }

        protected override int Hash(HeldSegment k)
        {
            return FastHash.OfStringFast(new Span<byte>(k.Segment.Array,
                k.Segment.Offset, k.Segment.Count));
        }

        protected override Address Compute(HeldSegment k)
        {
            Address addr;
            if (ActorPath.TryParseAddress(
                Encoding.UTF8.GetString(k.Segment.Array, k.Segment.Offset,
                    k.Segment.Count), out addr))
            {
                return addr;
            }
            return Address.AllSystems;
        }

        protected override bool IsCacheable(Address v)
        {
            return v != Address.AllSystems;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class AddressCache : LruBoundedCache<string, Address>
    {
        public AddressCache(int capacity = 1024, int evictAgeThreshold = 600) : base(capacity, evictAgeThreshold)
        {
        }

        protected override int Hash(string k)
        {
            return FastHash.OfStringFast(k);
        }

        protected override Address Compute(string k)
        {
            Address addr;
            if (ActorPath.TryParseAddress(k, out addr))
            {
                return addr;
            }
            return Address.AllSystems;
        }

        protected override bool IsCacheable(Address v)
        {
            return v != Address.AllSystems;
        }
    }
}
