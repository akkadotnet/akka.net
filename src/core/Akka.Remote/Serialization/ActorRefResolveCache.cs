//-----------------------------------------------------------------------
// <copyright file="ActorRefResolveCache.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorRefResolveThreadLocalCache : ExtensionIdProvider<ActorRefResolveThreadLocalCache>, IExtension
    {
        private readonly IRemoteActorRefProvider _provider;

        public ActorRefResolveThreadLocalCache() { }

        public ActorRefResolveThreadLocalCache(IRemoteActorRefProvider provider)
        {
            _provider = provider;
            _current = new ThreadLocal<ActorRefResolveCache>(() => new ActorRefResolveCache(_provider));
        }

        public override ActorRefResolveThreadLocalCache CreateExtension(ExtendedActorSystem system)
        {
            return new ActorRefResolveThreadLocalCache((IRemoteActorRefProvider)system.Provider);
        }

        private readonly ThreadLocal<ActorRefResolveCache> _current;

        public ActorRefResolveCache Cache => _current.Value;

        public static ActorRefResolveThreadLocalCache For(ActorSystem system)
        {
            return system.WithExtension<ActorRefResolveThreadLocalCache, ActorRefResolveThreadLocalCache>();
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorRefResolveCache : LruBoundedCache<string, IActorRef>
    {
        private readonly IRemoteActorRefProvider _provider;

        public ActorRefResolveCache(IRemoteActorRefProvider provider, int capacity = 1024, int evictAgeThreshold = 600) : base(capacity, evictAgeThreshold)
        {
            _provider = provider;
        }

        protected override IActorRef Compute(string k)
        {
            return _provider.InternalResolveActorRef(k);
        }

        protected override int Hash(string k)
        {
            return FastHash.OfStringFast(k);
        }

        protected override bool IsCacheable(IActorRef v)
        {
            return !(v is EmptyLocalActorRef);
        }
    }
}
