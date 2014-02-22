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
        public string TypeName { get; protected set; }
        public Type Type { get; protected set; }
        public string Dispatcher { get; protected set; }
        public string Mailbox { get; protected set; }
        public RouterConfig RouterConfig { get; protected set; }
        public Deploy Deploy { get; protected set; }
        public SupervisorStrategy SupervisorStrategy { get; protected set; }

        public static Props Create<TActor>(Expression<Func<TActor>> factory) where TActor : ActorBase
        {
            if (factory.Body is UnaryExpression)
                return new DynamicProps<TActor>(factory.Compile());

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

        public Props()
        {
            this.RouterConfig = RouterConfig.NoRouter;
            this.Arguments = new object[] { };
            this.Dispatcher = "akka.actor.default-dispatcher";
            this.Mailbox = "akka.actor.default-mailbox";
        }

        protected Props(Type type, object[] args)
            : this()
        {
            this.Type = type;
            this.TypeName = type.AssemblyQualifiedName;
            this.Arguments = args;
        }
        protected Props(Type type)
            : this()
        {
            this.Type = type;
            this.TypeName = type.AssemblyQualifiedName;
            this.Arguments = new object[] { };
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

        public Props WithRouter(RouterConfig routerConfig)
        {
            var copy = Copy();
            copy.Type = typeof(RouterActor);
            copy.RouterConfig = routerConfig;
            return copy;
        }

        public Props WithDeploy(Deploy deploy)
        {
            var copy = Copy();
            copy.Deploy = deploy;
            return copy;
        }

        public Props WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            var copy = Copy();
            copy.SupervisorStrategy = strategy;
            return copy;
        }

        //TODO: use Linq Expressions so compile a creator
        //cache the creator
        public virtual ActorBase NewActor()
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

        protected virtual Props Copy()
        {
            return new Props()
            {
                Arguments = this.Arguments,
                Dispatcher = this.Dispatcher,
                Mailbox = this.Mailbox,
                RouterConfig = this.RouterConfig,
                Type = this.Type,
                Deploy = this.Deploy,
                SupervisorStrategy = this.SupervisorStrategy
            };
        }

        public object[] Arguments { get; private set; }
    }

    /// <summary>
    /// Props instance that uses dynamic invocation to create new Actor instances,
    /// rather than a traditional Activator. 
    /// 
    /// Intended to be used in conjunction with Dependency Injection.
    /// </summary>
    class DynamicProps<TActor> : Props where TActor : ActorBase
    {
        private readonly Func<TActor> _invoker;

        public DynamicProps(Func<TActor> invoker)
            : base(typeof(TActor))
        {
            _invoker = invoker;
        }

        public override ActorBase NewActor()
        {
            return _invoker.Invoke();
        }

        #region Copy methods

        /// <summary>
        /// Copy constructor
        /// </summary>
        protected DynamicProps(Props copy, Func<TActor> invoker)
        {
            Dispatcher = copy.Dispatcher;
            Mailbox = copy.Mailbox;
            RouterConfig = copy.RouterConfig;
            Type = copy.Type;
            Deploy = copy.Deploy;
            _invoker = invoker;
        }

        protected override Props Copy()
        {
            var initialCopy = base.Copy();
            var invokerCopy = (Func<TActor>) _invoker.Clone();
            return new DynamicProps<TActor>(initialCopy, invokerCopy);
        }

        #endregion
    }
}
