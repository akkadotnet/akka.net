using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Routing
{
    public class ResizablePoolCell : RoutedActorCell
    {
        /// <summary>
        /// State of the resize in progress. Since I can't use bool for interlocked ops I choose to use ints.
        /// </summary>
        private static class ResizeInProgressState
        {
            /// <summary>
            /// True
            /// </summary>
            public static int True = 1;
            
            /// <summary>
            /// False
            /// </summary>
            public static int False = 0;
        }

        private Resizer resizer;
        /// <summary>
        /// must always use ResizeInProgressState static class to compare or assign values
        /// </summary>
        private volatile int resizeInProgress;
        private long resizeCounter;
        private Pool pool;

        public ResizablePoolCell(ActorSystem system, InternalActorRef supervisor, Props routerProps,
            Props routeeProps, ActorPath path, Mailbox mailbox, Pool pool)
            : base(system, supervisor, routerProps, routeeProps, path, mailbox)
        {
            if (pool.Resizer == null)
                throw new ArgumentException("RouterConfig must be a Pool with defined resizer");

            resizer = pool.Resizer;
            this.pool = pool;
            this.resizeCounter = 0;
            this.resizeInProgress = ResizeInProgressState.False;
        }

        protected override void PreStart()
        {
            // initial resize, before message send
            if (resizer.IsTimeForResize(Interlocked.Increment(ref resizeCounter) - 1))
            {
                Resize(true);
            }
            base.PreStart();
        }

        internal override void Post(ActorRef sender, object message)
        {
            if (!(RouterConfig.IsManagementMessage(message)) &&
                resizer.IsTimeForResize(Interlocked.Increment(ref resizeCounter) - 1) &&
                Interlocked.Exchange(ref resizeInProgress, ResizeInProgressState.True) == ResizeInProgressState.False)
            {
                base.Post(Self, new Resize());
                
            }
            base.Post(sender, message);
        }

        internal void Resize(bool initial)
        {
            if (resizeInProgress == ResizeInProgressState.True || initial)
                try
                {
                    var requestedCapacity = resizer.Resize(Router.Routees);
                    if (requestedCapacity > 0)
                    {
                        var newRoutees = new List<Routee>();
                        for (var i = 0; i < requestedCapacity; i++)
                        {
                            newRoutees.Add(pool.NewRoutee(RouteeProps, this));
                        }
                        AddRoutees(newRoutees.ToArray());
                    }
                    else if (requestedCapacity < 0)
                    {
                        var currentRoutees = Router.Routees;
                        var enumerable = currentRoutees as Routee[] ?? currentRoutees.ToArray();
                        var routeesToAbandon = enumerable.Drop(enumerable.Count() + requestedCapacity);
                        foreach (var routee in routeesToAbandon.OfType<ActorRefRoutee>())
                        {
                            RemoveRoutee(routee.Actor, true);
                        }
                    }
                }
                finally
                {
                    resizeInProgress = ResizeInProgressState.False;
                }
        }
    }
}
