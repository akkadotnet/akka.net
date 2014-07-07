using System;
using System.Configuration;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;

namespace Akka.Routing
{
    public class RoutedActorRef : RepointableActorRef
    {
        private readonly ActorSystem _system;
        private readonly Props _routerProps;
        private readonly MessageDispatcher _routerDispatcher;
        private readonly Func<Mailbox> _createMailbox;
        private readonly Props _routeeProps;
        private readonly InternalActorRef _supervisor;

        public RoutedActorRef(ActorSystem system, Props routerProps, MessageDispatcher routerDispatcher,
            Func<Mailbox> createMailbox, Props routeeProps, InternalActorRef supervisor, ActorPath path)
            : base(system, routerProps, routerDispatcher, createMailbox, supervisor, path)
        {
            _system = system;
            _routerProps = routerProps;
            _routerDispatcher = routerDispatcher;
            _createMailbox = createMailbox;
            _routeeProps = routeeProps;
            _supervisor = supervisor;
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
            var pool = _routerProps.RouterConfig as Pool;
            ActorCell cell = null;
            if(pool != null)
            {
                if(pool.Resizer != null)
                {
                    //if there is a resizer, use ResizablePoolCell
                    cell = new ResizablePoolCell(_system, this, _routerProps, _routerDispatcher, _routeeProps, _supervisor, pool);
                }
            }
            if(cell == null)
                cell = new RoutedActorCell(_system, this, _routerProps, _routerDispatcher, _routeeProps, _supervisor);
            cell.Init(sendSupervise: false, createMailbox: _createMailbox);
            return cell;
        }
    }
}