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
    /// <summary>
    /// INTERNAL API
    /// 
    /// <see cref="IActorRef"/> implementation for <see cref="Router"/> instances.
    /// </summary>
    public class RoutedActorRef : RepointableActorRef
    {
        private readonly Props _routeeProps;

        public RoutedActorRef(ActorSystemImpl system, Props routerProps, MessageDispatcher routerDispatcher,
            MailboxType mailboxType, Props routeeProps, IInternalActorRef supervisor, ActorPath path)
            : base(system, routerProps, routerDispatcher, mailboxType, supervisor, path)
        {
            _routeeProps = routeeProps;

            //TODO: Implement:
            // // verify that a BalancingDispatcher is not used with a Router
            // if (!(routerProps.RouterConfig is NoRouter) && routerDispatcher is BalancingDispatcher)
            // {
            //     throw new ConfigurationException("Configuration for " + this +
            //                                 " is invalid - you can not use a 'BalancingDispatcher' as a Router's dispatcher, you can however use it for the routees.");
            // }
            // routerProps.RouterConfig.VerifyConfig(path);
        }

        protected override ActorCell NewCell()
        {
            var pool = Props.RouterConfig as Pool;
            ActorCell cell = null;
            if(pool != null)
            {
                if(pool.Resizer != null)
                {
                    //if there is a resizer, use ResizablePoolCell
                    cell = new ResizablePoolCell(System, this, Props, Dispatcher, _routeeProps, Supervisor, pool);
                }
            }
            if(cell == null)
                cell = new RoutedActorCell(System, this, Props, Dispatcher, _routeeProps, Supervisor);
            cell.Init(false,MailboxType);
            return cell;
        }
    }
}

