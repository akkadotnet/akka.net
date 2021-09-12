//-----------------------------------------------------------------------
// <copyright file="ActorPathCache.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using System.Threading;
using System.Collections.Generic;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorPathThreadLocalCache : ExtensionIdProvider<ActorPathThreadLocalCache>, IExtension
    {
        private readonly ThreadLocal<ActorPathCache> _current = new ThreadLocal<ActorPathCache>(() => new ActorPathCache(), false);

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
            //todo lookup in address cache

            if (!ActorPath.TryParseAddress(k, out var address, out var absoluteUri))
                return null;

            //todo lookup in root in cache

            if (!ActorPath.TryParse(new RootActorPath(address), absoluteUri, out var actorPath))
                return null;

            return actorPath;
        }

        protected override bool IsCacheable(ActorPath v)
        {
            return v != null;
        }
    }
}
