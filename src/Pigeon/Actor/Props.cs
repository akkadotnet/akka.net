using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Akka.Reflection;
using Akka.Routing;

namespace Akka.Actor
{
    /// <summary>
    ///     Props is a configuration object using in creating an [[Actor]]; it is
    ///     immutable, so it is thread-safe and fully shareable.
    ///     Examples on C# API:
    ///     <code>
    ///   private Props props = Props.Empty();
    ///   private Props props = Props.Create(() => new MyActor(arg1, arg2));
    /// 
    ///   private Props otherProps = props.WithDispatcher("dispatcher-id");
    ///   private Props otherProps = props.WithDeploy(deployment info);
    ///  </code>
    /// </summary>
    public class Props
    {
        /// <summary>
        ///     A Props instance whose creator will create an actor that doesn't respond to any message
        /// </summary>
        private static readonly Props empty = new Props();

        /// <summary>
        ///     The none
        /// </summary>
        public static readonly Props None = null;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        public Props()
        {
            RouterConfig = RouterConfig.NoRouter;
            Arguments = new object[] {};
            Dispatcher = "akka.actor.default-dispatcher";
            Mailbox = "akka.actor.default-mailbox";
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="args">The arguments.</param>
        protected Props(Type type, object[] args)
            : this()
        {
            Type = type;
            TypeName = type.AssemblyQualifiedName;
            Arguments = args;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type.</param>
        protected Props(Type type)
            : this()
        {
            Type = type;
            TypeName = type.AssemblyQualifiedName;
            Arguments = new object[] {};
        }

        public Props(Deploy deploy, Type type, IEnumerable<object> args)
        {
            Deploy = deploy;
            Type = type;
            Arguments = args.ToArray();
        }

        /// <summary>
        ///     Gets or sets the name of the type.
        /// </summary>
        /// <value>The name of the type.</value>
        public string TypeName { get; protected set; }

        /// <summary>
        ///     Gets or sets the type.
        /// </summary>
        /// <value>The type.</value>
        public Type Type { get; protected set; }

        /// <summary>
        ///     Gets or sets the dispatcher.
        /// </summary>
        /// <value>The dispatcher.</value>
        public string Dispatcher { get; protected set; }

        /// <summary>
        ///     Gets or sets the mailbox.
        /// </summary>
        /// <value>The mailbox.</value>
        public string Mailbox { get; protected set; }

        /// <summary>
        ///     Gets or sets the router configuration.
        /// </summary>
        /// <value>The router configuration.</value>
        public RouterConfig RouterConfig { get; protected set; }

        /// <summary>
        ///     Gets or sets the deploy.
        /// </summary>
        /// <value>The deploy.</value>
        public Deploy Deploy { get; protected set; }

        /// <summary>
        ///     Gets or sets the supervisor strategy.
        /// </summary>
        /// <value>The supervisor strategy.</value>
        public SupervisorStrategy SupervisorStrategy { get; protected set; }

        /// <summary>
        ///     A Props instance whose creator will create an actor that doesn't respond to any message
        /// </summary>
        /// <value>The empty.</value>
        public static Props Empty
        {
            get { return empty; }
        }

        /// <summary>
        ///     Gets the arguments.
        /// </summary>
        /// <value>The arguments.</value>
        public object[] Arguments { get; private set; }

        /// <summary>
        ///     Creates the specified factory.
        /// </summary>
        /// <typeparam name="TActor">The type of the t actor.</typeparam>
        /// <param name="factory">The factory.</param>
        /// <returns>Props.</returns>
        /// <exception cref="System.ArgumentException">The create function must be a 'new T (args)' expression</exception>
        public static Props Create<TActor>(Expression<Func<TActor>> factory) where TActor : ActorBase
        {
            if (factory.Body is UnaryExpression)
                return new DynamicProps<TActor>(factory.Compile());

            var newExpression = factory.Body.AsInstanceOf<NewExpression>();
            if (newExpression == null)
                throw new ArgumentException("The create function must be a 'new T (args)' expression");

            object[] args = newExpression.GetArguments().ToArray();

            return new Props(typeof (TActor), args);
        }

        /// <summary>
        ///     Creates this instance.
        /// </summary>
        /// <typeparam name="TActor">The type of the t actor.</typeparam>
        /// <returns>Props.</returns>
        public static Props Create<TActor>() where TActor : ActorBase
        {
            return new Props(typeof (TActor));
        }

        /// <summary>
        ///     Creates the specified type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Props.</returns>
        public static Props Create(Type type)
        {
            return new Props(type);
        }

        /// <summary>
        ///     Returns a new Props with the specified mailbox set.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>Props.</returns>
        public Props WithMailbox(string path)
        {
            Props copy = Copy();
            copy.Mailbox = path;
            return copy;
        }

        /// <summary>
        ///     Returns a new Props with the specified dispatcher set.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>Props.</returns>
        public Props WithDispatcher(string path)
        {
            Props copy = Copy();
            copy.Dispatcher = path;
            return copy;
        }

        /// <summary>
        ///     Returns a new Props with the specified router config set.
        /// </summary>
        /// <param name="routerConfig">The router configuration.</param>
        /// <returns>Props.</returns>
        public Props WithRouter(RouterConfig routerConfig)
        {
            Props copy = Copy();
            //    copy.Type = typeof(RouterActor);
            copy.RouterConfig = routerConfig;
            return copy;
        }

        /// <summary>
        ///     Returns a new Props with the specified deployment configuration.
        /// </summary>
        /// <param name="deploy">The deploy.</param>
        /// <returns>Props.</returns>
        public Props WithDeploy(Deploy deploy)
        {
            Props copy = Copy();
            copy.Deploy = deploy;
            return copy;
        }

        /// <summary>
        ///     Returns a new Props with the specified supervisor strategy set.
        /// </summary>
        /// <param name="strategy">The strategy.</param>
        /// <returns>Props.</returns>
        public Props WithSupervisorStrategy(SupervisorStrategy strategy)
        {
            Props copy = Copy();
            copy.SupervisorStrategy = strategy;
            return copy;
        }

        //TODO: use Linq Expressions so compile a creator
        //cache the creator
        /// <summary>
        ///     Create a new actor instance. This method is only useful when called during
        ///     actor creation by the ActorSystem.
        /// </summary>
        /// <returns>ActorBase.</returns>
        public virtual ActorBase NewActor()
        {
            return (ActorBase) Activator.CreateInstance(Type, Arguments);
        }

        /// <summary>
        ///     Copies this instance.
        /// </summary>
        /// <returns>Props.</returns>
        protected virtual Props Copy()
        {
            return new Props
            {
                Arguments = Arguments,
                Dispatcher = Dispatcher,
                Mailbox = Mailbox,
                RouterConfig = RouterConfig,
                Type = Type,
                Deploy = Deploy,
                SupervisorStrategy = SupervisorStrategy
            };
        }
    }

    /// <summary>
    ///     Props instance that uses dynamic invocation to create new Actor instances,
    ///     rather than a traditional Activator.
    ///     Intended to be used in conjunction with Dependency Injection.
    /// </summary>
    /// <typeparam name="TActor">The type of the t actor.</typeparam>
    internal class DynamicProps<TActor> : Props where TActor : ActorBase
    {
        /// <summary>
        ///     The _invoker
        /// </summary>
        private readonly Func<TActor> invoker;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DynamicProps{TActor}" /> class.
        /// </summary>
        /// <param name="invoker">The invoker.</param>
        public DynamicProps(Func<TActor> invoker)
            : base(typeof (TActor))
        {
            this.invoker = invoker;
        }

        /// <summary>
        ///     News the actor.
        /// </summary>
        /// <returns>ActorBase.</returns>
        public override ActorBase NewActor()
        {
            return invoker.Invoke();
        }

        #region Copy methods

        /// <summary>
        ///     Copy constructor
        /// </summary>
        /// <param name="copy">The copy.</param>
        /// <param name="invoker">The invoker.</param>
        protected DynamicProps(Props copy, Func<TActor> invoker)
        {
            Dispatcher = copy.Dispatcher;
            Mailbox = copy.Mailbox;
            RouterConfig = copy.RouterConfig;
            Type = copy.Type;
            Deploy = copy.Deploy;
            this.invoker = invoker;
        }

        /// <summary>
        ///     Copies this instance.
        /// </summary>
        /// <returns>Props.</returns>
        protected override Props Copy()
        {
            Props initialCopy = base.Copy();
            var invokerCopy = (Func<TActor>) invoker.Clone();
            return new DynamicProps<TActor>(initialCopy, invokerCopy);
        }

        #endregion
    }
}