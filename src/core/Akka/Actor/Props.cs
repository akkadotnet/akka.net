//-----------------------------------------------------------------------
// <copyright file="Props.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Akka.Dispatch;
using Akka.Util.Internal;
using Akka.Util.Reflection;
using Akka.Routing;
using Akka.Util;
using Newtonsoft.Json;

namespace Akka.Actor
{
    /// <summary>
    /// This class represents a configuration object used in creating an <see cref="Akka.Actor.ActorBase">actor</see>.
    /// It is immutable and thus thread-safe.
    /// <example>
    /// <code>
    ///   private Props props = Props.Empty();
    ///   private Props props = Props.Create(() => new MyActor(arg1, arg2));
    /// 
    ///   private Props otherProps = props.WithDispatcher("dispatcher-id");
    ///   private Props otherProps = props.WithDeploy(deployment info);
    /// </code>
    /// </example>
    /// </summary>
    public class Props : IEquatable<Props> , ISurrogated
    {
        private const string NullActorTypeExceptionText = "Props must be instantiated with an actor type.";

        /// <summary>
        /// This class represents a surrogate of a <see cref="Props"/> configuration object.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class PropsSurrogate : ISurrogate
        {
            /// <summary>
            /// The type of actor to create
            /// </summary>
            public Type Type { get; set; }
            /// <summary>
            /// The configuration used to deploy the actor.
            /// </summary>
            public Deploy Deploy { get; set; }
            /// <summary>
            /// The arguments used to create the actor.
            /// </summary>
            public object[] Arguments { get; set; }

            /// <summary>
            /// Creates a <see cref="Props"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="Props"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new Props(Deploy, Type, Arguments);
            }
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="Props"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="Props"/>.</returns>
        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new PropsSurrogate()
            {
                Arguments = Arguments,
                Type = Type,
                Deploy = Deploy,
            };
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// <c>true</c> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <c>false</c>.
        /// </returns>
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

#pragma warning disable CS0162 // Disabled because it's marked as a TODO
        private bool CompareSupervisorStrategy(Props other)
        {
            return true; //TODO: fix https://github.com/akkadotnet/akka.net/issues/599
            return Equals(SupervisorStrategy, other.SupervisorStrategy);
        }
#pragma warning restore CS0162

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
            //arguments may also not implement equality operators, so we can not structurally compare either
            //we can not just call a serializer and compare outputs either, since different args may require diff serializer mechanics

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Props) obj);
        }

        /// <inheritdoc/>
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

        private static readonly Deploy defaultDeploy = new Deploy();
        private static readonly Object[] noArgs = { };
        private static readonly Props empty = Props.Create<EmptyActor>();

        /// <summary>
        /// A pre-configured <see cref="Akka.Actor.Props"/> that doesn't create actors.
        /// 
        /// <note>
        /// The value of this field is null.
        /// </note>
        /// </summary>
        public static readonly Props None = null;

        private static readonly IIndirectActorProducer defaultProducer = new DefaultProducer();
        private Type inputType;
        private Type outputType;
        private IIndirectActorProducer producer;

        /// <summary>
        /// Initializes a new instance of the <see cref="Props"/> class.
        /// </summary>
        protected Props()
            : this(defaultDeploy, null, noArgs)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="copy">The object that is being cloned.</param>
        protected Props(Props copy)
            : this(copy.Deploy, copy.inputType, copy.SupervisorStrategy, copy.Arguments)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Props" /> class.
        /// 
        /// <note>
        /// <see cref="Props"/> configured in this way uses the <see cref="Akka.Actor.Deploy"/> deployer.
        /// </note>
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if <see cref="Props"/> is not instantiated with an actor type.
        /// </exception>
        public Props(Type type, object[] args)
            : this(defaultDeploy, type, args)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Props" /> class.
        ///
        /// <note>
        /// <see cref="Props"/> configured in this way uses the <see cref="Akka.Actor.Deploy"/> deployer.
        /// </note>
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if <see cref="Props"/> is not instantiated with an actor type.
        /// </exception>

        public Props(Type type)
            : this(defaultDeploy, type, noArgs)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if <see cref="Props"/> is not instantiated with an actor type.
        /// </exception>

        public Props(Type type, SupervisorStrategy supervisorStrategy, IEnumerable<object> args)
            : this(defaultDeploy, type, args.ToArray())
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);

            SupervisorStrategy = supervisorStrategy;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if <see cref="Props"/> is not instantiated with an actor type.
        /// </exception>

        public Props(Type type, SupervisorStrategy supervisorStrategy, params object[] args)
            : this(defaultDeploy, type, args)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);

            SupervisorStrategy = supervisorStrategy;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if <see cref="Props"/> is not instantiated with an actor type.
        /// </exception>

        public Props(Deploy deploy, Type type, IEnumerable<object> args)
            : this(deploy, type, args.ToArray())
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentException">This exception is thrown if <paramref name="type"/> is an unknown actor producer.</exception>
        public Props(Deploy deploy, Type type, params object[] args)
        {
            Deploy = deploy;
            inputType = type;
            Arguments = args ?? noArgs;
            producer = CreateProducer(inputType, Arguments);
        }

        /// <summary>
        /// The type of the actor that is created.
        /// </summary>
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
        /// The dispatcher used in the deployment of the actor.
        /// </summary>
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
        /// The mailbox used in the deployment of the actor.
        /// </summary>
        [JsonIgnore]
        public string Mailbox
        {
            get
            {
                return Deploy.Mailbox;
            }
        }

        /// <summary>
        /// The assembly qualified name of the type of the actor that is created.
        /// </summary>
        public string TypeName
        {
            get { return inputType.AssemblyQualifiedName; }
            //for serialization
            private set { inputType = Type.GetType(value); }
        }

        /// <summary>
        /// The router used in the deployment of the actor.
        /// </summary>
        [JsonIgnore]
        public RouterConfig RouterConfig
        {
            get { return Deploy.RouterConfig; }
        }

        /// <summary>
        /// The configuration used to deploy the actor.
        /// </summary>
        public Deploy Deploy { get; protected set; }

        /// <summary>
        /// The supervisor strategy used to manage the actor.
        /// </summary>
        public SupervisorStrategy SupervisorStrategy { get; protected set; }

        /// <summary>
        /// A pre-configured <see cref="Akka.Actor.Props"/> that creates an actor that doesn't respond to messages.
        /// </summary>
        public static Props Empty
        {
            get { return empty; }
        }

        /// <summary>
        /// The arguments needed to create the actor.
        /// </summary>
        public object[] Arguments { get; private set; }

        /// <summary>
        /// Creates an actor using a specified lambda expression.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="factory">The lambda expression used to create the actor.</param>
        /// <param name="supervisorStrategy">Optional: The supervisor strategy used to manage the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        /// <exception cref="ArgumentException">The create function must be a 'new T (args)' expression</exception>
        public static Props Create<TActor>(Expression<Func<TActor>> factory, SupervisorStrategy supervisorStrategy=null) where TActor : ActorBase
        {
            if (factory.Body is UnaryExpression)
                return new DynamicProps<TActor>(factory.Compile());

            var newExpression = factory.Body.AsInstanceOf<NewExpression>();
            if (newExpression == null)
                throw new ArgumentException("The create function must be a 'new T (args)' expression");

            object[] args = newExpression.GetArguments().ToArray();

            return new Props(typeof (TActor), supervisorStrategy, args);
        }

        /// <summary>
        /// Creates an actor using the given arguments.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props Create<TActor>(params object[] args) where TActor : ActorBase
        {
            return new Props(typeof(TActor), args);
        }

        /// <summary>
        /// Creates an actor using a specified actor producer.
        /// </summary>
        /// <typeparam name="TProducer">The type of producer used to create the actor.</typeparam>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props CreateBy<TProducer>(params object[] args) where TProducer : class, IIndirectActorProducer
        {
            return new Props(typeof(TProducer), args);
        }

        /// <summary>
        /// Creates an actor using a specified supervisor strategy.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props Create<TActor>(SupervisorStrategy supervisorStrategy) where TActor : ActorBase, new()
        {
            return new Props(typeof(TActor), supervisorStrategy);
        }

        /// <summary>
        /// Creates an actor of a specified type.
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        /// <exception cref="ArgumentNullException">Props must be instantiated with an actor type.</exception>
        public static Props Create(Type type, params object[] args)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);

            return new Props(type, args);
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given <paramref name="mailbox" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="mailbox">The mailbox used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="mailbox" />.</returns>
        public Props WithMailbox(string mailbox)
        {
            Props copy = Copy();
            copy.Deploy = Deploy.WithMailbox(mailbox);
            return copy;
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given <paramref name="dispatcher" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="dispatcher" />.</returns>
        public Props WithDispatcher(string dispatcher)
        {
            Props copy = Copy();
            copy.Deploy = Deploy.WithDispatcher(dispatcher);
            return copy;
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given router.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="routerConfig">The router used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="routerConfig" />.</returns>
        public Props WithRouter(RouterConfig routerConfig)
        {
            Props copy = Copy();
            copy.Deploy = Deploy.WithRouterConfig(routerConfig);
            return copy;
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given deployment configuration.
        ///
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="deploy" />.</returns>
        public Props WithDeploy(Deploy deploy)
        {
            Props copy = Copy();
            var original = copy.Deploy;

            // TODO: this is a hack designed to preserve explicit router deployments https://github.com/akkadotnet/akka.net/issues/546
            // in reality, we should be able to do copy.Deploy = deploy.WithFallback(copy.Deploy); but that blows up at the moment
            // - Aaron Stannard
            copy.Deploy = deploy.WithFallback(copy.Deploy);
            //if (!(original.RouterConfig is NoRouter || original.RouterConfig is FromConfig) && deploy.RouterConfig is NoRouter)
            //{
            //    copy.Deploy = deploy.WithFallback(copy.Deploy);
            //    copy.Deploy = deploy.WithRouterConfig(original.RouterConfig);
            //}
            ////both configs describe valid, programmatically defined routers (usually clustered routers)
            //else if (!(original.RouterConfig is NoRouter || original.RouterConfig is FromConfig) &&
            //         !(deploy.RouterConfig is FromConfig))
            //{
            //    var deployedRouter = deploy.RouterConfig.WithFallback(original.RouterConfig);
            //    copy.Deploy = copy.Deploy.WithRouterConfig(deployedRouter);
            //}
            //else
            //{
            //    copy.Deploy = deploy;
            //}
            
            return copy;
        }

        ///  <summary>
        ///  Creates a new <see cref="Akka.Actor.Props" /> with a given supervisor strategy.
        /// 
        ///  <note>
        ///  This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        ///  </note>
        ///  </summary>
        ///  <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="supervisorStrategy" />.</returns>
        public Props WithSupervisorStrategy(SupervisorStrategy supervisorStrategy)
        {
            Props copy = Copy();
            copy.SupervisorStrategy = supervisorStrategy;
            return copy;
        }

        //TODO: use Linq Expressions so compile a creator
        //cache the creator
        /// <summary>
        /// Creates a new actor using the configured actor producer.
        /// 
        /// <remarks>
        /// This method is only useful when called during actor creation by the ActorSystem.
        /// </remarks>
        /// </summary>
        /// <exception cref="TypeLoadException">
        /// This exception is thrown if there was an error creating an actor of type <see cref="Props.Type"/>
        /// with the arguments from <see cref="Props.Arguments"/>.
        /// </exception>
        /// <returns>The newly created actor</returns>
        public virtual ActorBase NewActor()
        {
            var type = Type;
            var arguments = Arguments;
            try {
                return producer.Produce();
            } catch (Exception e) {
                throw new TypeLoadException($"Error while creating actor instance of type {type} with {arguments.Length} args: ({StringFormat.SafeJoin(",", arguments)})", e);
            }
        }

        /// <summary>
        /// Creates a copy of the current instance.
        /// </summary>
        /// <returns>The newly created <see cref="Akka.Actor.Props"/></returns>
        protected virtual Props Copy()
        {
            return new Props(Deploy, inputType, Arguments) { SupervisorStrategy = SupervisorStrategy };
        }

        #region INTERNAL API

        /// <summary>
        /// This class represents a specialized <see cref="UntypedActor" /> that doesn't respond to messages.
        /// </summary>
        internal class EmptyActor : UntypedActor
        {
            /// <summary>
            /// Handles messages received by the actor.
            /// </summary>
            /// <param name="message">The message past to the actor.</param>
            protected override void OnReceive(object message)
            {

            }
        }

        private class DefaultProducer : IIndirectActorProducer
        {
            public ActorBase Produce()
            {
                throw new InvalidOperationException("No actor producer specified!");
            }

            public Type ActorType
            {
                get { return typeof(ActorBase); }
            }


            public void Release(ActorBase actor)
            {
                actor = null;
            }
        }

        private class ActivatorProducer : IIndirectActorProducer
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


            public void Release(ActorBase actor)
            {
                actor = null;
            }
        }

        private class FactoryConsumer<TActor> : IIndirectActorProducer where TActor : ActorBase
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


            public void Release(ActorBase actor)
            {
                actor = null;
            }
        }

        #endregion

        private static IIndirectActorProducer CreateProducer(Type type, object[] args)
        {
            if (type == null) {
                return defaultProducer;
            }
            if (typeof(IIndirectActorProducer).IsAssignableFrom(type)) {
                return Activator.CreateInstance(type, args).AsInstanceOf<IIndirectActorProducer>();
            }
            if (typeof(ActorBase).IsAssignableFrom(type)) {
                return new ActivatorProducer(type, args);
            }
            throw new ArgumentException($"Unknown actor producer [{type.FullName}]", nameof(type));
        }

        /// <summary>
        /// Signals the producer that it can release its reference to the actor.
        /// </summary>
        /// <param name="actor">The actor to release</param>
        internal void Release(ActorBase actor)
        {
            try
            {
                if (this.producer != null) this.producer.Release(actor);
            }
            finally
            {
                actor = null;
            }
        }
    }

    /// <summary>
    /// This class represents a specialized <see cref="Akka.Actor.Props"/> used when the actor has been terminated.
    /// </summary>
    public class TerminatedProps : Props
    {
        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="InvalidOperationException">This exception is thrown automatically since the actor has been terminated.</exception>
        /// <returns>N/A</returns>
        public override ActorBase NewActor()
        {
            throw new InvalidOperationException("This actor has been terminated");
        }
    }

    /// <summary>
    /// This class represents a specialized <see cref="Akka.Actor.Props"/> that uses dynamic invocation
    /// to create new actor instances, rather than a traditional <see cref="System.Activator"/>.
    /// 
    /// <note>
    /// This is intended to be used in conjunction with Dependency Injection.
    /// </note>
    /// </summary>
    /// <typeparam name="TActor">The type of the actor to create.</typeparam>
    internal class DynamicProps<TActor> : Props where TActor : ActorBase
    {
        private readonly Func<TActor> invoker;

        /// <summary>
        /// Initializes a new instance of the <see cref="DynamicProps{TActor}" /> class.
        /// </summary>
        /// <param name="invoker">The factory method used to create an actor.</param>
        public DynamicProps(Func<TActor> invoker)
            : base(typeof(TActor))
        {
            this.invoker = invoker;
        }

        /// <summary>
        /// Creates a new actor using the configured factory method.
        /// </summary>
        /// <returns>The actor created using the factory method.</returns>
        public override ActorBase NewActor()
        {
            return invoker.Invoke();
        }

        #region Copy methods

        private DynamicProps(Props copy, Func<TActor> invoker)
            : base(copy)
        {
            this.invoker = invoker;
        }

        /// <summary>
        /// Creates a copy of the current instance.
        /// </summary>
        /// <returns>The newly created <see cref="Akka.Actor.Props"/></returns>
        protected override Props Copy()
        {
            Props initialCopy = base.Copy();
#if CLONEABLE
            var invokerCopy = (Func<TActor>)invoker.Clone();
#else
            // TODO: CORECLR FIX IT
            var invokerCopy = (Func<TActor>)invoker;
#endif
            return new DynamicProps<TActor>(initialCopy, invokerCopy);
        }

        #endregion
    }

    /// <summary>
    /// This interface defines a class of actor creation strategies deviating from
    /// the usual default of just reflectively instantiating the <see cref="Akka.Actor.ActorBase">Actor</see>
    /// subclass. It can be used to allow a dependency injection framework to
    /// determine the actual actor class and how it shall be instantiated.
    /// </summary>
    public interface IIndirectActorProducer
    {
        /// <summary>
        /// This factory method must produce a fresh actor instance upon each
        /// invocation. It is not permitted to return the same instance more than
        /// once.
        /// </summary>
        /// <returns>A fresh actor instance.</returns>
        ActorBase Produce();

        /// <summary>
        /// This method is used by <see cref="Akka.Actor.Props"/> to determine the type of actor to create.
        /// The returned type is not used to produce the actor.
        /// </summary>
        /// <returns>The type of the actor created.</returns>
        Type ActorType { get; }

        /// <summary>
        /// This method is used by <see cref="Akka.Actor.Props"/> to signal the producer that it can
        /// release it's reference.
        /// 
        /// <remarks>
        /// To learn more about using Dependency Injection in .NET, see <see href="http://www.amazon.com/Dependency-Injection-NET-Mark-Seemann/dp/1935182501">HERE</see>.
        /// </remarks>
        /// </summary>
        /// <param name="actor">The actor to release</param>
        void Release(ActorBase actor);
    }
}
