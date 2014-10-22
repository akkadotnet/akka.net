﻿using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;

namespace Akka.Routing
{
    public interface ConsistentHashable
    {
        object ConsistentHashKey { get; }
    }

    public class ConsistentHashableEnvelope : ConsistentHashable
    {
        public ConsistentHashableEnvelope(object message,object hashKey)
        {
            this.Message = message;
            this.HashKey = hashKey;
        }
        public object Message { get;private set; }
        public object HashKey { get;private set; }

        public object ConsistentHashKey
        {
            get { return HashKey; }
        }
    }

    public class ConsistentHashingRoutingLogic : RoutingLogic
    {
        private readonly Lazy<LoggingAdapter> _log;
        private Dictionary<Type, Func<object, object>> _hashMapping;
        private ActorSystem _system;
        public override Routee Select(object message, Routee[] routees)
        {
            if (message == null)
                return NoRoutee.NoRoutee;

            if (_hashMapping.ContainsKey(message.GetType()))
            {
                var key = _hashMapping[message.GetType()](message);
                if (key == null)
                    return NoRoutee.NoRoutee;

                var hash = Murmur3.Hash(key);
                if (routees.Length == 0) return Routee.NoRoutee; //avoid divide by zero error (can happen in Akka.Cluster routers)
                return routees[Math.Abs(hash) % routees.Length]; //The hash might be negative, so we have to make sure it's positive
            }
            else if (message is ConsistentHashable)
            {
                var hashable = (ConsistentHashable) message;
                int hash = Murmur3.Hash(hashable.ConsistentHashKey);
                if (routees.Length == 0) return Routee.NoRoutee; //avoid divide by zero error (can happen in Akka.Cluster routers)
                return routees[Math.Abs(hash) % routees.Length]; //The hash might be negative, so we have to make sure it's positive
            }
            else
            {
                _log.Value.Warning("Message [{0}] must be handled by hashMapping, or implement [{1}] or be wrapped in [{2}]", message.GetType().Name, typeof(ConsistentHashable).Name, typeof(ConsistentHashableEnvelope).Name);
                return Routee.NoRoutee;
            }
        }

        public ConsistentHashingRoutingLogic(ActorSystem system) : this(system,new Dictionary<Type,Func<object,object>>())
        {
        }

        private ConsistentHashingRoutingLogic(ActorSystem system, Dictionary<Type, Func<object, object>> hashMapping)
        {            
            _system = system;
            _log = new Lazy<LoggingAdapter>(() => Logging.GetLogger(_system, this), true);
            _hashMapping = hashMapping;
        }
   

        public ConsistentHashingRoutingLogic WithHashMapping<T>(Func<T,object> mapping)
        {
            if (mapping == null)
                throw new ArgumentNullException("mapping");

            var copy = new Dictionary<Type, Func<object, object>>(_hashMapping);
            copy.Add(typeof(T), o => mapping((T)o));
            return new ConsistentHashingRoutingLogic(_system,copy);
        }
    }

    public class ConsistentHashingGroup : Group
    {
        [Obsolete("For serialization only",true)]
        public ConsistentHashingGroup()
        {
        }

        public ConsistentHashingGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {
        }

        public ConsistentHashingGroup(params string[] paths)
            : base(paths)
        {
        }

        public ConsistentHashingGroup(IEnumerable<string> paths)
            : base(paths)
        {
        }

        public ConsistentHashingGroup(IEnumerable<ActorRef> routees) : base(routees)
        {
        }

        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ConsistentHashingRoutingLogic(system));
        }
    }

    public class ConsistentHashingPool : Pool
    {

                /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingPool"/> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        public ConsistentHashingPool(Config config) : base(config)
        {
            
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="ConsistentHashingPool"/> class.
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        /// <param name="resizer">The resizer.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="routerDispatcher">The router dispatcher.</param>
        /// <param name="usePoolDispatcher">if set to <c>true</c> [use pool dispatcher].</param>
        public ConsistentHashingPool(int nrOfInstances, Resizer resizer,SupervisorStrategy supervisorStrategy, string routerDispatcher, bool usePoolDispatcher = false)
            : base(nrOfInstances, resizer, supervisorStrategy, routerDispatcher, usePoolDispatcher)
        {
            
        }

        /// <summary>
        /// Simple form of ConsistentHashingPool constructor
        /// </summary>
        /// <param name="nrOfInstances">The nr of instances.</param>
        public ConsistentHashingPool(int nrOfInstances) : base(nrOfInstances, null, Pool.DefaultStrategy, null) { }

        public override Router CreateRouter(ActorSystem system)
        {
            return new Router(new ConsistentHashingRoutingLogic(system));
        }
    }
}