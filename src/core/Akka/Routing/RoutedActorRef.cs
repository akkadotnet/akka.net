//-----------------------------------------------------------------------
// <copyright file="RoutedActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch;

namespace Akka.Routing
{
    /// <summary>
    /// Actor reference that routes messages to a configured set of routees.
    /// </summary>
    internal class RoutedActorRef : RepointableActorRef
    {
        private readonly Props _routeeProps;

        /// <summary>
        /// Initializes a new instance of the <see cref="RoutedActorRef"/> class.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <param name="routerProps">The properties used to configure the router.</param>
        /// <param name="routerDispatcher">The dispatcher used by the router actor.</param>
        /// <param name="routerMailbox">The mailbox type used by the router actor.</param>
        /// <param name="routeeProps">The properties used to create pool routees.</param>
        /// <param name="supervisor">The actor supervising this router.</param>
        /// <param name="path">The actor path assigned to the router.</param>
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

        /// <summary>
        /// Creates the cell used to manage the router and its routees.
        /// </summary>
        /// <returns>A routed actor cell, with a resizable pool cell when the pool defines a resizer.</returns>
        protected override ActorCell NewCell()
        {
            ActorCell cell = Props.RouterConfig is Pool pool && pool.Resizer != null
                ? new ResizablePoolCell(System, this, Props, Dispatcher, _routeeProps, Supervisor, pool)
                : new RoutedActorCell(System, this, Props, Dispatcher, _routeeProps, Supervisor);

            cell.Init(false, MailboxType);
            return cell;
        }
    }
}
