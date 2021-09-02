using System.Threading;
using Akka.Actor;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Remote.Transport;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorRefSerializeThreadLocalCache : ExtensionIdProvider<ActorRefSerializeThreadLocalCache>, IExtension
    {
        private readonly IRemoteActorRefProvider _provider;

        public ActorRefSerializeThreadLocalCache() { }

        public ActorRefSerializeThreadLocalCache(IRemoteActorRefProvider provider)
        {
            _provider = provider;
            _current = new ThreadLocal<ActorRefSerializationCache>(() => new ActorRefSerializationCache(_provider));
        }

        public override ActorRefSerializeThreadLocalCache CreateExtension(ExtendedActorSystem system)
        {
            return new ActorRefSerializeThreadLocalCache((IRemoteActorRefProvider)system.Provider);
        }

        private readonly ThreadLocal<ActorRefSerializationCache> _current;

        public ActorRefSerializationCache Cache => _current.Value;

        public static ActorRefSerializeThreadLocalCache For(ActorSystem system)
        {
            return system.WithExtension<ActorRefSerializeThreadLocalCache, ActorRefSerializeThreadLocalCache>();
        }
    }
    
    /// <summary>
    /// Cache used for outbound serialization
    /// </summary>
    internal sealed class ActorRefSerializationCache : LruBoundedCache<IActorRef, ActorRefData>
    {
        private readonly IRemoteActorRefProvider _provider;
        
        public ActorRefSerializationCache(IRemoteActorRefProvider provider, int capacity = 1024, int evictAgeThreshold = 600) : base(capacity, evictAgeThreshold)
        {
            _provider = provider;
        }

        protected override int Hash(IActorRef k)
        {
            return k.GetHashCode();
        }

        protected override ActorRefData Compute(IActorRef k)
        {
            return AkkaPduProtobuffCodec.InternalSerializeActorRef(_provider.DefaultAddress, k);
        }

        protected override bool IsCacheable(ActorRefData v)
        {
            // don't cache temporary actors
            return !v.Path.Contains("temp");
        }
    }
}