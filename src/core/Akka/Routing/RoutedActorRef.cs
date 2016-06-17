//-----------------------------------------------------------------------
// <copyright file="RoutedActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch;

namespace Akka.Routing
{
    internal class RoutedActorRef : RepointableActorRef
    {
        private readonly Props _routeeProps;

        public RoutedActorRef(
            ActorSystemImpl system,
            Props routerProps,
            MessageDispatcher routerDispatcher,
            MailboxType routerMailbox,
            Props routeeProps,
            IInternalActorRef supervisor,
            ActorPath path)
            : base(system, routerProps, routerDispatcher, routerMailbox, supervisor, path)
        {
            _routeeProps = routeeProps;
            routerProps.RouterConfig.VerifyConfig(path);
        }

        protected override ActorCell NewCell()
        {
            var pool = Props.RouterConfig as Pool;
            ActorCell cell = null;
            if (pool != null)
            {
                if (pool.Resizer != null)
                {
                    cell = new ResizablePoolCell(System, this, Props, Dispatcher, _routeeProps, Supervisor, pool);
                }
            }
            if (cell == null)
            {
                cell = new RoutedActorCell(System, this, Props, Dispatcher, _routeeProps, Supervisor);
            }
            cell.Init(false, MailboxType);
            return cell;
        }
    }
}
