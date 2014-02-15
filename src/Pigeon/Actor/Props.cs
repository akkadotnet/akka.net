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

        public RouterConfig RouterConfig { get; private set; }

        public static Props Create<TActor>(Func<TActor> factory) where TActor : ActorBase
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
        public string DispatcherPath { get; private set; }
        public string MailboxPath { get; private set; }
        public string RouterPath { get; private set; }

        public Props()
        {
            this.DispatcherPath = "akka.actor.default-dispatcher";
            this.MailboxPath = "akka.actor.default-mailbox";
            this.RouterPath = null;
        }

        private Props(Type type, Func<ActorBase> factory)
            : this()
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

        public Props WithRouter(string path)
        {
            this.RouterPath = path;
            return this;
        }

        public Props WithMailbox(string path)
        {
            this.MailboxPath = path;
            return this;
        }

        public Props WithDispatcher(string path)
        {
            this.DispatcherPath = path;
            return this;
        }

        public Props WithRouter(RouterConfig routerConfig)
        {
            this.Type = typeof(RouterActor);
            this.factory = () => new RouterActor();
            this.RouterConfig = routerConfig;
            return this;
        }

        public ActorBase NewActor()
        {
            return this.factory();
        }

        public Props Empty
        {
            get
            {
                return new Props();
            }
        }
    }
}
