﻿//-----------------------------------------------------------------------
// <copyright file="ResizablePoolCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
            if (pool.Resizer == null) throw new ArgumentException("RouterConfig must be a Pool with defined resizer");

            resizer = pool.Resizer;
            _pool = pool;
            _resizeCounter = new AtomicCounterLong(0);
            _resizeInProgress = new AtomicBoolean();
        }

        protected override void PreSuperStart()
        {
            // initial resize, before message send
            if (resizer.IsTimeForResize(_resizeCounter.GetAndIncrement()))
            {
                Resize(initial: true);
            }
        }

        public override void SendMessage(IActorRef sender, object message)
        {
            if(!(RouterConfig.IsManagementMessage(message)) &&
                resizer.IsTimeForResize(_resizeCounter.GetAndIncrement()) &&
                _resizeInProgress.CompareAndSet(false, true))
            {
                base.SendMessage(Self, new Resize());
                
            }

            base.SendMessage(sender, message);
        }

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
