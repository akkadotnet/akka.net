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
using BitFaster.Caching.Lru;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorPathThreadLocalCache : ExtensionIdProvider<ActorPathThreadLocalCache>, IExtension
    {
        //private readonly ThreadLocal<ActorPathCache> _current = new ThreadLocal<ActorPathCache>(() => new ActorPathCache());

        private readonly ActorPathBitfasterCache _current =
            new ActorPathBitfasterCache();

        //public ActorPathCache Cache => _current.Value;
        public ActorPathBitfasterCache Cache => _current;
        public override ActorPathThreadLocalCache CreateExtension(ExtendedActorSystem system)
        {
            return new ActorPathThreadLocalCache();
        }

        public static ActorPathThreadLocalCache For(ActorSystem system)
        {
            return system.WithExtension<ActorPathThreadLocalCache, ActorPathThreadLocalCache>();
        }
    }
    internal sealed class ActorPathAskResolverCache : ExtensionIdProvider<ActorPathAskResolverCache>, IExtension
    {
        //private readonly ThreadLocal<ActorPathCache> _current = new ThreadLocal<ActorPathCache>(() => new ActorPathCache());

        private readonly ActorPathAskCache _current =
            new ActorPathAskCache();

        //public ActorPathCache Cache => _current.Value;
        public ActorPathAskCache Cache => _current;
        public override ActorPathAskResolverCache CreateExtension(ExtendedActorSystem system)
        {
            return new ActorPathAskResolverCache();
        }

        public static ActorPathAskResolverCache For(ActorSystem system)
        {
            return system.WithExtension<ActorPathAskResolverCache, ActorPathAskResolverCache>();
        }
    }
    public class ActorPathAskCache
    {
        private readonly FastConcurrentLru<string, ActorPath> _cache =
            new FastConcurrentLru<string, ActorPath>(Environment.ProcessorCount,
                1030, FastHashComparer.Default);

        public ActorPath GetOrNull(string actorPath)
        {
            if (_cache.TryGet(actorPath, out ActorPath askRef))
            {
                return askRef;
            }

            return null;
        }

        public void Set(string actorPath, ActorPath actorPathObj)
        {
            _cache.AddOrUpdate(actorPath,actorPathObj);
        }
        
    }

    internal sealed class ActorPathBitfasterCache
    {
        public FastConcurrentLru<string, ActorPath> _cache =
            new FastConcurrentLru<string, ActorPath>(Environment.ProcessorCount,
                1030, FastHashComparer.Default);
        public FastConcurrentLru<string, ActorPath> _rootCache =
            new FastConcurrentLru<string, ActorPath>(Environment.ProcessorCount,
                1030, FastHashComparer.Default);
        public ActorPath GetOrCompute(string k)
        {
            if (k.Contains("/temp/"))
            {
                return ParsePath(k);
            }
            if (_cache.TryGet(k, out ActorPath outPath))
            {
                return outPath;
            }

            outPath = ParsePath(k);
            if (outPath != null)
            {
                _cache.AddOrUpdate(k,outPath);
            }

            return outPath;
        }

        private ActorPath ParsePath(string k)
        {
            var path = k.AsSpan();

            if (!ActorPath.TryParseParts(path, out var addressSpan, out var absoluteUri)
            )
                return null;


            string rootPath;
            if (absoluteUri.Length > 1 || path.Length > addressSpan.Length)
            {
                //path end with /
                rootPath = path.Slice(0, addressSpan.Length + 1).ToString();
            }
            else
            {
                //todo replace with string.create
                Span<char> buffer = addressSpan.Length < 1024
                    ? stackalloc char[addressSpan.Length + 1]
                    : new char[addressSpan.Length + 1];
                path.Slice(0, addressSpan.Length).CopyTo(buffer);
                buffer[buffer.Length - 1] = '/';
                rootPath = buffer.ToString();
            }

            //try lookup root in cache
            if (!_rootCache.TryGet(rootPath, out var actorPath))
            {
                if (!Address.TryParse(addressSpan, out var address))
                    return null;

                actorPath = new RootActorPath(address);
                _rootCache.AddOrUpdate(rootPath, actorPath);
            }

            if (!ActorPath.TryParse(actorPath, absoluteUri, out actorPath))
                return null;

            return actorPath;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorPathCache : LruBoundedCache<string, ActorPath>
    {
        public ActorPathCache(int capacity = 1024, int evictAgeThreshold = 600)
           : base(capacity, evictAgeThreshold, FastHashComparer.Default)
        {
        }

        protected override ActorPath Compute(string k)
        {
            ActorPath actorPath;

            var path = k.AsSpan();

            if (!ActorPath.TryParseParts(path, out var addressSpan, out var absoluteUri))
                return null;


            string rootPath;
            if(absoluteUri.Length > 1 || path.Length > addressSpan.Length)
            {
                //path end with /
                rootPath = path.Slice(0, addressSpan.Length + 1).ToString();   
            }
            else
            {
                //todo replace with string.create
                Span<char> buffer = addressSpan.Length < 1024 
                    ? stackalloc char[addressSpan.Length + 1] 
                    : new char[addressSpan.Length + 1];
                path.Slice(0, addressSpan.Length).CopyTo(buffer);
                buffer[buffer.Length - 1] = '/';
                rootPath = buffer.ToString();
            }

            //try lookup root in cache
            if (!TryGet(rootPath, out actorPath))
            {
                if (!Address.TryParse(addressSpan, out var address))
                    return null;

                actorPath = new RootActorPath(address);
                TrySet(rootPath, actorPath);
            }

            if (!ActorPath.TryParse(actorPath, absoluteUri, out actorPath))
                return null;

            return actorPath;            
        }

        protected override bool IsCacheable(ActorPath v)
        {
            return v != null;
        }
    }
}
