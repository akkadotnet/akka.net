//-----------------------------------------------------------------------
// <copyright file="Props.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public class Props : IEquatable<Props>, ISurrogated
    {
        private const string NullActorTypeExceptionText = "Props must be instantiated with an actor type.";
        private static readonly Deploy DefaultDeploy = new Deploy();
        private static readonly object[] NoArgs = { };

        /// <summary>
        /// A pre-configured <see cref="Akka.Actor.Props"/> that creates an actor that doesn't respond to messages.
        /// </summary>
        public static readonly Props Empty = Props.Create<EmptyActor>();

        /// <summary>
        /// A pre-configured <see cref="Akka.Actor.Props"/> that doesn't create actors. 
        /// <note>
        /// The value of this field is null.
        /// </note>
        /// </summary>
        public static readonly Props None = null;

        [JsonIgnore]
        protected Func<object> Producer;

        /// <summary>
        /// The type of the actor that is created.
        /// </summary>
        public Type Type { get; }

        /// <summary>
        /// The configuration used to deploy the actor.
        /// </summary>
        public Deploy Deploy { get; }

        /// <summary>
        /// The supervisor strategy used to manage the actor.
        /// </summary>
        public SupervisorStrategy SupervisorStrategy { get; }

        /// <summary>
        /// The arguments needed to create the actor.
        /// </summary>
        public object[] Arguments { get; }

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
        public string Mailbox => Deploy.Mailbox;

        /// <summary>
        /// The assembly qualified name of the type of the actor that is created.
        /// </summary>
        public string TypeName => Type.AssemblyQualifiedName;

        /// <summary>
        /// The router used in the deployment of the actor.
        /// </summary>
        [JsonIgnore]
        public RouterConfig RouterConfig => Deploy.RouterConfig;

        /// <summary>
        /// Initializes a new instance of the <see cref="Props"/> class.
        /// </summary>
        protected Props()
        {
            Deploy = DefaultDeploy;
            Arguments = NoArgs;
        }

        public Props(Type type, object[] args, Deploy deploy = null, SupervisorStrategy strategy = null, Func<object> producer = null)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!typeof(ActorBase).IsAssignableFrom(type))
                throw new ArgumentException($"Type [{type}] cannot be used in props as it's not an actor type", nameof(type));

            this.Type = type;
            this.Arguments = args;
            this.Deploy = deploy ?? DefaultDeploy;
            this.SupervisorStrategy = strategy;
            this.Producer = producer;
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
            : this(type: type, args: args, deploy: DefaultDeploy, strategy: null, producer: () => Activator.CreateInstance(type, args))
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
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if <see cref="Props"/> is not instantiated with an actor type.
        /// </exception>

        public Props(Type type)
            : this(type: type, args: NoArgs, deploy: DefaultDeploy, strategy: null, producer: null)
        {
        }

        /// <summary>
        /// Creates an actor using a specified lambda expression.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="factory">The lambda expression used to create the actor.</param>
        /// <param name="supervisorStrategy">Optional: The supervisor strategy used to manage the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        /// <exception cref="ArgumentException">The create function must be a 'new T (args)' expression</exception>
        public static Props Create<TActor>(Expression<Func<TActor>> factory, SupervisorStrategy supervisorStrategy = null) where TActor : ActorBase
        {
            if (factory.Body is UnaryExpression)
                return new DynamicProps<TActor>(factory.Compile());

            var newExpression = factory.Body.AsInstanceOf<NewExpression>();
            if (newExpression == null)
                throw new ArgumentException("The create function must be a 'new T (args)' expression");

            object[] args = newExpression.GetArguments().ToArray();

            return new Props(
                type: typeof(TActor),
                args: args,
                deploy: DefaultDeploy,
                strategy: supervisorStrategy,
                producer: () => Activator.CreateInstance(typeof(TActor), args));
        }

        /// <summary>
        /// Creates an actor using partially applied arguments given 
        /// as provided <paramref name="anonymousObject"/> eg.: 
        /// <c>Props.Dynamic&lt;MyActor&gt;(new { ctorField = "value" })</c> 
        /// </summary>
        /// <typeparam name="TActor"></typeparam>
        /// <param name="anonymousObject"></param>
        /// <returns></returns>
        public static Props Dynamic<TActor>(object anonymousObject) where TActor : ActorBase
        {
            var tActor = typeof(TActor);
            var args = ReflectionHelpers.ArgsFromDynamic(tActor, anonymousObject);
            return new Props(
                type: tActor,
                args: args,
                deploy: DefaultDeploy,
                strategy: null,
                producer: null);
        }

        /// <summary>
        /// Creates an actor using the given arguments.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props Create<TActor>(params object[] args) where TActor : ActorBase
        {
            Func<object> producer = !(args == null || args.Length == 0)
                ? () => Activator.CreateInstance(typeof(TActor), args)
                : default(Func<object>);

            return new Props(
                type: typeof(TActor),
                args: args,
                deploy: DefaultDeploy,
                strategy: null,
                producer: producer);
        }

        /// <summary>
        /// Creates an actor using a specified supervisor strategy.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props Create<TActor>(SupervisorStrategy supervisorStrategy) where TActor : ActorBase, new()
        {
            return new Props(
                type: typeof(TActor),
                args: NoArgs,
                deploy: DefaultDeploy,
                strategy: supervisorStrategy,
                producer: null);
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
            return new Props(
                type: this.Type,
                args: this.Arguments,
                deploy: this.Deploy.WithMailbox(mailbox),
                strategy: this.SupervisorStrategy,
                producer: this.Producer);
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
            return new Props(
                type: this.Type,
                args: this.Arguments,
                deploy: this.Deploy.WithDispatcher(dispatcher),
                strategy: this.SupervisorStrategy,
                producer: this.Producer);
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given router. 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="routerConfig">The router used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="routerConfig" />.</returns>
        public Props WithRouter(RouterConfig routerConfig)
        {
            return new Props(
                type: this.Type,
                args: this.Arguments,
                deploy: this.Deploy.WithRouterConfig(routerConfig),
                strategy: this.SupervisorStrategy,
                producer: this.Producer);
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given deployment configuration.
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="deploy" />.</returns>
        public Props WithDeploy(Deploy deploy)
        {
            return new Props(
                type: this.Type,
                args: this.Arguments,
                deploy: deploy.WithFallback(this.Deploy),
                strategy: this.SupervisorStrategy,
                producer: this.Producer);
        }

        ///  <summary>
        ///  Creates a new <see cref="Akka.Actor.Props" /> with a given supervisor strategy. 
        ///  <note>
        ///  This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        ///  </note>
        ///  </summary>
        ///  <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="supervisorStrategy" />.</returns>
        public Props WithSupervisorStrategy(SupervisorStrategy supervisorStrategy)
        {
            return new Props(
                type: this.Type,
                args: this.Arguments,
                deploy: this.Deploy,
                strategy: supervisorStrategy,
                producer: this.Producer);
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
        public virtual ActorBase NewActor(IDependencyResolver resolver)
        {
            var type = Type;
            var arguments = Arguments;
            try
            {
                if (Producer == null)
                {
                    Producer = resolver.ResolveWithArgs(this.Type, this.Arguments);
                }

                return (ActorBase)Producer();
            }
            catch (Exception e)
            {
                throw new TypeLoadException($"Error while creating actor instance of type {type} with {arguments.Length} args: ({StringFormat.SafeJoin(",", arguments)})", e);
            }
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


        #endregion

        /// <summary>
        /// Signals the producer that it can release its reference to the actor.
        /// </summary>
        /// <param name="actor">The actor to release</param>
        internal void Release(ActorBase actor)
        {
            try
            {
                var withStash = actor as IActorStash;
                withStash?.Stash.UnstashAll();
            }
            finally
            {
                actor = null;
            }
        }

        #region surrogates

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
                return new Props(
                    type: Type,
                    args: Arguments,
                    deploy: Deploy,
                    strategy: null,
                    producer: system.DependencyResolver.ResolveWithArgs(Type, Arguments));
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

        #endregion

        #region equality & hash code

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
            return this.Type == other.Type && Equals(this.Deploy, other.Deploy) && CompareSupervisorStrategy(other) && CompareArguments(other);
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
            return Equals((Props)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (Deploy != null ? Deploy.GetHashCode() : 0);
                //  hashCode = (hashCode*397) ^ (SupervisorStrategy != null ? SupervisorStrategy.GetHashCode() : 0);
                //  hashCode = (hashCode*397) ^ (Arguments != null ? Arguments.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Type.GetHashCode();
                return hashCode;
            }
        }

        #endregion
    }

    /// <summary>
    /// This class represents a specialized <see cref="Akka.Actor.Props"/> used when the actor has been terminated.
    /// </summary>
    internal class TerminatedProps : Props
    {
        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="InvalidOperationException">This exception is thrown automatically since the actor has been terminated.</exception>
        /// <returns>N/A</returns>
        public override ActorBase NewActor(IDependencyResolver resolver)
        {
            throw new InvalidOperationException("This actor has been terminated");
        }
    }

    /// <summary>
    /// This class represents a specialized <see cref="Akka.Actor.Props"/> that uses dynamic invocation
    /// to create new actor instances, rather than a using heavy <see cref="IDependencyResolver"/>.
    /// 
    /// <note>
    /// This is intended to be used in conjunction with Dependency Injection.
    /// </note>
    /// </summary>
    /// <typeparam name="TActor">The type of the actor to create.</typeparam>
    internal class DynamicProps<TActor> : Props where TActor : ActorBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DynamicProps{TActor}" /> class.
        /// </summary>
        /// <param name="invoker">The factory method used to create an actor.</param>
        public DynamicProps(Func<TActor> invoker)
            : base(typeof(TActor))
        {
            this.Producer = invoker;
        }

        /// <summary>
        /// Creates a new actor using the configured factory method.
        /// </summary>
        /// <returns>The actor created using the factory method.</returns>
        public override ActorBase NewActor(IDependencyResolver resolver) => (ActorBase)this.Producer();

        #region Copy methods

        private DynamicProps(Props copy, Func<TActor> invoker)
            : base(type: copy.Type, args: copy.Arguments, deploy: copy.Deploy, strategy: copy.SupervisorStrategy, producer: invoker)
        {
        }

        #endregion
    }
}
