﻿//-----------------------------------------------------------------------
// <copyright file="ResizablePoolCell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        private readonly Props _routerProps;
        private Pool _pool;

        public ResizablePoolCell(ActorSystemImpl system, IInternalActorRef self, Props routerProps, MessageDispatcher dispatcher, Props routeeProps, IInternalActorRef supervisor, Pool pool)
            : base(system,self, routerProps,dispatcher, routeeProps, supervisor)
        {
            if (pool.Resizer == null) throw new ArgumentException("RouterConfig must be a Pool with defined resizer");

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

        public override void Post(IActorRef sender, object message)
        {
            if(!(_routerProps.RouterConfig.IsManagementMessage(message)) &&
                !(message is ISystemMessage) &&
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

                        var routeesToAbandon = enumerable
                            .Drop(enumerable.Count() + requestedCapacity)
                            .ToList();

                        RemoveRoutees(routeesToAbandon, true);
                    }
                }
                finally
                {
                    _resizeInProgress.Value = false;
                }
        }
    }
}

