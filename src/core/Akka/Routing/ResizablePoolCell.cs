//-----------------------------------------------------------------------
// <copyright file="ResizablePoolCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <param name="system">The actor system that owns the router.</param>
        /// <param name="self">The router actor reference backed by this cell.</param>
        /// <param name="routerProps">The properties used to create the router actor.</param>
        /// <param name="dispatcher">The dispatcher used by the router actor.</param>
        /// <param name="routeeProps">The properties used to create pool routees.</param>
        /// <param name="supervisor">The actor supervising this router.</param>
        /// <param name="pool">The pool configuration that supplies the resizer and routee creation behavior.</param>
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
            resizer = pool.Resizer ?? throw new ArgumentException("RouterConfig must be a Pool with defined resizer", nameof(pool));
            _pool = pool;
            _resizeCounter = new AtomicCounterLong(0);
            _resizeInProgress = new AtomicBoolean();
        }

        /// <summary>
        /// Performs the initial resize before the router begins processing messages.
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
        /// Schedules a resize check when the message count reaches the configured interval, then routes the message.
        /// </summary>
        /// <param name="envelope">The message envelope to route or process as management traffic.</param>
        public override void SendMessage(Envelope envelope)
        {
            if (!(RouterConfig.IsManagementMessage(envelope.Message)) &&
                resizer.IsTimeForResize(_resizeCounter.GetAndIncrement()) &&
                _resizeInProgress.CompareAndSet(false, true))
            {
                base.SendMessage(new Envelope(new Resize(), Self, System));
            }

            base.SendMessage(envelope);
        }

        /// <summary>
        /// Applies the resizer's requested routee-count change.
        /// </summary>
        /// <param name="initial">Whether this is the initial resize performed before message processing.</param>
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
