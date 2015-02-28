using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Akka.Dispatch;
using Akka.Util.Internal;
using Akka.Util.Reflection;
using Akka.Routing;
using Akka.Util;
using Newtonsoft.Json;

namespace Akka.Actor
{
    /// <summary>
    ///     Props is a configuration object using in creating an [[Actor]]; it is
    ///     immutable, so it is thread-safe and fully shareable.
    ///     Examples on C# API:
    /// <code>
    ///   private Props props = Props.Empty();
    ///   private Props props = Props.Create(() => new MyActor(arg1, arg2));
    /// 
    ///   private Props otherProps = props.WithDispatcher("dispatcher-id");
    ///   private Props otherProps = props.WithDeploy(deployment info);
    ///  </code>
    /// </summary>
    public class Props : IEquatable<Props> , ISurrogated
    {
        public class PropsSurrogate : ISurrogate
        {
            public Type Type { get; set; }
            public Deploy Deploy { get; set; }
            public object[] Arguments { get; set; }
            public object FromSurrogate(ActorSystem system)
            {
                return new Props(Deploy, Type, Arguments);
            }
        }

        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new PropsSurrogate()
            {
                Arguments = Arguments,
                Type = Type,
                Deploy = Deploy,
            };
        }

        public bool Equals(Props other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return CompareDeploy(other) && CompareSupervisorStrategy(other) && CompareArguments(other) && CompareInputType(other);
        }

        private bool CompareInputType(Props other)
        {
            return inputType == other.inputType;
        }

        private bool CompareDeploy(Props other)
        {
            return Deploy.Equals(other.Deploy);
        }

        private bool CompareSupervisorStrategy(Props other)
        {
            return true; //TODO: fix https://github.com/akkadotnet/akka.net/issues/599
            return Equals(SupervisorStrategy, other.SupervisorStrategy);
        }

