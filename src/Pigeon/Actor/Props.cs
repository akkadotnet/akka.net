using Pigeon.Dispatch;
using Pigeon.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class Props
    {
        private Func<ActorBase> factory;
        public Type Type { get; private set; }

        public MessageDispatcher Dispatcher { get;private set; }

        public RouterConfig RouterConfig { get; private set; }

        public Type MailboxType { get; private set; }

        public static Props Create<TActor> (Func<TActor> factory) where TActor : ActorBase
        {
            return new Props(typeof(TActor), factory);
        }
        public static Props Create<TActor>() where TActor : ActorBase
        {
            return new Props(typeof(TActor));
        }
        public static Props Create(Type type)
        {
            return new Props(type);
        }

        public Props()
        {
            this.MailboxType = typeof(ConcurrentQueueMailbox);
        }

        private Props(Type type, Func<ActorBase> factory) : this()
        {            
            this.factory = factory;
            this.Type = type;
        }
        private Props(Type type)
            : this()
        {
            this.factory = () => (ActorBase)Activator.CreateInstance(this.Type, new object[] { });
            this.Type = type;
        }

        public Props WithDispatcher(MessageDispatcher dispatcher)
        {
            this.Dispatcher = dispatcher;
            return this;
        }

        public Props WithRouter(RouterConfig routerConfig)
        {
            this.Type = typeof(RouterActor);
            this.factory = () => new RouterActor();
            this.RouterConfig = routerConfig;
            return this;
        }

        public Props WithMailbox<T>() where T : Mailbox
        {
            this.MailboxType = typeof(T);
            return this;
        }

        public ActorBase NewActor()
        {
            return this.factory();
        }
    }
}
