//-----------------------------------------------------------------------
// <copyright file="ConsistentHashRouter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
    /// This interface marks a given class as consistently hashable, for use with
    /// <see cref="ConsistentHashingGroup"/> or <see cref="ConsistentHashingPool"/>
    /// routers.
    /// </summary>
    public interface IConsistentHashable
    {
        /// <summary>
        /// The consistent hash key of the marked class.
        /// </summary>
        object ConsistentHashKey { get; }
    }


    /// <summary>
    /// This class represents a <see cref="RouterEnvelope"/> that can be wrapped around a message in order to make
    /// it hashable for use with <see cref="ConsistentHashingGroup"/> or <see cref="ConsistentHashingPool"/> routers.
    /// </summary>
    public class ConsistentHashableEnvelope : RouterEnvelope, IConsistentHashable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashableEnvelope"/> class.
        /// </summary>
        /// <param name="message">The message that is being wrapped in the envelope.</param>
        /// <param name="hashKey">The key used as the consistent hash key for the envelope.</param>
        public ConsistentHashableEnvelope(object message, object hashKey)
            : base(message)
        {
            HashKey = hashKey;
        }

        /// <summary>
        /// The key used as the consistent hash key.
        /// 
        /// <remarks>
        /// This is the same as the <see cref="ConsistentHashKey"/>
        /// </remarks>
        /// </summary>
        public object HashKey { get; private set; }

        /// <summary>
        /// The consistent hash key of the envelope.
        /// </summary>
        public object ConsistentHashKey
        {
            get { return HashKey; }
        }
    }

    /// <summary>
    /// Delegate for computing the hashkey from any given type of message. Extracts the property / data
    /// that is going to be used for a given hash, but doesn't actually return the hash values themselves.
    /// 
    /// If returning a byte[] or string it will be used as is, otherwise the configured
    /// <see cref="Serializer"/> will be applied to the returned data.
    /// </summary>
    public delegate object ConsistentHashMapping(object msg);

    /// <summary>
    /// This class contains logic used by a <see cref="Router"/> to route a message to a <see cref="Routee"/>
    /// determined using consistent-hashing. This process has the router select a routee based on a message's
    /// consistent hash key. There are 3 ways to define the key, which can be used individually or combined
    /// to form the key. The <see cref="ConsistentHashMapping"/> is tried first.
    /// 
    /// <ol>
    /// <li>
    /// You can define a <see cref="ConsistentHashMapping"/> or use <see cref="WithHashMapping"/>
    /// of the router to map incoming messages to their consistent hash key.
    /// This makes the decision transparent for the sender.
    /// </li>
    /// <li>
    /// Messages may implement <see cref="IConsistentHashable"/>. The hash key is part
    /// of the message and it's convenient to define it together with the message
    /// definition.
    /// </li>
    /// <li>
    /// The message can be wrapped in a <see cref="ConsistentHashableEnvelope"/> to
    /// define what data to use for the consistent hash key. The sender knows what key
    /// to use.
    /// </li>
    /// </ol>
    /// </summary>
    public class ConsistentHashingRoutingLogic : RoutingLogic
    {
        private readonly Lazy<ILoggingAdapter> _log;
        private ConsistentHashMapping _hashMapping;
        private readonly ActorSystem _system;

        private readonly AtomicReference<Tuple<Routee[], ConsistentHash<ConsistentRoutee>>> _consistentHashRef =
            new AtomicReference<Tuple<Routee[], ConsistentHash<ConsistentRoutee>>>(
                Tuple.Create<Routee[], ConsistentHash<ConsistentRoutee>>(null, null));

        private readonly Address _selfAddress;
        private readonly int _vnodes;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingRoutingLogic"/> class.
        /// 
        /// <note>
        /// A <see cref="ConsistentHashingRoutingLogic"/> configured in this way uses the
        /// <see cref="ConsistentHashingRouter.EmptyConsistentHashMapping"/> as the hash
        /// mapping function with a virtual node factor of 0 (zero).
        /// </note>
        /// </summary>
        /// <param name="system">The actor system that owns the router with this logic.</param>
        public ConsistentHashingRoutingLogic(ActorSystem system)
            : this(system, 0, ConsistentHashingRouter.EmptyConsistentHashMapping)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingRoutingLogic"/> class.
        /// </summary>
        /// <param name="system">The actor system that owns the router with this logic.</param>
        /// <param name="virtualNodesFactor">The number of virtual nodes to use on the hash ring.</param>
        /// <param name="hashMapping">The consistent hash mapping function to use on incoming messages.</param>
        public ConsistentHashingRoutingLogic(ActorSystem system, int virtualNodesFactor,
            ConsistentHashMapping hashMapping)
        {
            _system = system;
            _log = new Lazy<ILoggingAdapter>(() => Logging.GetLogger(_system, this), true);
            _hashMapping = hashMapping;
            _selfAddress = system.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            _vnodes = virtualNodesFactor == 0 ? system.Settings.DefaultVirtualNodesFactor : virtualNodesFactor;
        }

        /// <summary>
        /// Picks a <see cref="Routee" /> to receive the <paramref name="message" />.
        /// </summary>
        /// <param name="message">The message that is being routed</param>
        /// <param name="routees">A collection of routees to choose from when receiving the <paramref name="message" />.</param>
        /// <returns>A <see cref="Routee" /> that receives the <paramref name="message" />.</returns>
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
                    //serialization failed
                    _log.Value.Warning("Couldn't route message with consistent hash key [{0}] due to [{1}]", hashData,
                        ex.Message);
                    return Routee.NoRoutee;
                }
            };

            if (_hashMapping(message) != null)
            {
                return target(ConsistentHash.ToBytesOrObject(_hashMapping(message)));
            }
            else if (message is IConsistentHashable)
            {
                var hashable = (IConsistentHashable) message;
                return target(ConsistentHash.ToBytesOrObject(hashable.ConsistentHashKey));
            }
            else
            {
                _log.Value.Warning("Message [{0}] must be handled by hashMapping, or implement [{1}] or be wrapped in [{2}]",
                    message.GetType().Name, typeof (IConsistentHashable).Name, typeof (ConsistentHashableEnvelope).Name);
                return Routee.NoRoutee;
            }
        }

        /// <summary>
        /// Creates a new <see cref="ConsistentHashingRoutingLogic"/> router logic with a given <see cref="ConsistentHashMapping"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="mapping">The <see cref="ConsistentHashMapping"/> used to configure the new router.</param>
        /// <returns>A new router logic with the provided <paramref name="mapping"/>.</returns>
        /// <exception cref="ArgumentNullException">The mapping can not be null.</exception>
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
    /// This class represents a <see cref="Group"/> router that sends messages to a <see cref="Routee"/> determined using consistent-hashing.
    /// Please refer to <see cref="ConsistentHashingRoutingLogic"/> for more information on consistent hashing.
    /// </summary>
    public class ConsistentHashingGroup : Group
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="ConsistentHashingGroup"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class ConsistentHashingGroupSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="ConsistentHashingGroup"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="ConsistentHashingGroup"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new ConsistentHashingGroup(Paths);
            }

            /// <summary>
            /// The actor paths used by this router during routee selection.
            /// </summary>
            public string[] Paths { get; set; }
        }

        /// <summary>
        /// Virtual nodes used in the <see cref="ConsistentHash{T}"/>.
        /// </summary>
        public int VirtualNodesFactor { get; private set; }

        /// <summary>
        /// The consistent hash mapping function to use on incoming messages.
        /// </summary>
        protected ConsistentHashMapping HashMapping;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingGroup"/> class.
        /// </summary>
        /// <param name="config">
        /// The configuration to use to lookup paths used by the group router.
        /// 
        /// <note>
        /// If 'routees.path' is defined in the provided configuration then those paths will be used by the router.
        /// 'virtual-nodes-factor' defaults to 0 (zero) if it is not defined in the provided configuration.
        /// </note>
        /// </param>
        public ConsistentHashingGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
            VirtualNodesFactor = config.GetInt("virtual-nodes-factor", 0);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingGroup"/> class.
        /// </summary>
        /// <param name="paths">A list of actor paths used by the group router.</param>
        public ConsistentHashingGroup(params string[] paths)
            : base(paths)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingGroup"/> class.
        /// </summary>
        /// <param name="paths">An enumeration of actor paths used by the group router.</param>
        /// <param name="virtualNodesFactor">The number of virtual nodes to use on the hash ring.</param>
        /// <param name="hashMapping">The consistent hash mapping function to use on incoming messages.</param>
        public ConsistentHashingGroup(IEnumerable<string> paths, int virtualNodesFactor = 0,
            ConsistentHashMapping hashMapping = null)
            : base(paths)
        {
            VirtualNodesFactor = virtualNodesFactor;
            HashMapping = hashMapping;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingGroup"/> class.
        /// </summary>
        /// <param name="routees">An enumeration of routees used by the group router.</param>
        /// <param name="virtualNodesFactor">The number of virtual nodes to use on the hash ring.</param>
        /// <param name="hashMapping">The consistent hash mapping function to use on incoming messages.</param>
        public ConsistentHashingGroup(IEnumerable<IActorRef> routees, int virtualNodesFactor = 0,
            ConsistentHashMapping hashMapping = null)
            : base(routees)
        {
            VirtualNodesFactor = virtualNodesFactor;
            HashMapping = hashMapping;
        }

        /// <summary>
        /// Creates a new <see cref="ConsistentHashingGroup" /> router with a given <see cref="VirtualNodesFactor"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="vnodes">The <see cref="VirtualNodesFactor"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="vnodes" />.</returns>
        public ConsistentHashingGroup WithVirtualNodesFactor(int vnodes)
        {
            return new ConsistentHashingGroup(Paths, vnodes, HashMapping);
        }

        /// <summary>
        /// Creates a new <see cref="ConsistentHashingGroup"/> router with a given <see cref="ConsistentHashMapping"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="mapping">The <see cref="ConsistentHashMapping"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="mapping"/>.</returns>
        public ConsistentHashingGroup WithHashMapping(ConsistentHashMapping mapping)
        {
            return new ConsistentHashingGroup(Paths, VirtualNodesFactor, mapping);
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return
                new Router(new ConsistentHashingRoutingLogic(system, VirtualNodesFactor,
                    HashMapping ?? ConsistentHashingRouter.EmptyConsistentHashMapping));
        }

        /// <summary>
        /// Creates a new <see cref="ConsistentHashingGroup" /> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Group WithDispatcher(string dispatcher)
        {
            return new ConsistentHashingGroup(Paths, VirtualNodesFactor, HashMapping){ RouterDispatcher = dispatcher};
        }

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <returns>The router configured with the auxiliary information.</returns>
        /// <exception cref="ArgumentException">Expected ConsistentHashingGroup, got <paramref name="routerConfig"/>.</exception>
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
                throw new ArgumentException(string.Format("Expected ConsistentHashingGroup, got {0}", routerConfig),
                    "routerConfig");
            }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="ConsistentHashingGroup"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="ConsistentHashingGroup"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new ConsistentHashingGroupSurrogate
            {
                Paths = Paths,
            };
        }
    }

    /// <summary>
    /// This class represents a <see cref="Pool"/> router that sends messages to a <see cref="Routee"/> determined using consistent-hashing.
    /// Please refer to <see cref="ConsistentHashingRoutingLogic"/> for more information on consistent hashing.
    /// 
    /// <note>
    /// Using <see cref="Resizer"/> with <see cref="ConsistentHashingPool"/> is potentially harmful, as hash ranges
    /// might change radically during live message processing. This router works best with fixed-sized pools or fixed
    /// number of routees per node in the event of clustered deployments.
    /// </note>
    /// </summary>
    public class ConsistentHashingPool : Pool
    {
        /// <summary>
        /// Virtual nodes used in the <see cref="ConsistentHash{T}"/>.
        /// </summary>
        public int VirtualNodesFactor { get; private set; }

        private readonly ConsistentHashMapping _hashMapping;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingPool"/> class.
        /// 
        /// <note>
        /// 'virtual-nodes-factor' defaults to 0 (zero) if it is not defined in the provided configuration.
        /// </note>
        /// </summary>
        /// <param name="config">The configuration used to configure the pool.</param>
        public ConsistentHashingPool(Config config)
            : base(config)
        {
            VirtualNodesFactor = config.GetInt("virtual-nodes-factor", 0);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        /// <param name="resizer">The resizer to use when dynamically allocating routees to the pool.</param>
        /// <param name="supervisorStrategy">The strategy to use when supervising the pool.</param>
        /// <param name="routerDispatcher">The dispatcher to use when passing messages to the routees.</param>
        /// <param name="usePoolDispatcher"><c>true</c> to use the pool dispatcher; otherwise <c>false</c>.</param>
        /// <param name="virtualNodesFactor">The number of virtual nodes to use on the hash ring.</param>
        /// <param name="hashMapping">The consistent hash mapping function to use on incoming messages.</param>
        public ConsistentHashingPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy,
            string routerDispatcher, bool usePoolDispatcher = false, int virtualNodesFactor = 0,
            ConsistentHashMapping hashMapping = null)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            VirtualNodesFactor = virtualNodesFactor;
            _hashMapping = hashMapping;
        }

        /// <summary>
        /// Creates a new <see cref="ConsistentHashingPool" /> router with a given <see cref="VirtualNodesFactor"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="vnodes">The <see cref="VirtualNodesFactor"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="vnodes" />.</returns>
        public ConsistentHashingPool WithVirtualNodesFactor(int vnodes)
        {
            return new ConsistentHashingPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher,
                UsePoolDispatcher, vnodes, _hashMapping);
        }

        /// <summary>
        /// Creates a new <see cref="ConsistentHashingPool"/> router with a given <see cref="ConsistentHashMapping"/>.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="mapping">The <see cref="ConsistentHashMapping"/> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="mapping"/>.</returns>
        public ConsistentHashingPool WithHashMapping(ConsistentHashMapping mapping)
        {
            return new ConsistentHashingPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher,
                UsePoolDispatcher, VirtualNodesFactor, mapping);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingPool"/> class.
        /// 
        /// <note>
        /// A <see cref="ConsistentHashingPool"/> configured in this way uses the <see cref="Pool.DefaultStrategy"/> supervisor strategy.
        /// </note>
        /// </summary>
        /// <param name="nrOfInstances">The initial number of routees in the pool.</param>
        public ConsistentHashingPool(int nrOfInstances) : base(nrOfInstances, null, Pool.DefaultStrategy, null)
        {
        }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return
                new Router(new ConsistentHashingRoutingLogic(system, VirtualNodesFactor,
                    _hashMapping ?? ConsistentHashingRouter.EmptyConsistentHashMapping));
        }

        /// <summary>
        /// Creates a new <see cref="ConsistentHashingPool" /> router with a given <see cref="SupervisorStrategy" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="strategy">The <see cref="SupervisorStrategy" /> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="strategy" />.</returns>
        public override Pool WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            return new ConsistentHashingPool(NrOfInstances, Resizer, strategy, RouterDispatcher, UsePoolDispatcher,
                VirtualNodesFactor, _hashMapping);
        }

        /// <summary>
        /// Creates a new <see cref="ConsistentHashingPool" /> router with a given <see cref="Resizer" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// 
        /// <note>
        /// Using <see cref="Resizer"/> with <see cref="ConsistentHashingPool"/> is potentially harmful, as hash ranges
        /// might change radically during live message processing. This router works best with fixed-sized pools or fixed
        /// number of routees per node in the event of clustered deployments.
        /// </note>
        /// </summary>
        /// <param name="resizer">The <see cref="Resizer" /> used to configure the new router.</param>
        /// <returns>A new router with the provided <paramref name="resizer" />.</returns>
        public override Pool WithResizer(Resizer resizer)
        {
            return new ConsistentHashingPool(NrOfInstances, resizer, SupervisorStrategy, RouterDispatcher,
                UsePoolDispatcher, VirtualNodesFactor, _hashMapping);
        }

        /// <summary>
        /// Creates a new <see cref="ConsistentHashingPool" /> router with a given dispatcher id.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of the router.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher id used to configure the new router.</param>
        /// <returns>A new router with the provided dispatcher id.</returns>
        public override Pool WithDispatcher(string dispatcher)
        {
            return new ConsistentHashingPool(NrOfInstances, Resizer, SupervisorStrategy, dispatcher,
               UsePoolDispatcher, VirtualNodesFactor, _hashMapping);
        }

        /// <summary>
        /// Configure the current router with an auxiliary router for routes that it does not know how to handle.
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <returns>The router configured with the auxiliary information.</returns>
        /// <exception cref="System.ArgumentException">routerConfig</exception>
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            if (routerConfig is FromConfig || routerConfig is NoRouter)
            {
                return OverrideUnsetConfig(routerConfig);
            }
            else if (routerConfig is ConsistentHashingPool)
            {
                var other = routerConfig as ConsistentHashingPool;
                return WithHashMapping(other._hashMapping).OverrideUnsetConfig(other);
            }
            else
            {
                throw new ArgumentException(string.Format("Expected ConsistentHashingPool, got {0}", routerConfig),
                    "routerConfig");
            }
        }

        /// <summary>
        /// This class represents a surrogate of a <see cref="ConsistentHashingPool"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class ConsistentHashingPoolSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="ConsistentHashingPool"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="ConsistentHashingPool"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new ConsistentHashingPool(NrOfInstances, Resizer, SupervisorStrategy, RouterDispatcher, UsePoolDispatcher);
            }

            /// <summary>
            /// The number of routees associated with this pool.
            /// </summary>
            public int NrOfInstances { get; set; }
            /// <summary>
            /// Determine whether or not to use the pool dispatcher. The dispatcher is defined in the
            /// 'pool-dispatcher' configuration property in the deployment section of the router.
            /// </summary>
            public bool UsePoolDispatcher { get; set; }
            /// <summary>
            /// The resizer to use when dynamically allocating routees to the pool.
            /// </summary>
            public Resizer Resizer { get; set; }
            /// <summary>
            /// The strategy to use when supervising the pool.
            /// </summary>
            public SupervisorStrategy SupervisorStrategy { get; set; }
            /// <summary>
            /// The dispatcher to use when passing messages to the routees.
            /// </summary>
            public string RouterDispatcher { get; set; }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="ConsistentHashingPool"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="ConsistentHashingPool"/>.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new ConsistentHashingPoolSurrogate
            {
                NrOfInstances = NrOfInstances,
                UsePoolDispatcher = UsePoolDispatcher,
                Resizer = Resizer,
                SupervisorStrategy = SupervisorStrategy,
                RouterDispatcher = RouterDispatcher,
            };
        }
    }
}
