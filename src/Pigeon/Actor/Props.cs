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
        public string Dispatcher { get; private set; }
        public string Mailbox { get; private set; }
        public string Router { get; private set; }

        public Props()
        {
            this.Dispatcher = "akka.actor.default-dispatcher";
            this.Mailbox = "akka.actor.default-mailbox";
            this.Router = null;
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
            var copy = Copy();
            copy.Router = path;
            return copy;
        }

        public Props WithMailbox(string path)
        {
            var copy = Copy();
            copy.Mailbox = path;
            return copy;
        }

        public Props WithDispatcher(string path)
        {
            var copy = Copy();
            copy.Dispatcher = path;
            return copy;
        }

        [Obsolete("Just for dev, should be replaced")]
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

        private static Props empty = new Props();
        public Props Empty
        {
            get
            {
                return empty;
            }
        }

        private Props Copy()
        {
            return new Props()
            {
                factory = this.factory,
                Dispatcher = this.Dispatcher,
                Mailbox = this.Mailbox,
                RouterConfig = this.RouterConfig,
                Router = this.Router,
                Type = this.Type,
            };
        }
    }
}
