//-----------------------------------------------------------------------
// <copyright file="RemoteRouterConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Routing;
using Akka.Util;
using Akka.Util.Internal;
using Akka.Configuration;

namespace Akka.Remote.Routing
{
    /// <summary>
    /// <see cref="RouterConfig"/> implementation for remote deployment of 
    /// routees on defined target nodes. Delegates other duties to the local <see cref="Pool"/>,
    /// which makes it possible to mix this with built-in routers such as <see cref="RoundRobinGroup"/> or custom routers.
    /// </summary>
    public sealed class RemoteRouterConfig : Pool, IEquatable<RouterConfig>
    {
        private readonly IEnumerator<Address> _nodeAddrEnumerator;
        private readonly AtomicCounter _childNameCounter = new AtomicCounter();

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteRouterConfig"/> class.
        /// </summary>
        /// <param name="local">TBD</param>
        /// <param name="nodes">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the enumeration of specified nodes is empty.
        /// </exception>
        public RemoteRouterConfig(Pool local, IEnumerable<Address> nodes) 
            : base(local.NrOfInstances,local.Resizer,local.SupervisorStrategy,local.RouterDispatcher,local.UsePoolDispatcher)
        {
            if (!nodes.Any()) throw new ArgumentException("Must specify list of remote target nodes.", nameof(nodes));

            Local = local;
            Nodes = nodes.ToList();
            _nodeAddrEnumerator = Nodes.GetContinuousEnumerator();
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal Pool Local { get; }

        /// <summary>
        /// TBD
        /// </summary>
        internal IList<Address> Nodes { get; }

        /// <summary>
        /// Creates a router that is responsible for routing messages to routees within the provided <paramref name="system" />.
        /// </summary>
        /// <param name="system">The ActorSystem this router belongs to.</param>
        /// <returns>The newly created router tied to the given system.</returns>
        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override int GetNrOfInstances(ActorSystem system)
        {
            return Local.GetNrOfInstances(system);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routeeProps">TBD</param>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        internal override Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            var name = "c" + _childNameCounter.IncrementAndGet();
            _nodeAddrEnumerator.MoveNext();

            var deploy = new Deploy(routeeProps.RouterConfig, new RemoteScope(_nodeAddrEnumerator.Current));

            // attachChild means that the provider will treat this call as if possibly done out of the wrong
            // context and use RepointableActorRef instead of LocalActorRef. Seems like a slightly sub-optimal
            // choice in a corner case (and hence not worth fixing).
            var actorRef = context.AsInstanceOf<ActorCell>()
                .AttachChild(Local.EnrichWithPoolDispatcher(routeeProps, context).WithDeploy(deploy), false, name);
            return new ActorRefRoutee(actorRef);
        }

        // TODO: why internal?
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        internal override RouterActor CreateRouterActor()
        {
            return Local.CreateRouterActor();
        }

        /// <summary>
        /// Retrieve the strategy to use when supervising the pool.
        /// </summary>
        public override SupervisorStrategy SupervisorStrategy
        {
            get { return Local.SupervisorStrategy; }
        }

        /// <summary>
        /// Dispatcher ID to use for running the "head" actor, which handles supervision, death watch and router management messages.
        /// </summary>
        public override string RouterDispatcher
        {
            get { return Local.RouterDispatcher; }
        }

        /// <summary>
        /// Retrieve the resizer to use when dynamically allocating routees to the pool.
        /// </summary>
        public override Resizer Resizer
        {
            get { return Local.Resizer; }
        }

        /// <summary>
        /// Configures the current router with an auxiliary router for routes that it does not know how to handle.
        /// </summary>
        /// <param name="routerConfig">The router to use as an auxiliary source.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when the specified router is another <see cref="RemoteRouterConfig"/>.
        /// This configuration is not allowed.
        /// </exception>
        /// <returns>The router configured with the auxiliary information.</returns>
        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            var other = routerConfig as RemoteRouterConfig;
            if (other != null && other.Local is RemoteRouterConfig)
                throw new ConfigurationException("RemoteRouterConfig is not allowed to wrap a RemoteRouterConfig");
            if (other != null && other.Local != null)
                return Copy(Local.WithFallback(other.Local).AsInstanceOf<Pool>());
            return Copy(Local.WithFallback(routerConfig).AsInstanceOf<Pool>());
        }

        private RouterConfig Copy(Pool local = null, IEnumerable<Address> nodes = null)
        {
            return new RemoteRouterConfig(local ?? Local, nodes ?? Nodes);
        }

        /// <summary>
        /// Determines whether the specified router, is equal to this instance.
        /// </summary>
        /// <param name="other">The group to compare.</param>
        /// <returns><c>true</c> if the specified router is equal to this instance; otherwise, <c>false</c>.</returns>
        public new bool Equals(RouterConfig other)
        {
            if (!base.Equals(other)) return false;
            var otherRemote = other as RemoteRouterConfig;
            if (otherRemote == null) return false; //should never be true due to the previous check
            return Local.Equals(otherRemote.Local) &&
                   Nodes.Intersect(otherRemote.Nodes).Count() == Nodes.Count;
        }

        /// <summary>
        /// Creates a surrogate representation of the current router.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current router.</returns>
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
            return new RemoteRouterConfigSurrogate
            {
                Local = Local,
                Nodes = Nodes.ToArray(),
            };
        }

        /// <summary>
        /// This class represents a surrogate of a <see cref="RemoteRouterConfig"/> router.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class RemoteRouterConfigSurrogate : ISurrogate
        {
            /// <summary>
            /// TBD
            /// </summary>
            public Pool Local { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public Address[] Nodes { get; set; }

            /// <summary>
            /// Creates a <see cref="RemoteRouterConfig"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="RemoteRouterConfig"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new RemoteRouterConfig(Local, Nodes);
            }
        }
    }
}

