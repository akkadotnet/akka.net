//-----------------------------------------------------------------------
// <copyright file="ResizablePoolCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
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
        private Pool _pool;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResizablePoolCell"/> class.
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="self">TBD</param>
        /// <param name="routerProps">TBD</param>
        /// <param name="dispatcher">TBD</param>
        /// <param name="routeeProps">TBD</param>
        /// <param name="supervisor">TBD</param>
        /// <param name="pool">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if pool's resizer is undefined.
        /// </exception>
        public ResizablePoolCell(
            ActorSystemImpl system,
            IInternalActorRef self,
            Props routerProps,
            MessageDispatcher dispatcher,
            Props routeeProps,
            IInternalActorRef supervisor,
            Pool pool)
            : base(system, self, routerProps, dispatcher, routeeProps, supervisor)
        {
            if (pool.Resizer == null) throw new ArgumentException("RouterConfig must be a Pool with defined resizer", nameof(pool));

            resizer = pool.Resizer;
            _pool = pool;
            _resizeCounter = new AtomicCounterLong(0);
            _resizeInProgress = new AtomicBoolean();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreSuperStart()
        {
            // initial resize, before message send
            if (resizer.IsTimeForResize(_resizeCounter.GetAndIncrement()))
            {
                Resize(initial: true);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        public override void SendMessage(Envelope envelope)
        {
            if(!(RouterConfig.IsManagementMessage(envelope.Message)) &&
                resizer.IsTimeForResize(_resizeCounter.GetAndIncrement()) &&
                _resizeInProgress.CompareAndSet(false, true))
            {
                base.SendMessage(new Envelope(new Resize(), Self, System));
            }

            base.SendMessage(envelope);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initial">TBD</param>
        internal void Resize(bool initial)
        {
            if (_resizeInProgress.Value || initial)
            {
                try
                {
                    var requestedCapacity = resizer.Resize(Router.Routees);
                    if (requestedCapacity > 0)
                    {
                        var newRoutees = Vector.Fill<Routee>(requestedCapacity)(() => _pool.NewRoutee(RouteeProps, this));
                        AddRoutees(newRoutees);
                    }
                    else if (requestedCapacity < 0)
                    {
                        var currentRoutees = Router.Routees.ToList();

                        var abandon = currentRoutees
                            .Drop(currentRoutees.Count + requestedCapacity)
                            .ToList();

                        RemoveRoutees(abandon, stopChild: true);
                    }
                }
                finally
                {
                    _resizeInProgress.Value = false;
                }
            }
        }
    }
}
