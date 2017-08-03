using System;
using Akka.Actor;
using System.Threading;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorPathThreadLocalCache : ExtensionIdProvider<ActorPathThreadLocalCache>, IExtension
    {
        private readonly ThreadLocal<ActorPathCache> _current = new ThreadLocal<ActorPathCache>(() => new ActorPathCache());

        public ActorPathCache Cache => _current.Value;

        public override ActorPathThreadLocalCache CreateExtension(ExtendedActorSystem system)
        {
            return new ActorPathThreadLocalCache();
        }

        public static ActorPathThreadLocalCache For(ActorSystem system)
        {
            return system.WithExtension<ActorPathThreadLocalCache, ActorPathThreadLocalCache>();
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorPathCache : LruBoundedCache<string, ActorPath>
    {
        public ActorPathCache(int capacity = 1024, int evictAgeThreshold = 600) : base(capacity, evictAgeThreshold)
        {
        }

        protected override int Hash(string k)
        {
            return FastHash.OfStringFast(k);
        }

        protected override ActorPath Compute(string k)
        {
            ActorPath actorPath;
            if (ActorPath.TryParse(k, out actorPath))
                return actorPath;
            return null;
        }

        protected override bool IsCacheable(ActorPath v)
        {
            return v != null;
        }
    }
}