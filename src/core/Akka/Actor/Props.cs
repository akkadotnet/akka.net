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
    public class Props : IEquatable<Props> , ISurrogated
    {
        private sealed class EmptyActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }

        private const string NullActorTypeExceptionText = "Props must be instantiated with an actor type.";
        private static readonly object[] noArgs = { };

        /// <summary>
        /// A pre-configured <see cref="Akka.Actor.Props"/> that creates an actor that doesn't respond to messages.
        /// </summary>
        public static Props Empty { get; } = Props.Create(() => new EmptyActor());

        /// <summary>
        /// A pre-configured <see cref="Akka.Actor.Props"/> that doesn't create actors.
        /// 
        /// <note>
        /// The value of this field is null.
        /// </note>
        /// </summary>
        public static Props None { get; } = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="Props"/> class.
        /// </summary>
        protected Props()
            : this(Deploy.None, null, noArgs)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="copy">The object that is being cloned.</param>
        protected Props(Props copy)
            : this(copy.Deploy, copy.Type, copy.SupervisorStrategy, copy.Arguments)
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
            : this(Deploy.None, type, args)
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
            : this(type, noArgs, null, null)
        {
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
            : this(type, args.ToArray(), Deploy.None, supervisorStrategy)
        {
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
            : this(type, args, Deploy.None, supervisorStrategy)
        {
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
            : this(type, args.ToArray(), deploy, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Props" /> class.
        /// </summary>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <exception cref="ArgumentException">This exception is thrown if <paramref name="type"/> is an unknown actor producer.</exception>
        public Props(Deploy deploy, Type type, params object[] args)
            : this(type, args, deploy, null)
        {
        }

        protected Props(Type type, object[] args, Deploy deploy, SupervisorStrategy supervisorStrategy)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type), NullActorTypeExceptionText);
            Arguments = args ?? noArgs;
            Deploy = deploy ?? Deploy.None;
            SupervisorStrategy = supervisorStrategy;
        }

        /// <summary>
        /// The type of the actor that is created.
        /// </summary>
        [JsonIgnore]
        public Type Type { get; }

        /// <summary>
        /// The arguments needed to create the actor.
        /// </summary>
        public object[] Arguments { get; }

        /// <summary>
        /// The configuration used to deploy the actor.
        /// </summary>
        public Deploy Deploy { get; }

        /// <summary>
        /// The supervisor strategy used to manage the actor.
        /// </summary>
        public SupervisorStrategy SupervisorStrategy { get; }

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
        /// Creates an actor using a specified lambda expression.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="factory">The lambda expression used to create the actor.</param>
        /// <param name="supervisorStrategy">Optional: The supervisor strategy used to manage the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        /// <exception cref="ArgumentException">The create function must be a 'new T (args)' expression</exception>
        public static Props Create<TActor>(Expression<Func<TActor>> factory, SupervisorStrategy supervisorStrategy=null) where TActor : ActorBase
        {
            var newExpression = factory.Body.AsInstanceOf<NewExpression>();
            if (newExpression == null)
                throw new ArgumentException("The create function must be a 'new T (args)' expression");

            var args = newExpression.GetArguments().ToArray();

            return new Props(typeof (TActor), supervisorStrategy, args);
        }

        /// <summary>
        /// Creates an actor using the given arguments.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props Create<TActor>(params object[] args) where TActor : ActorBase => new Props(typeof(TActor), args);
        
        /// <summary>
        /// Creates an actor using a specified supervisor strategy.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor to create.</typeparam>
        /// <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        public static Props Create<TActor>(SupervisorStrategy supervisorStrategy) where TActor : ActorBase, new() => new Props(typeof(TActor), supervisorStrategy);

        /// <summary>
        /// Creates an actor of a specified type.
        /// </summary>
        /// <param name="type">The type of the actor to create.</param>
        /// <param name="args">The arguments needed to create the actor.</param>
        /// <returns>The newly created <see cref="Akka.Actor.Props" />.</returns>
        /// <exception cref="ArgumentNullException">Props must be instantiated with an actor type.</exception>
        public static Props Create(Type type, params object[] args) => new Props(type, args);

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given <paramref name="mailbox" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="mailbox">The mailbox used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="mailbox" />.</returns>
        public Props WithMailbox(string mailbox) => new Props(Type, Arguments, Deploy.WithMailbox(mailbox), SupervisorStrategy);

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given <paramref name="dispatcher" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="dispatcher" />.</returns>
        public Props WithDispatcher(string dispatcher) => new Props(Type, Arguments, Deploy.WithDispatcher(dispatcher), SupervisorStrategy);

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given router.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="routerConfig">The router used when deploying the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="routerConfig" />.</returns>
        public Props WithRouter(RouterConfig routerConfig) => new Props(Type, Arguments, Deploy.WithRouterConfig(routerConfig), SupervisorStrategy);

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Props" /> with a given deployment configuration.
        ///
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        /// </note>
        /// </summary>
        /// <param name="deploy">The configuration used to deploy the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="deploy" />.</returns>
        public Props WithDeploy(Deploy deploy) =>
            // TODO: this is a hack designed to preserve explicit router deployments https://github.com/akkadotnet/akka.net/issues/546
            // in reality, we should be able to do copy.Deploy = deploy.WithFallback(copy.Deploy); but that blows up at the moment
            // - Aaron Stannard
            new Props(Type, Arguments, deploy.WithFallback(Deploy), SupervisorStrategy);

        ///  <summary>
        ///  Creates a new <see cref="Akka.Actor.Props" /> with a given supervisor strategy.
        /// 
        ///  <note>
        ///  This method is immutable and returns a new instance of <see cref="Akka.Actor.Props" />.
        ///  </note>
        ///  </summary>
        ///  <param name="supervisorStrategy">The supervisor strategy used to manage the actor.</param>
        /// <returns>A new <see cref="Akka.Actor.Props" /> with the provided <paramref name="supervisorStrategy" />.</returns>
        public Props WithSupervisorStrategy(SupervisorStrategy supervisorStrategy) => new Props(Type, Arguments, Deploy, supervisorStrategy);
        
        internal virtual ActorBase CreateActor() => (ActorBase)Activator.CreateInstance(Type, Arguments);

        internal virtual void ReleaseActor(ActorBase actor) { }

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
            return new PropsSurrogate
            {
                Arguments = Arguments,
                Type = Type,
                Deploy = Deploy,
            };
        }

        #endregion

        #region equality operators
        
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
            if (GetType() != other.GetType()) return false;

            return Deploy.Equals(other.Deploy)
                   && CompareArguments(other)
                   && Type == other.Type;
        }

        private bool CompareArguments(Props other)
        {
            if (ReferenceEquals(Arguments, other.Arguments)) return true;
            if (ReferenceEquals(Arguments, null)) return false;
            if (ReferenceEquals(other.Arguments, null)) return false;

            //TODO: since arguments can be serialized, we can not compare by ref
            //arguments may also not implement equality operators, so we can not structurally compare either
            //we can not just call a serializer and compare outputs either, since different args may require diff serializer mechanics

            return Arguments.Length == other.Arguments.Length;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Props props && Equals(props);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (Deploy != null ? Deploy.GetHashCode() : 0);
                //  hashCode = (hashCode*397) ^ (SupervisorStrategy != null ? SupervisorStrategy.GetHashCode() : 0);
                //  hashCode = (hashCode*397) ^ (Arguments != null ? Arguments.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Type?.GetHashCode() ?? 0);
                return hashCode;
            }
        }

        #endregion
    }
}

