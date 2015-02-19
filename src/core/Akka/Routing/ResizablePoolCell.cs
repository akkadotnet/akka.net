using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Dispatch;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ResizablePoolCell : RoutedActorCell
    {
        private Resizer resizer;
        /// <summary>
        /// must always use ResizeInProgressState static class to compare or assign values
        /// </summary>
        private AtomicBoolean _resizeInProgress;
        private AtomicCounterLong _resizeCounter;
        private readonly Props _routerProps;
        private Pool _pool;

        public ResizablePoolCell(ActorSystemImpl system, InternalActorRef self, Props routerProps, MessageDispatcher dispatcher, Props routeeProps, InternalActorRef supervisor, Pool pool)
            : base(system,self, routerProps,dispatcher, routeeProps, supervisor)
        {

            Guard.Assert(pool.Resizer != null, "RouterConfig must be a Pool with defined resizer");

            resizer = pool.Resizer;
            _routerProps = routerProps;
            _pool = pool;
            _resizeCounter = new AtomicCounterLong(0);
            _resizeInProgress = new AtomicBoolean();
        }

        protected override void PreSuperStart()
        {
            // initial resize, before message send
            if (resizer.IsTimeForResize(_resizeCounter.GetAndIncrement()))
            {
                Resize(true);
            }
        }

        public override void Post(ActorRef sender, object message)
        {
            if(!(_routerProps.RouterConfig.IsManagementMessage(message)) &&
                resizer.IsTimeForResize(_resizeCounter.GetAndIncrement()) &&
                _resizeInProgress.CompareAndSet(false, true))
            {
                base.Post(Self, new Resize());
                
            }
            base.Post(sender, message);
        }

        internal void Resize(bool initial)
        {
            if (_resizeInProgress.Value || initial)
                try
                {
                    var requestedCapacity = resizer.Resize(Router.Routees);
                    if (requestedCapacity > 0)
                    {
                        var newRoutees = new List<Routee>();
                        for (var i = 0; i < requestedCapacity; i++)
                        {
                            newRoutees.Add(_pool.NewRoutee(RouteeProps, this));
                        }
                        AddRoutees(newRoutees.ToArray());
                    }
                    else if (requestedCapacity < 0)
                    {
                        var currentRoutees = Router.Routees;
                        var enumerable = currentRoutees as Routee[] ?? currentRoutees.ToArray();
                        var routeesToAbandon = enumerable.Drop(enumerable.Count() + requestedCapacity);
                        RemoveRoutees(routeesToAbandon, true);
                    }
                }
                finally
                {
                    _resizeInProgress = false;
                }
        }
    }
}
