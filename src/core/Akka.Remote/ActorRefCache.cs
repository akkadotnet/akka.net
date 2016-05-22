using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Util.Internal;
using System.Threading;

namespace Akka.Remote
{
    internal class ActorRefCacheController : ReceiveActor
    {
        #region Messages

        public class Init
        {
            public ActorRefCache Cache { get; }

            public Init(ActorRefCache cache)
            {
                Cache = cache;
            }
        }

        public class AddCacheEntry
        {
            public LinkedListNode<ActorRefCacheEntry> CacheEntry { get; }

            public AddCacheEntry(LinkedListNode<ActorRefCacheEntry> cacheEntry)
            {
                CacheEntry = cacheEntry;
            }
        }

        public class CacheHit
        {
            public LinkedListNode<ActorRefCacheEntry> CacheEntry { get; }

            public CacheHit(LinkedListNode<ActorRefCacheEntry> cacheEntry)
            {
                CacheEntry = cacheEntry;
            }
        }

        public class CheckCacheExpiration
        {
            public static readonly CheckCacheExpiration Instance = new CheckCacheExpiration();
        }

        public class GetCacheSize
        { }

        public class CacheSize
        {
            public int LocalActorRefCount { get; }
            public int RemoteActorRefCount { get; }

            public CacheSize(int localActorRefCount, int remoteActorRefCount)
            {
                LocalActorRefCount = localActorRefCount;
                RemoteActorRefCount = remoteActorRefCount;
            }
        }

        #endregion

        private static readonly TimeSpan CacheExpirationDelay = TimeSpan.FromSeconds(1);

        private ActorRefCache _cache;
        private readonly ActorRefCacheSettings _settings;
        private readonly Dictionary<IActorRef, string> _localActorRefs;
        private readonly LinkedList<ActorRefCacheEntry> _remoteActorRefs;
        private int _remoteActorRefCount;
        
        public ActorRefCacheController(ActorRefCacheSettings settings)
        {
            _settings = settings;
            _localActorRefs = new Dictionary<IActorRef, string>(RefEqualityComparer<IActorRef>.Default);
            _remoteActorRefs = new LinkedList<ActorRefCacheEntry>();

            Receive<Init>(msg =>
            {
                _cache = msg.Cache;
                Become(Ready);
            });
        }

        public void Ready()
        {
            if (_settings.RemoteCacheExpiration != Timeout.InfiniteTimeSpan)
                Context.System.Scheduler.ScheduleTellOnce(CacheExpirationDelay, Self, CheckCacheExpiration.Instance, ActorRefs.NoSender);

            Receive<AddCacheEntry>(msg =>
            {
                var node = msg.CacheEntry;
                var actorRef = node.Value.ActorRef;

                if (actorRef is ActorRefWithCell)
                {
                    if (!_localActorRefs.ContainsKey(actorRef))
                    {
                        _localActorRefs.Add(actorRef, node.Value.Path);
                        Context.Watch(actorRef);
                    }
                }
                else
                {
                    node.Value.LastCacheHit = Context.System.Scheduler.HighResMonotonicClock;
                    _remoteActorRefs.AddFirst(node);

                    if (_remoteActorRefCount >= _settings.MaximumCacheSize)
                    {
                        var last = _remoteActorRefs.Last;
                        _remoteActorRefs.Remove(last);
                        _cache.Remove(last.Value.Path);
                    }
                    else
                    {
                        ++_remoteActorRefCount;
                    }
                }
            });

            Receive<Terminated>(msg =>
            {
                string path;

                if (_localActorRefs.TryGetValue(msg.ActorRef, out path))
                {
                    _localActorRefs.Remove(msg.ActorRef);
                    _cache.Remove(path);
                }
            });

            Receive<CacheHit>(msg =>
            {
                LinkedListNode<ActorRefCacheEntry> node = msg.CacheEntry;

                node.Value.LastCacheHit = Context.System.Scheduler.HighResMonotonicClock;

                if (_remoteActorRefs.First != node)
                {
                    _remoteActorRefs.Remove(node);
                    _remoteActorRefs.AddFirst(node);
                }
            });

            Receive<CheckCacheExpiration>(msg =>
            {
                var expirationTime = Context.System.Scheduler.HighResMonotonicClock - _settings.RemoteCacheExpiration;

                var node = _remoteActorRefs.Last;
                while (node != null && node.Value.LastCacheHit <= expirationTime)
                {
                    _remoteActorRefs.Remove(node);
                    _cache.Remove(node.Value.Path);
                    --_remoteActorRefCount;

                    node = _remoteActorRefs.Last;
                }

                Context.System.Scheduler.ScheduleTellOnce(CacheExpirationDelay, Self, CheckCacheExpiration.Instance, ActorRefs.NoSender);
            });

            Receive<GetCacheSize>(msg =>
            {
                Sender.Tell(new CacheSize(_localActorRefs.Count, _remoteActorRefCount));
            });
        }
    }

