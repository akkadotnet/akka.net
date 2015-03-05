using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// Static class for assisting with <see cref="ConsistentHashMapping"/> instances
    /// </summary>
    internal static class ConsistentHashingRouter
    {
        /// <summary>
        /// Default empty <see cref="ConsistentHashMapping"/> implementation
        /// </summary>
        public static readonly ConsistentHashMapping EmptyConsistentHashMapping = key => null;
    }

    /// <summary>
    /// Marks a given class as consistently hashable, for use with <see cref="ConsistentHashingGroup"/>
    /// or <see cref="ConsistentHashingPool"/> routers.
    /// </summary>
    public interface IConsistentHashable
    {
        object ConsistentHashKey { get; }
    }


    /// <summary>
    /// Envelope you can wrap around a message in order to make it hashable for use with <see cref="ConsistentHashingGroup"/>
    /// or <see cref="ConsistentHashingPool"/> routers.
    /// </summary>
    public class ConsistentHashableEnvelope : RouterEnvelope, IConsistentHashable
    {
        public ConsistentHashableEnvelope(object message, object hashKey)
            : base(message)
        {
            HashKey = hashKey;
        }
        public object HashKey { get; private set; }

        public object ConsistentHashKey
        {
            get { return HashKey; }
        }
    }

    /// <summary>
    /// Delegate for computing the hashkey from any given
    /// type of message. Extracts the property / data that is going
    /// to be used for a given hash, but doesn't actually return
    /// the hash values themselves.
    /// 
    /// If returning an byte[] or string it will be used as is,
    /// otherwise the configured <see cref="Serializer"/> will be applied
    /// to the returned data."/>
    /// </summary>
    public delegate object ConsistentHashMapping(object msg);

    /// <summary>
    /// Uses consistent hashing to select a <see cref="Routee"/> based on the sent message.
    /// 
    /// There are 3 ways to define what data to use for the consistent hash key.
    /// 
    /// 1. You can define a <see cref="ConsistentHashMapping"/> or use <see cref="WithHashMapper"/>
    /// of the router to map incoming messages to their consistent hash key.
    /// This makes the decision transparent for the sender.
    /// 
    /// 2. Messages may implement <see cref="IConsistentHashable"/>. The hash key is part
    /// of the message and it's convenient to define it together with the message
    /// definition.
    /// 
    /// 3. The message can be wrapped in a <see cref="ConsistentHashableEnvelope"/> to
    /// define what data to use for the consistent hash key. The sender knows what key
    /// to use.
    /// 
    /// These ways to define the consistent hash key can be used together and at the
    /// same time for one router. The <see cref="ConsistentHashMapping"/> is tried first.
    /// </summary>
    public class ConsistentHashingRoutingLogic : RoutingLogic
    {
        private readonly Lazy<LoggingAdapter> _log;
        private ConsistentHashMapping _hashMapping;
        private readonly ActorSystem _system;

        private readonly AtomicReference<Tuple<Routee[], ConsistentHash<ConsistentRoutee>>> _consistentHashRef = new AtomicReference<Tuple<Routee[], ConsistentHash<ConsistentRoutee>>>(Tuple.Create<Routee[], ConsistentHash<ConsistentRoutee>>(null, null));

        private readonly Address _selfAddress;
        private readonly int _vnodes;

        public ConsistentHashingRoutingLogic(ActorSystem system)
            : this(system, 0, ConsistentHashingRouter.EmptyConsistentHashMapping)
        {
        }

        public ConsistentHashingRoutingLogic(ActorSystem system, int virtualNodesFactor, ConsistentHashMapping hashMapping)
        {
            _system = system;
            _log = new Lazy<LoggingAdapter>(() => Logging.GetLogger(_system, this), true);
            _hashMapping = hashMapping;
            _selfAddress = system.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            _vnodes = virtualNodesFactor == 0 ? system.Settings.DefaultVirtualNodesFactor : virtualNodesFactor;
        }


        public override Routee Select(object message, Routee[] routees)
        {
            if (message == null || routees == null || routees.Length == 0)
                return Routee.NoRoutee;

            Func<ConsistentHash<ConsistentRoutee>> updateConsistentHash = () =>
            {
                // update consistentHash when routees are changed
                // changes to routees are rare when no changes this is a quick operation
                var oldConsistHashTuple = _consistentHashRef.Value;
                var oldRoutees = oldConsistHashTuple.Item1;
                var oldConsistentHash = oldConsistHashTuple.Item2;

                if (oldRoutees == null || !routees.SequenceEqual(oldRoutees))
                {
                    // when other instance, same content, no need to re-hash, but try to set routees
                    var consistentHash = routees == oldRoutees
                        ? oldConsistentHash
                        : ConsistentHash.Create(routees.Select(x => new ConsistentRoutee(x, _selfAddress)), _vnodes);
                    //ignore, don't update, in case of CAS failure
                    _consistentHashRef.CompareAndSet(oldConsistHashTuple, Tuple.Create(routees, consistentHash));
                    return consistentHash;
                }
                return oldConsistentHash;
            };

            Func<object, Routee> target = hashData =>
            {
                try
                {
                    var currentConsistentHash = updateConsistentHash();
                    if (currentConsistentHash.IsEmpty) return Routee.NoRoutee;
                    else
                    {
                        if (hashData is byte[])
                            return currentConsistentHash.NodeFor(hashData as byte[]).Routee;
                        if (hashData is string)
                            return currentConsistentHash.NodeFor(hashData as string).Routee;
                        return
                            currentConsistentHash.NodeFor(
                                _system.Serialization.FindSerializerFor(hashData).ToBinary(hashData)).Routee;
                    }
                }
                catch (Exception ex)
                {
                    //serializationfailed
                    _log.Value.Warning("Couldn't route message with consistent hash key [{0}] due to [{1}]", hashData, ex.Message);
                    return Routee.NoRoutee;
                }
            };

            if (_hashMapping(message) != null)
            {
                return target(ConsistentHash.ToBytesOrObject(_hashMapping(message)));
            }
            else if (message is IConsistentHashable)
            {
                var hashable = (IConsistentHashable)message;
                return target(ConsistentHash.ToBytesOrObject(hashable.ConsistentHashKey));
            }
            else
            {
                _log.Value.Warning("Message [{0}] must be handled by hashMapping, or implement [{1}] or be wrapped in [{2}]", message.GetType().Name, typeof(IConsistentHashable).Name, typeof(ConsistentHashableEnvelope).Name);
                return Routee.NoRoutee;
            }
        }

        public ConsistentHashingRoutingLogic WithHashMapping(ConsistentHashMapping mapping)
        {
            if (mapping == null)
                throw new ArgumentNullException("mapping");
           
            return new ConsistentHashingRoutingLogic(_system, _vnodes, mapping);
        }

    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Important to use ActorRef with full address, with host and port, in the hash ring,
    /// so that same ring is produced on different nodes.
    /// The ConsistentHash uses toString of the ring nodes, and the ActorRef itself
    /// isn't a good representation, because LocalActorRef doesn't include the
    /// host and port.
    /// </summary>
    internal sealed class ConsistentRoutee
    {
        public ConsistentRoutee(Routee routee, Address selfAddress)
        {
            SelfAddress = selfAddress;
            Routee = routee;
        }

        public Routee Routee { get; private set; }

        public Address SelfAddress { get; private set; }

        public override string ToString()
        {
            if (Routee is ActorRefRoutee)
            {
                var actorRef = Routee as ActorRefRoutee;
                return ToStringWithFullAddress(actorRef.Actor.Path);
            }
            else if (Routee is ActorSelectionRoutee)
            {
                var selection = Routee as ActorSelectionRoutee;
                return ToStringWithFullAddress(selection.Selection.Anchor.Path) + selection.Selection.PathString;
            }
            else
            {
                return Routee.ToString();
            }
        }

        private string ToStringWithFullAddress(ActorPath path)
        {
            if (string.IsNullOrEmpty(path.Address.Host) || !path.Address.Port.HasValue)
                return path.ToStringWithAddress(SelfAddress);
            return path.ToString();
        }
    }

    /// <summary>
    /// <see cref="Group"/> implementation of the consistent hashing router.
    /// </summary>
    public class ConsistentHashingGroup : Group
    {
        /// <summary>
        /// Virtual nodes used in the <see cref="ConsistentHash{T}"/>.
        /// </summary>
        public int VirtualNodesFactor { get; private set; }

        protected ConsistentHashMapping HashMapping;

        protected ConsistentHashingGroup()
        {
        }

        public ConsistentHashingGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
            VirtualNodesFactor = config.GetInt("virtual-nodes-factor", 0);
        }

        public ConsistentHashingGroup(params string[] paths)
            : base(paths)
        {
        }

        public ConsistentHashingGroup(IEnumerable<string> paths, int virtualNodesFactor = 0, ConsistentHashMapping hashMapping = null)
            : base(paths)
        {
            VirtualNodesFactor = virtualNodesFactor;
            HashMapping = hashMapping;
        }

        public ConsistentHashingGroup(IEnumerable<ActorRef> routees, int virtualNodesFactor = 0, ConsistentHashMapping hashMapping = null)
            : base(routees)
        {
            VirtualNodesFactor = virtualNodesFactor;
            HashMapping = hashMapping;
        }

        /// <summary>
        /// Apply a <see cref="VirtualNodesFactor"/> to the <see cref="ConsistentHashingGroup"/>
        /// 
        /// Note: this method is immutable and will return a new instance.
        /// </summary>
        public ConsistentHashingGroup WithVirtualNodesFactor(int vnodes)
        {
            return new ConsistentHashingGroup(Paths, vnodes, HashMapping);
        }

        /// <summary>
        /// Apply a <see cref="ConsistentHashMapping"/> to the <see cref="ConsistentHashingGroup"/>.
        /// 
        /// Note: this method is immutable and will return a new instance.
        /// </summary>
        public ConsistentHashingGroup WithHashMapping(ConsistentHashMapping mapping)
        {
            return new ConsistentHashingGroup(Paths, VirtualNodesFactor, mapping);
        }

        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ConsistentHashingRoutingLogic(system, VirtualNodesFactor, HashMapping ?? ConsistentHashingRouter.EmptyConsistentHashMapping));
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            if (routerConfig is FromConfig || routerConfig is NoRouter)
            {
                return base.WithFallback(routerConfig);
            }
            else if (routerConfig is ConsistentHashingGroup)
            {
                var other = routerConfig as ConsistentHashingGroup;
                return WithHashMapping(other.HashMapping);
            }
            else
            {
                throw new ArgumentException(string.Format("Expected ConsistentHashingGroup, got {0}", routerConfig), "routerConfig");
            }
        }
    }

    /// <summary>
    /// <see cref="Pool"/> implementation of a consistent hash router.
    /// 
    /// NOTE: Using <see cref="Resizer"/> with <see cref="ConsistentHashingPool"/> is potentially harmful, as hash ranges
    /// might change radically during live message processing. This router works best with fixed-sized pools or fixed
    /// number of routees per node in the event of clustered deployments.
    /// </summary>
    public class ConsistentHashingPool : Pool
    {
        /// <summary>
        /// Virtual nodes used in the <see cref="ConsistentHash{T}"/>.
        /// </summary>
        public int VirtualNodesFactor { get; private set; }

        private ConsistentHashMapping HashMapping;

        protected ConsistentHashingPool()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingPool"/> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public ConsistentHashingPool(Config config)
            : base(config)
        {
            VirtualNodesFactor = config.GetInt("virtual-nodes-factor", 0);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        /// <param name="virtualNodesFactor">The number of virtual nodes to use on the hash ring</param>
        /// <param name="hashMapping">The consistent hash mapping function to use on incoming messages</param>
        public ConsistentHashingPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy, string routerDispatcher, bool usePoolDispatcher = false, int virtualNodesFactor = 0, ConsistentHashMapping hashMapping = null)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            VirtualNodesFactor = virtualNodesFactor;
            HashMapping = hashMapping;
        }

        /// <summary>
        /// Apply a <see cref="VirtualNodesFactor"/> to the <see cref="ConsistentHashingPool"/>
        /// 
        /// Note: this method is immutable and will return a new instance.
        /// </summary>
        public ConsistentHashingPool WithVirtualNodesFactor(int vnodes)
        {
            return new ConsistentHashingPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher, vnodes, HashMapping);
        }

        /// <summary>
        /// Apply a <see cref="ConsistentHashMapping"/> to the <see cref="ConsistentHashingPool"/>.
        /// 
        /// Note: this method is immutable and will return a new instance.
        /// </summary>
        public ConsistentHashingPool WithHashMapping(ConsistentHashMapping mapping)
        {
            return new ConsistentHashingPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher, VirtualNodesFactor, mapping);
        }

        /// <summary>
        /// Simple form of ConsistentHashingPool constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        public ConsistentHashingPool(int nrOfInstances) : base(nrOfInstances, null, Pool.DefaultStrategy, null) { }

        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ConsistentHashingRoutingLogic(system, VirtualNodesFactor, HashMapping ?? ConsistentHashingRouter.EmptyConsistentHashMapping));
        }

        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new ConsistentHashingPool(NrOfInstances, Resizer, strategy, RouterDispatcher, UsePoolDispatcher, VirtualNodesFactor, HashMapping);
        }

        public override Pool WithResizer(Resizer resizer)
        {
            return new ConsistentHashingPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher, VirtualNodesFactor, HashMapping);
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            if (routerConfig is FromConfig || routerConfig is NoRouter)
            {
                return OverrideUnsetConfig(routerConfig);
            }
            else if (routerConfig is ConsistentHashingPool)
            {
                var other = routerConfig as ConsistentHashingPool;
                return WithHashMapping(other.HashMapping).OverrideUnsetConfig(other);
            }
            else
            {
                throw new ArgumentException(string.Format("Expected ConsistentHashingPool, got {0}", routerConfig), "routerConfig");
            }
        }
    }
}
