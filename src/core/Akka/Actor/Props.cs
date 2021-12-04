//-----------------------------------------------------------------------
// <copyright file="Props.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Akka.Dispatch;
using Akka.Routing;
using Akka.Util;
using Akka.Util.Reflection;
using Newtonsoft.Json;

namespace Akka.Actor
{
    /// <summary>
    ///     This class represents a configuration object used in creating an <see cref="Akka.Actor.ActorBase">actor</see>.
    ///     It is immutable and thus thread-safe.
    ///     <example>
    ///         <code>
    ///   private Props props = Props.Empty();
    ///   private Props props = Props.Create(() => new MyActor(arg1, arg2));
    /// 
    ///   private Props otherProps = props.WithDispatcher("dispatcher-id");
    ///   private Props otherProps = props.WithDeploy(deployment info);
    /// </code>
    ///     </example>
    /// </summary>
    public sealed class Props : IEquatable<Props>, ISurrogated
    {
        private const string NullActorTypeExceptionText = "Props must be instantiated with an actor type.";

        private static readonly Deploy DefaultDeploy = new Deploy();
        private static readonly object[] NoArgs = Array.Empty<object>();

        /// <summary>
        ///     A pre-configured <see cref="Akka.Actor.Props" /> that doesn't create actors.
        ///     <note>
        ///         The value of this field is null.
        ///     </note>
        /// </summary>
        public static readonly Props None = null;

        private readonly IIndirectActorProducer _producer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        ///     <note>
        ///         <see cref="Props" /> configured in this way uses the <see cref="Akka.Actor.Deploy" /> deployer.
        ///     </note>
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentNullException">
        ///     This exception is thrown if <see cref="Props" /> is not instantiated with an actor type.
        /// </exception>
        public Props(Type type, object[] args)
            : this(DefaultDeploy, type, args)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        ///     <note>
        ///         <see cref="Props" /> configured in this way uses the <see cref="Akka.Actor.Deploy" /> deployer.
        ///     </note>
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <exception cref="ArgumentNullException">
        ///     This exception is thrown if <see cref="Props" /> is not instantiated with an actor type.
        /// </exception>
        public Props(Type type)
            : this(DefaultDeploy, type, NoArgs)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentNullException">
        ///     This exception is thrown if <see cref="Props" /> is not instantiated with an actor type.
        /// </exception>
        public Props(Type type, SupervisorStrategy supervisorStrategy, IEnumerable<object> args)
            : this(DefaultDeploy, type, args.ToArray())
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);

