//-----------------------------------------------------------------------
// <copyright file="ActorRefResolveCache.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Akka.Actor;
using Akka.Util.Internal;
using BitFaster.Caching.Lru;

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
            //_current =  new ThreadLocal<ActorRefResolveCache>(() => new ActorRefResolveCache(_provider));
            _current = new ActorRefResolveBitfasterCache(_provider);
        }

        public override ActorRefResolveThreadLocalCache CreateExtension(ExtendedActorSystem system)
        {
            return new ActorRefResolveThreadLocalCache((IRemoteActorRefProvider)system.Provider);
        }

        //private readonly ThreadLocal<ActorRefResolveCache> _current;
        private readonly ActorRefResolveBitfasterCache _current;

        //public ActorRefResolveCache Cache => _current.Value;
        public ActorRefResolveBitfasterCache Cache => _current;

        public static ActorRefResolveThreadLocalCache For(ActorSystem system)
        {
            return system.WithExtension<ActorRefResolveThreadLocalCache, ActorRefResolveThreadLocalCache>();
        }
    }
    
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorRefResolveAskCache : ExtensionIdProvider<ActorRefResolveAskCache>, IExtension
    {
        private readonly IRemoteActorRefProvider _provider;
        
        public ActorRefResolveAskCache()
        {
            //_current =  new ThreadLocal<ActorRefResolveCache>(() => new ActorRefResolveCache(_provider));
            _current = new ActorRefAskResolverCache();
        }

        public override ActorRefResolveAskCache CreateExtension(ExtendedActorSystem system)
        {
            return new ActorRefResolveAskCache();
        }

        //private readonly ThreadLocal<ActorRefResolveCache> _current;
        private readonly ActorRefAskResolverCache _current;

        //public ActorRefResolveCache Cache => _current.Value;
        public ActorRefAskResolverCache Cache => _current;

        public static ActorRefResolveAskCache For(ActorSystem system)
        {
            return system.WithExtension<ActorRefResolveAskCache, ActorRefResolveAskCache>();
        }
    }

    public class ActorRefAskResolverCache
    {
        private readonly FastConcurrentLru<string, IActorRef> _cache =
            new FastConcurrentLru<string, IActorRef>(Environment.ProcessorCount,
                1030, FastHashComparer.Default);

        public IActorRef GetOrNull(string actorPath)
        {
            if (_cache.TryGet(actorPath, out IActorRef askRef))
            {
                return askRef;
            }

            return null;
        }

        public void Set(string actorPath, IActorRef actorRef)
        {
            _cache.AddOrUpdate(actorPath,actorRef);
        }
        
    }

    public class ActorRefResolveBitfasterCache
    {
        private readonly IRemoteActorRefProvider _provider;

        private readonly FastConcurrentLru<string, IActorRef> _cache =
            new FastConcurrentLru<string, IActorRef>(Environment.ProcessorCount,
                1030, FastHashComparer.Default);
        public ActorRefResolveBitfasterCache(IRemoteActorRefProvider provider)
        {
            _provider = provider;
        }

        public IActorRef GetOrCompute(string k)
        {
            if (_cache.TryGet(k, out IActorRef outRef))
            {
                return outRef;
            }
            outRef= _provider.InternalResolveActorRef(k);
            if (!(outRef is MinimalActorRef && !(outRef is FunctionRef)))
            {
                _cache.AddOrUpdate(k, outRef);
            }

            return outRef;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorRefResolveCache : LruBoundedCache<string, IActorRef>
    {
        private readonly IRemoteActorRefProvider _provider;

        public ActorRefResolveCache(IRemoteActorRefProvider provider, int capacity = 1024, int evictAgeThreshold = 600) 
            : base(capacity, evictAgeThreshold, FastHashComparer.Default)
        {
            _provider = provider;
        }

        protected override IActorRef Compute(string k)
        {
            return _provider.InternalResolveActorRef(k);
        }

        protected override bool IsCacheable(IActorRef v)
        {
            // don't cache any FutureActorRefs, et al
            return !(v is MinimalActorRef && !(v is FunctionRef));
        }
    }
}