        private bool CompareArguments(Props other)
        {
            if (other == null)
                return false;

            if (Arguments == null && other.Arguments == null)
                return true;

            if (Arguments == null)
                return false;

            if (Arguments.Length != other.Arguments.Length)
                return false;

            //TODO: since arguments can be serialized, we can not compare by ref
            //arguments may also not impement equality opertators, so we can not structurally compare either
            //we can not just call a serializer and compare outputs either, since different args may require diff serializer mechanics

            return true;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Props) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (Deploy != null ? Deploy.GetHashCode() : 0);
              //  hashCode = (hashCode*397) ^ (SupervisorStrategy != null ? SupervisorStrategy.GetHashCode() : 0);
              //  hashCode = (hashCode*397) ^ (Arguments != null ? Arguments.GetHashCode() : 0);
                hashCode = (hashCode*397) ^ (inputType != null ? inputType.GetHashCode() : 0);
                return hashCode;
            }
        }



        /// <summary>
        ///     The default deploy
        /// </summary>
        private static readonly Deploy defaultDeploy = new Deploy();

        /// <summary>
        ///     No args
        /// </summary>
        private static readonly Object[] noArgs = { };

        /// <summary>
        ///     A Props instance whose creator will create an actor that doesn't respond to any message
        /// </summary>
        private static readonly Props empty = Props.Create<EmptyActor>();

        /// <summary>
        ///     The none
        /// </summary>
        public static readonly Props None = null;

        /// <summary>
        ///     The default producer
        /// </summary>
        private static readonly IndirectActorProducer defaultProducer = new DefaultProducer();

        /// <summary>
        ///     The intern type of the actor or the producer
        /// </summary>
        private Type inputType;

        /// <summary>
        ///     The extern type of the actor
        /// </summary>
        private Type outputType;

        /// <summary>
        ///     The producer of the actor
        /// </summary>
        private IndirectActorProducer producer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        protected Props() 
            : this(defaultDeploy, null, noArgs)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class from a copy.
        /// </summary>
        protected Props(Props copy)
            : this(copy.Deploy, copy.inputType, copy.SupervisorStrategy, copy.Arguments)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="args">The arguments.</param>
        public Props(Type type, object[] args)
            : this(defaultDeploy, type, args)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type.</param>
        public Props(Type type)
            : this(defaultDeploy, type, noArgs)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="args">The arguments.</param>
        public Props(Type type, SupervisorStrategy supervisorStrategy, IEnumerable<object> args)
            : this(defaultDeploy, type, args.ToArray())
        {
            SupervisorStrategy = supervisorStrategy;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="supervisorStrategy">The supervisor strategy.</param>
        /// <param name="args">The arguments.</param>
        public Props(Type type, SupervisorStrategy supervisorStrategy, params object[] args)
            : this(defaultDeploy, type, args)
        {
            SupervisorStrategy = supervisorStrategy;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="deploy">The deploy.</param>
        /// <param name="type">The type.</param>
        /// <param name="args">The arguments.</param>
        public Props(Deploy deploy, Type type, IEnumerable<object> args)
            : this(deploy, type, args.ToArray())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="deploy">The deploy.</param>
        /// <param name="type">The type.</param>
        /// <param name="args">The arguments.</param>
        public Props(Deploy deploy, Type type, params object[] args)
        {
            Deploy = deploy;
            inputType = type;
            Arguments = args;
            producer = CreateProducer(inputType, Arguments);
        }

        /// <summary>
        ///     Gets the type.
        /// </summary>
        /// <value>The type.</value>
        [JsonIgnore]
        public Type Type
        {
            get
            {
                if (outputType == null) {
                    outputType = producer.ActorType;
                }
                return outputType;
            }
        }

        /// <summary>
        ///     Gets or sets the dispatcher.
        /// </summary>
        /// <value>The dispatcher.</value>
        [JsonIgnore]
        public string Dispatcher
        {
            get
            {
                var dispatcher = Deploy.Dispatcher;
                return dispatcher == Deploy.NoDispatcherGiven ? Dispatchers.DefaultDispatcherId : dispatcher;
            }
        }

        /// <summary>
        ///     Gets or sets the mailbox.
        /// </summary>
        /// <value>The mailbox.</value>
        [JsonIgnore]
        public string Mailbox
        {
            get
            {
                return Deploy.Mailbox;
            }
        }

        public string TypeName
        {
            get { return inputType.AssemblyQualifiedName; }
            //for serialization
            private set { inputType = Type.GetType(value); }
        }

        /// <summary>
        ///     Gets or sets the router configuration.
        /// </summary>
        /// <value>The router configuration.</value>
        [JsonIgnore]
        public RouterConfig RouterConfig
        {
            get { return Deploy.RouterConfig; }
        }

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
        /// <param name="supervisorStrategy">Optional: Supervisor strategy</param>
        /// <returns>Props.</returns>
        /// <exception cref="System.ArgumentException">The create function must be a 'new T (args)' expression</exception>
        public static Props Create<TActor>(Expression<Func<TActor>> factory, SupervisorStrategy supervisorStrategy=null) where TActor : ActorBase
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
        public static Props Create<TActor>(params object[] args) where TActor : ActorBase
        {
            return new Props(typeof(TActor), args);
        }

        /// <summary>
        ///     Creates an actor by an actor producer
        /// </summary>
        /// <typeparam name="TProducer">The type of the actor producer</typeparam>
        /// <param name="args">The arguments</param>
        /// <returns>Props</returns>
        public static Props CreateBy<TProducer>(params object[] args) where TProducer : class, IndirectActorProducer
        {
            return new Props(typeof(TProducer), args);
        }


        /// <summary>
        ///     Creates this instance.
        /// </summary>
        /// <typeparam name="TActor">The type of the t actor.</typeparam>
        /// <returns>Props.</returns>
        public static Props Create<TActor>(SupervisorStrategy supervisorStrategy) where TActor : ActorBase, new()
        {
            return new Props(typeof(TActor), supervisorStrategy);
        }



        /// <summary>
        ///     Creates the specified type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Props.</returns>
        public static Props Create(Type type, params object[] args)
        {
            return new Props(type, args);
        }

        /// <summary>
        ///     Returns a new Props with the specified mailbox set.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>Props.</returns>
        public Props WithMailbox(string path)
        {
            Props copy = Copy();
            copy.Deploy = Deploy.WithMailbox(path);
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
            copy.Deploy = Deploy.WithDispatcher(path);
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
            copy.Deploy = Deploy.WithRouterConfig(routerConfig);
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
            var original = copy.Deploy;

            // TODO: this is a hack designed to preserve explicit router deployments https://github.com/akkadotnet/akka.net/issues/546
            // in reality, we should be able to do copy.Deploy = deploy.WithFallback(copy.Deploy); but that blows up at the moment
            // - Aaron Stannard
            if (!(original.RouterConfig is NoRouter || original.RouterConfig is FromConfig) && deploy.RouterConfig is NoRouter)
            {
                copy.Deploy = deploy.WithRouterConfig(original.RouterConfig);
            }
            else
            {
                copy.Deploy = deploy;
            }
            
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
            var type = Type;
            var arguments = Arguments;
            try {
                return producer.Produce();
            } catch (Exception e) {
                throw new Exception("Error while creating actor instance of type " + type + " with " + arguments.Length + " args: (" + StringFormat.SafeJoin(",", arguments) + ")", e);
            }
        }

        /// <summary>
        ///     Copies this instance.
        /// </summary>
        /// <returns>Props.</returns>
        protected virtual Props Copy()
        {
            return new Props(Deploy, inputType, Arguments) { SupervisorStrategy = SupervisorStrategy };
        }

        #region INTERNAL API

        /// <summary>
        /// EmptyActor is used by <see cref="Props.None"/> in order to create actors that
        /// don't respond to any messages.
        /// </summary>
        internal class EmptyActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {

            }
        }

        private class DefaultProducer : IndirectActorProducer
        {
            public ActorBase Produce()
            {
                throw new InvalidOperationException("No actor producer specified!");
            }

            public Type ActorType
            {
                get { return typeof(ActorBase); }
            }
        }

        private class ActivatorProducer : IndirectActorProducer
        {
            private readonly Type _actorType;
            private readonly object[] _args;

            public ActivatorProducer(Type actorType, object[] args)
            {
                _actorType = actorType;
                _args = args;
            }

            public ActorBase Produce()
            {
                return Activator.CreateInstance(_actorType, _args).AsInstanceOf<ActorBase>();
            }

            public Type ActorType
            {
                get { return _actorType; }
            }
        }

        private class FactoryConsumer<TActor> : IndirectActorProducer where TActor : ActorBase
        {
            private readonly Func<TActor> _factory;

            public FactoryConsumer(Func<TActor> factory)
            {
                _factory = factory;
            }

            public ActorBase Produce()
            {
                return _factory.Invoke();
            }

            public Type ActorType
            {
                get { return typeof(TActor); }
            }
        }

        #endregion

        private static IndirectActorProducer CreateProducer(Type type, object[] args)
        {
            if (type == null) {
                return defaultProducer;
            }
            if (typeof(IndirectActorProducer).IsAssignableFrom(type)) {
                return Activator.CreateInstance(type, args).AsInstanceOf<IndirectActorProducer>();
            }
            if (typeof(ActorBase).IsAssignableFrom(type)) {
                return new ActivatorProducer(type, args);
            }
            throw new ArgumentException(string.Format("Unknown actor producer [{0}]", type.FullName));
        }
    }

    public class TerminatedProps : Props
    {
        public override ActorBase NewActor()
        {
            throw new InvalidOperationException("This actor has been terminated");
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
            : base(typeof(TActor))
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
        private DynamicProps(Props copy, Func<TActor> invoker)
            : base(copy)
        {
            this.invoker = invoker;
        }

        /// <summary>
        ///     Copies this instance.
        /// </summary>
        /// <returns>Props.</returns>
        protected override Props Copy()
        {
            Props initialCopy = base.Copy();
            var invokerCopy = (Func<TActor>)invoker.Clone();
            return new DynamicProps<TActor>(initialCopy, invokerCopy);
        }

        #endregion
    }

    /// <summary>
    ///     This interface defines a class of actor creation strategies deviating from
    ///     the usual default of just reflectively instantiating the [[Actor]]
    ///     subclass. It can be used to allow a dependency injection framework to
    ///     determine the actual actor class and how it shall be instantiated.
    /// </summary>
    public interface IndirectActorProducer
    {
        /// <summary>
        ///     This factory method must produce a fresh actor instance upon each
        ///     invocation. It is not permitted to return the same instance more than
        ///     once.
        /// </summary>
        /// <returns>A fresh actor instance.</returns>
        ActorBase Produce();

        /// <summary>
        ///     This method is used by [[Props]] to determine the type of actor which will
        ///     be created. The returned type is not used to produce the actor.
        /// </summary>
        Type ActorType { get; }
    }
}