            SupervisorStrategy = supervisorStrategy;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentNullException">
        ///     This exception is thrown if <see cref="Props" /> is not instantiated with an actor type.
        /// </exception>
        public Props(Type type, SupervisorStrategy supervisorStrategy, params object[] args)
            : this(DefaultDeploy, type, args)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);

            SupervisorStrategy = supervisorStrategy;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentNullException">
        ///     This exception is thrown if <see cref="Props" /> is not instantiated with an actor type.
        /// </exception>
        public Props(Deploy deploy, Type type, IEnumerable<object> args)
            : this(deploy, type, args.ToArray())
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentException">This exception is thrown if <paramref name="type" /> is an unknown actor producer.</exception>
        public Props(Deploy deploy, Type type, params object[] args)
        {
            Deploy = deploy;
            // have to preserve the "CreateProducer" call here to preserve backwards compat with Akka.DI.Core
            (_producer, Type, Arguments) = CreateProducer(type, args);
            Arguments = Arguments?.Length > 0 ? Arguments : NoArgs;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Props" /> class using a specified <see cref="IIndirectActorProducer"/>.
        /// </summary>
        /// <remarks>
        ///     This API is meant for advanced use cases, such as Akka.DependencyInjection.
        /// </remarks>
        /// <param name="producer">The type of <see cref="IIndirectActorProducer"/> that will be used to instantiate <see cref="Type"/></param>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        internal Props(IIndirectActorProducer producer, Deploy deploy, Type type, params object[] args)
        {
            Deploy = deploy;
            Type = type;
            Arguments = args?.Length > 0 ? args : NoArgs;
            _producer = producer;
        }

        /// <summary>
        ///     The type of the actor that is created.
        /// </summary>
        [JsonIgnore]
        public Type Type { get; private set; }

        /// <summary>
        ///     The dispatcher used in the deployment of the actor.
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
        ///     The mailbox used in the deployment of the actor.
        /// </summary>
        [JsonIgnore]
        public string Mailbox => Deploy.Mailbox;

        /// <summary>
        ///     The assembly qualified name of the type of the actor that is created.
        /// </summary>
        public string TypeName
        {
            get => Type.AssemblyQualifiedName;
            //for serialization
            private set => Type = Type.GetType(value);
        }

        /// <summary>
        ///     The router used in the deployment of the actor.
        /// </summary>
        [JsonIgnore]
        public RouterConfig RouterConfig => Deploy.RouterConfig;

        /// <summary>
        ///     The configuration used to deploy the actor.
        /// </summary>
        public Deploy Deploy { get; private set; }

        /// <summary>
        ///     The supervisor strategy used to manage the actor.
        /// </summary>
        public SupervisorStrategy SupervisorStrategy { get; private set; }

        /// <summary>
        ///     A pre-configured <see cref="Akka.Actor.Props" /> that creates an actor that doesn't respond to messages.
        /// </summary>
        public static Props Empty { get; } = Create<EmptyActor>();

        /// <summary>
        ///     A pre-configured <see cref="Akka.Actor.Props" /> used when the actor has been terminated.
        /// </summary>
        public static Props Terminated { get; } = new Props(InvalidProducer.Terminated, DefaultDeploy, typeof(EmptyActor), NoArgs);

        /// <summary>
        ///     The arguments needed to create the actor.
        /// </summary>
        public object[] Arguments { get; }

        /// <summary>
        ///     Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        ///     <c>true</c> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(Props other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return CompareDeploy(other) && CompareSupervisorStrategy(other) && CompareArguments(other) &&
                   CompareInputType(other);
        }

        /// <summary>
        ///     Creates a surrogate representation of the current <see cref="Props" />.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="Props" />.</returns>
        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new PropsSurrogate { Arguments = Arguments, Type = Type, Deploy = Deploy };
        }

        private bool CompareInputType(Props other)
        {
            return Type == other.Type;
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
            if (other is null)
                return false;

            if (Arguments is null && other.Arguments is null)
                return true;

            if (Arguments is null)
                return false;

            if (Arguments.Length != other.Arguments.Length)
                return false;

            //TODO: since arguments can be serialized, we can not compare by ref
            //arguments may also not implement equality operators, so we can not structurally compare either
            //we can not just call a serializer and compare outputs either, since different args may require diff serializer mechanics

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((Props)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Deploy != null ? Deploy.GetHashCode() : 0;
                //  hashCode = (hashCode*397) ^ (SupervisorStrategy != null ? SupervisorStrategy.GetHashCode() : 0);
                //  hashCode = (hashCode*397) ^ (Arguments != null ? Arguments.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Type?.GetHashCode() ?? 0);
                return hashCode;
            }
        }

        /// <summary>
        ///     Creates an actor using a specified lambda expression.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="factory">The lambda expression used to create the actor.</param>
        /// <param name="supervisorStrategy">Optional: The supervisor strategy used to manage the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        /// <exception cref="ArgumentException">The create function must be a 'new T (args)' expression</exception>
        public static Props Create<TActor>(Expression<Func<TActor>> factory,
            SupervisorStrategy supervisorStrategy = null) where TActor : ActorBase
        {
            if (factory.Body is UnaryExpression)
                return new Props(new FactoryConsumer<TActor>(factory.Compile()), DefaultDeploy, typeof(TActor), NoArgs);

            var newExpression = factory.Body as NewExpression
                ?? throw new ArgumentException("The create function must be a 'new T (args)' expression");

            var args = newExpression.GetArguments();
            args = args.Length > 0 ? args : NoArgs;

            return new Props(ActivatorProducer.Instance, DefaultDeploy, typeof(TActor), args) { SupervisorStrategy = supervisorStrategy };
        }

        /// <summary>
        ///     Creates an actor using the given arguments.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props Create<TActor>(params object[] args) where TActor : ActorBase
        {
            return new Props(ActivatorProducer.Instance, DefaultDeploy, typeof(TActor), args);
        }

        /// <summary>
        ///     Creates an actor using a specified actor producer.
        /// </summary>
        /// <typeparam name="TProducer">The type of producer used to create the actor.</typeparam>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        [Obsolete("Do not use this method. Call CreateBy(IIndirectActorProducer, params object[] args) instead")]
        public static Props CreateBy<TProducer>(Type type, params object[] args) where TProducer : class, IIndirectActorProducer
        {
            IIndirectActorProducer producer;
            if (typeof(TProducer) == typeof(ActivatorProducer))
                producer = ActivatorProducer.Instance;
            else
                producer = Activator.CreateInstance<TProducer>();

            return new Props(producer, DefaultDeploy, type, args);
        }

        /// <summary>
        ///     Creates an actor using a specified actor producer.
        /// </summary>
        /// <param name="producer">The actor producer that will be used to create the underlying actor..</param>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props CreateBy(IIndirectActorProducer producer, Type type, params object[] args)
        {
            return new Props(producer, DefaultDeploy, type, args);
        }

        /// <summary>
        ///     Creates an actor using a specified supervisor strategy.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props Create<TActor>(SupervisorStrategy supervisorStrategy) where TActor : ActorBase, new()
        {
            return new Props(ActivatorProducer.Instance, DefaultDeploy, typeof(TActor), NoArgs) { SupervisorStrategy = supervisorStrategy };
        }

        /// <summary>
        ///     Creates an actor of a specified type.
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
        ///     Creates a new <see cref="Akka.Actor.Props" /> with a given <paramref name="mailbox" />.
        ///     <note>
        ///         This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        ///     </note>
        /// </summary>
        /// <param name="mailbox">The mailbox used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="mailbox" />.</returns>
        public Props WithMailbox(string mailbox)
        {
            var copy = Copy();
            copy.Deploy = Deploy.WithMailbox(mailbox);
            return copy;
        }

        /// <summary>
        ///     Creates a new <see cref="Akka.Actor.Props" /> with a given <paramref name="dispatcher" />.
        ///     <note>
        ///         This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        ///     </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="dispatcher" />.</returns>
        public Props WithDispatcher(string dispatcher)
        {
            var copy = Copy();
            copy.Deploy = Deploy.WithDispatcher(dispatcher);
            return copy;
        }

        /// <summary>
        ///     Creates a new <see cref="Akka.Actor.Props" /> with a given router.
        ///     <note>
        ///         This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        ///     </note>
        /// </summary>
        /// <param name="routerConfig">The router used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="routerConfig" />.</returns>
        public Props WithRouter(RouterConfig routerConfig)
        {
            var copy = Copy();
            copy.Deploy = Deploy.WithRouterConfig(routerConfig);
            return copy;
        }

        /// <summary>
        ///     Creates a new <see cref="Akka.Actor.Props" /> with a given deployment configuration.
        ///     <note>
        ///         This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        ///     </note>
        /// </summary>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="deploy" />.</returns>
        public Props WithDeploy(Deploy deploy)
        {
            var copy = Copy();
            //var original = copy.Deploy;

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

        /// <summary>
        ///     Creates a new <see cref="Akka.Actor.Props" /> with a given supervisor strategy.
        ///     <note>
        ///         This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        ///     </note>
        /// </summary>
        /// <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="supervisorStrategy" />.</returns>
        public Props WithSupervisorStrategy(SupervisorStrategy supervisorStrategy)
        {
            var copy = Copy();
            copy.SupervisorStrategy = supervisorStrategy;
            return copy;
        }

        //TODO: use Linq Expressions so compile a creator
        //cache the creator
        /// <summary>
        ///     Creates a new actor using the configured actor producer.
        ///     <remarks>
        ///         This method is only useful when called during actor creation by the ActorSystem.
        ///     </remarks>
        /// </summary>
        /// <exception cref="TypeLoadException">
        ///     This exception is thrown if there was an error creating an actor of type <see cref="Props.Type" />
        ///     with the arguments from <see cref="Props.Arguments" />.
        /// </exception>
        /// <returns>The newly created actor</returns>
        public ActorBase NewActor()
        {
            try
            {
                return _producer.Produce(this);
            }
            catch (Exception e)
            {
                throw new TypeLoadException(
                    $"Error while creating actor instance of type {Type} with {Arguments.Length} args: ({StringFormat.SafeJoin(",", Arguments)})",
                    e);
            }
        }

        /// <summary>
        ///     Creates a copy of the current instance.
        /// </summary>
        /// <returns>The newly created <see cref="Akka.Actor.Props" /></returns>
        private Props Copy()
        {
            return new Props(_producer, Deploy, Type, Arguments) { SupervisorStrategy = SupervisorStrategy };
        }

        [Obsolete("we should not be calling this method. Pass in an explicit IIndirectActorProducer reference instead.")]
        private static (IIndirectActorProducer, Type, object[] args) CreateProducer(Type type, object[] args)
        {
            if (type is null)
                return (InvalidProducer.Default, typeof(EmptyActor), NoArgs);

            if (typeof(IIndirectActorProducer).IsAssignableFrom(type))
            {
                var producer = (IIndirectActorProducer)Activator.CreateInstance(type, args);
                if (producer is IIndirectActorProducerWithActorType p)
                    return (producer, p.ActorType, NoArgs);

                //maybe throw new ArgumentException($"Unsupported legacy actor producer [{type.FullName}]", nameof(type));
                return (producer, typeof(ActorBase), NoArgs);
            }

            if (typeof(ActorBase).IsAssignableFrom(type))
                return (ActivatorProducer.Instance, type, args);

            throw new ArgumentException($"Unknown actor producer [{type.FullName}]", nameof(type));
        }

        /// <summary>
        ///     Signals the producer that it can release its reference to the actor.
        /// </summary>
        /// <param name="actor">The actor to release</param>
        internal void Release(ActorBase actor)
        {
            _producer?.Release(actor);
        }

        /// <summary>
        ///     This class represents a surrogate of a <see cref="Props" /> configuration object.
        ///     Its main use is to help during the serialization process.
        /// </summary>
        public sealed class PropsSurrogate : ISurrogate
        {
            /// <summary>
            ///     The type of actor to create
            /// </summary>
            public Type Type { get; set; }

            /// <summary>
            ///     The configuration used to deploy the actor.
            /// </summary>
            public Deploy Deploy { get; set; }

            /// <summary>
            ///     The arguments used to create the actor.
            /// </summary>
            public object[] Arguments { get; set; }

            /// <summary>
            ///     Creates a <see cref="Props" /> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="Props" /> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new Props(Deploy, Type, Arguments);
            }
        }

        #region INTERNAL API

        /// <summary>
        ///     This class represents a specialized <see cref="UntypedActor" /> that doesn't respond to messages.
        /// </summary>
        internal sealed class EmptyActor : UntypedActor
        {
            /// <summary>
            ///     Handles messages received by the actor.
            /// </summary>
            /// <param name="message">The message past to the actor.</param>
            protected override void OnReceive(object message)
            {
            }
        }

        private sealed class InvalidProducer : IIndirectActorProducer
        {
            public readonly static InvalidProducer Default = new InvalidProducer("No actor producer specified!");
            public readonly static InvalidProducer Terminated = new InvalidProducer("This actor has been terminated");

            private readonly string _message;

            public InvalidProducer(string message)
            {
                _message = message;
            }

            public ActorBase Produce(Props props)
            {
                throw new InvalidOperationException(_message);
            }

            public void Release(ActorBase actor)
            {
                //no-op
            }
        }

        private sealed class ActivatorProducer : IIndirectActorProducer
        {
            public readonly static ActivatorProducer Instance = new ActivatorProducer();

            public ActorBase Produce(Props props)
            {
                return (ActorBase)Activator.CreateInstance(props.Type, props.Arguments);
            }

            public void Release(ActorBase actor)
            {
                //no-op
            }
        }

        private sealed class FactoryConsumer<TActor> : IIndirectActorProducer where TActor : ActorBase
        {
            private readonly Func<TActor> _factory;

            public FactoryConsumer(Func<TActor> factory)
            {
                _factory = factory;
            }

            public ActorBase Produce(Props props)
            {
                return _factory.Invoke();
            }

            public void Release(ActorBase actor)
            {
                //no-op
            }
        }

        #endregion
    }

    /// <summary>
    ///     This interface defines a class of actor creation strategies deviating from
    ///     the usual default of just reflectively instantiating the <see cref="Akka.Actor.ActorBase">Actor</see>
    ///     subclass. It can be used to allow a dependency injection framework to
    ///     determine the actual actor class and how it shall be instantiated.
    /// </summary>
    public interface IIndirectActorProducer
    {
        /// <summary>
        ///     This factory method must produce a fresh actor instance upon each
        ///     invocation. It is not permitted to return the same instance more than
        ///     once.
        /// </summary>
        /// <param name="props">The actor props</param>
        /// <returns>A fresh actor instance.</returns>
        ActorBase Produce(Props props);

        /// <summary>
        ///     This method is used by <see cref="Akka.Actor.Props" /> to signal the producer that it can
        ///     release it's reference.
        ///     <remarks>
        ///         To learn more about using Dependency Injection in .NET, see
        ///         <see href="http://www.amazon.com/Dependency-Injection-NET-Mark-Seemann/dp/1935182501">HERE</see>.
        ///     </remarks>
        /// </summary>
        /// <param name="actor">The actor to release</param>
        void Release(ActorBase actor);
    }

    /// <summary>
    /// Interface for legacy Akka.DI.Core support
    /// </summary>
    [Obsolete("Do not use this interface")]
    public interface IIndirectActorProducerWithActorType : IIndirectActorProducer
    {
        Type ActorType { get; }
    }
}
