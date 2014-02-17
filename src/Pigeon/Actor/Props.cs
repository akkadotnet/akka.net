using Pigeon.Dispatch;
using Pigeon.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Reflection;

namespace Pigeon.Actor
{
    public class Props
    {
        public string TypeName { get;private set; }
        public Type Type { get; private set; }

        public RouterConfig RouterConfig { get; private set; }

        public static Props Create<TActor>(Expression<Func<TActor>> factory) where TActor : ActorBase
        {
            var newExpression = factory.Body.AsInstanceOf<NewExpression>();
            if (newExpression == null)
                throw new ArgumentException("The create function must be a 'new T (args)' expression");

            var args = newExpression.GetArguments().ToArray();
            
            return new Props(typeof(TActor), args);
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
            this.Arguments = new object[] { };
            this.Dispatcher = "akka.actor.default-dispatcher";
            this.Mailbox = "akka.actor.default-mailbox";
            this.Router = null;
        }

        private Props(Type type, object[] args)
            : this()
        {
            this.Type = type;
            this.TypeName = type.AssemblyQualifiedName;
            this.Arguments = args;
        }
        private Props(Type type)
            : this()
        {
            this.Type = type;
            this.TypeName = type.AssemblyQualifiedName;
            this.Arguments = new object[] { };
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
            this.RouterConfig = routerConfig;
            return this;
        }

        public ActorBase NewActor()
        {
            return (ActorBase)Activator.CreateInstance(Type, Arguments);
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
                Arguments = this.Arguments,
                Dispatcher = this.Dispatcher,
                Mailbox = this.Mailbox,
                RouterConfig = this.RouterConfig,
                Router = this.Router,
                Type = this.Type,
            };
        }

        public object[] Arguments { get;private set; }
    }
}