    internal class ActorRefCacheEntry
    {
        public string Path { get; }
        public IInternalActorRef ActorRef { get; }
        public TimeSpan LastCacheHit { get; set; }

        public ActorRefCacheEntry(string path, IInternalActorRef actorRef)
        {
            Path = path;
            ActorRef = actorRef;
        }
    }

    internal class ActorRefCacheSettings
    {
        public bool Enabled { get; }
        public int MaximumCacheSize { get; }
        public TimeSpan RemoteCacheExpiration { get; }

        public ActorRefCacheSettings(Config config)
        {
            Enabled = config.GetBoolean("enabled");
            MaximumCacheSize = config.GetInt("maximum-remote-cache-size");
            RemoteCacheExpiration = config.GetTimeSpan("remote-cache-expiration");
        }
    }

    internal class ActorRefCache
    {
        private readonly ActorRefCacheSettings _settings;
        private readonly IActorRef _cacheController;
        private readonly ConcurrentDictionary<string, LinkedListNode<ActorRefCacheEntry>> _pathToRefMap;

        public ActorRefCache(ActorRefCacheSettings settings, IActorRef cacheController)
        {
            _settings = settings;
            _cacheController = cacheController;
            _pathToRefMap = new ConcurrentDictionary<string, LinkedListNode<ActorRefCacheEntry>>(StringComparer.Ordinal);
        }

        public bool TryGetActorRef(string path, out IInternalActorRef actorRef)
        {
            LinkedListNode<ActorRefCacheEntry> node;
            if (_settings.Enabled && _pathToRefMap.TryGetValue(path, out node))
            {
                actorRef = node.Value.ActorRef;

                if (!(actorRef is ActorRefWithCell))
                    _cacheController.Tell(new ActorRefCacheController.CacheHit(node));

                return true;
            }

            actorRef = null;
            return false;
        }

        public void Add(string path, IInternalActorRef actorRef)
        {
            if (!_settings.Enabled)
                return;

            // Don't cache temp actors, they are most likely only used onced so it's not worth caching
            var actorPath = actorRef.Path;
            if (actorPath.Elements.Count > 0 && actorPath.Elements[0] == "temp")
                return;

            var node = new LinkedListNode<ActorRefCacheEntry>(new ActorRefCacheEntry(path, actorRef));
            if (_pathToRefMap.TryAdd(path, node))
                _cacheController.Tell(new ActorRefCacheController.AddCacheEntry(node));
        }

        /// <summary>
        /// Do not call directly
        /// </summary>
        internal void Remove(string path)
        {
            LinkedListNode<ActorRefCacheEntry> node;
            _pathToRefMap.TryRemove(path, out node);
        }

        public static ActorRefCache Create(ExtendedActorSystem system, RemoteSettings remoteSettings = null)
        {
            var config = system.Settings.Config.GetConfig("akka.remote.actorref-cache");
            ActorRefCacheSettings settings = new ActorRefCacheSettings(config);

            if (!settings.Enabled)
                return new ActorRefCache(settings, system.DeadLetters);

            Props props;

            if (remoteSettings == null)
                props = Props.Create(() => new ActorRefCacheController(settings));
            else
                props = remoteSettings.ConfigureDispatcher(Props.Create(() => new ActorRefCacheController(settings)));

            IActorRef cacheController = system.SystemActorOf(props, "actorref-cache");
            var actorRefCache = new ActorRefCache(settings, cacheController);
            cacheController.Tell(new ActorRefCacheController.Init(actorRefCache));

            return actorRefCache;
        }
    }
}