using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Routing;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote.Routing
{
    /// <summary>
    /// <see cref="RouterConfig"/> implementation for remote deployment of 
    /// routees on defined target nodes. Delegates other duties to the local <see cref="Pool"/>,
    /// which makes it possible to mix this with built-in routers such as <see cref="RoundRobinGroup"/> or custom routers.
    /// </summary>
    public sealed class RemoteRouterConfig : Pool
    {
        public override ISurrogate ToSurrogate(ActorSystem system)
        {
           throw  new NotImplementedException();
        }

        internal readonly Pool Local;
        internal readonly IList<Address> Nodes;

        /// <summary>
        /// Used for distributing routees to <see cref="Nodes"/>. Needs to be an instance variable since <see cref="Resizer"/> may call <see cref="RoutedActorCell.AddRoutees"/> several times.
        /// </summary>
        private readonly IEnumerator<Address> _nodeAddrEnumerator;

        /// <summary>
        /// Used for naming child routees. Needs to be an instance variable since <see cref="Resizer"/> may call <see cref="RoutedActorCell.AddRoutees"/> several times.
        /// </summary>
        private readonly AtomicCounter _childNameCounter = new AtomicCounter();

        public RemoteRouterConfig(Pool local, IEnumerable<Address> nodes) : base(local.NrOfInstances,local.Resizer,local.SupervisorStrategy,local.RouterDispatcher,local.UsePoolDispatcher)
        {
            
            Local = local;
            Nodes = nodes.ToList();
            if (!Nodes.Any()) throw new ArgumentException("Must specify list of remote target nodes.", "nodes");
            _nodeAddrEnumerator = Nodes.GetContinuousEnumerator();
        }

        #region Trivial method overrides

        internal override RouterActor CreateRouterActor()
        {
            return Local.CreateRouterActor();
        }

        public override Router CreateRouter(ActorSystem system)
        {
            return Local.CreateRouter(system);
        }

        #endregion

        #region Equality overrides

        public override bool Equals(RouterConfig other)
        {
            if (!base.Equals(other)) return false;
            var otherRemote = other as RemoteRouterConfig;
            if (otherRemote == null) return false; //should never be true due to the previous check
            return Local.Equals(otherRemote.Local) &&
                   Nodes.Intersect(otherRemote.Nodes).Count() == Nodes.Count;
        }

        #endregion

        #region RemoteRouterConfig core methods

        public override Routee NewRoutee(Props routeeProps, IActorContext context)
        {
            _nodeAddrEnumerator.MoveNext();
            var name = "c" + _childNameCounter.GetAndIncrement();
            var deploy = new Deploy(routeeProps.RouterConfig, new RemoteScope(_nodeAddrEnumerator.Current));
            

            var actorRef = context.AsInstanceOf<ActorCell>()
                .AttachChild(Local.EnrichWithPoolDispatcher(routeeProps, context).WithDeploy(deploy), false, name);
            return new ActorRefRoutee(actorRef);
        }

        public override RouterConfig WithFallback(RouterConfig routerConfig)
        {
            var other = routerConfig as RemoteRouterConfig;
            if(other != null && other.Local is RemoteRouterConfig)
                throw new ArgumentException("RemoteRouterConfig is not allowed to wrap a RemoteRouterConfig", "routerConfig");
            if (other != null && other.Local != null)
                return Copy(Local.WithFallback(other.Local).AsInstanceOf<Pool>());
            return Copy(Local.WithFallback(routerConfig).AsInstanceOf<Pool>());
        }

        public RouterConfig Copy(Pool local = null, IEnumerable<Address> nodes = null)
        {
            return new RemoteRouterConfig(local ?? Local, nodes ?? Nodes);
        }

        #endregion
    }
}
