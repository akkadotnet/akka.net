//-----------------------------------------------------------------------
// <copyright file="Deploy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Routing;
using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    /// This class represents a configuration object used in the deployment of an <see cref="Akka.Actor.ActorBase">actor</see>.
    /// </summary>
    public sealed class Deploy : IEquatable<Deploy>, ISurrogated
    {
        /// <summary>
        /// A deployment configuration that is bound to the <see cref="Akka.Actor.Scope.Local"/> scope.
        /// </summary>
        public static readonly Deploy Local = new Deploy(Scope.Local);

        /// <summary>
        /// This deployment does not have a dispatcher associated with it.
        /// </summary>
        public static readonly string NoDispatcherGiven = string.Empty;

        /// <summary>
        /// This deployment does not have a mailbox associated with it.
        /// </summary>
        public static readonly string NoMailboxGiven = string.Empty;

        /// <summary>
        /// This deployment has an unspecified scope associated with it.
        /// </summary>
        public static readonly Scope NoScopeGiven = Actor.NoScopeGiven.Instance;

        /// <summary>
        /// A deployment configuration where none of the options have been configured.
        /// </summary>
        public static readonly Deploy None = new Deploy();

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        public Deploy()
        {
            Path = "";
            Config = ConfigurationFactory.Empty;
            RouterConfig = NoRouter.Instance;
            Scope = NoScopeGiven;
            Dispatcher = NoDispatcherGiven;
            Mailbox = NoMailboxGiven;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="path">The actor path associated with this deployment.</param>
        /// <param name="scope">The scope to bind to this deployment.</param>
        public Deploy(string path, Scope scope)
            : this(scope)
        {
            Path = path;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="scope">The scope to bind to this deployment.</param>
        public Deploy(Scope scope)
            : this()
        {
            Scope = scope ?? NoScopeGiven;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="routerConfig">The router to use for this deployment.</param>
        /// <param name="scope">The scope to bind to this deployment.</param>
        public Deploy(RouterConfig routerConfig, Scope scope)
            : this()
        {
            RouterConfig = routerConfig;
            Scope = scope ?? NoScopeGiven;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="routerConfig">The router to use for this deployment.</param>
        public Deploy(RouterConfig routerConfig) : this()
        {
            RouterConfig = routerConfig;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="path">The path to deploy the actor.</param>
        /// <param name="config">The configuration used when deploying the actor.</param>
        /// <param name="routerConfig">The router used in this deployment.</param>
        /// <param name="scope">The scope to bind to this deployment.</param>
        /// <param name="dispatcher">The dispatcher used in this deployment.</param>
        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher)
            : this()
        {
            Path = path;
            Config = config;
            RouterConfig = routerConfig;
            Scope = scope ?? NoScopeGiven;
            Dispatcher = dispatcher ?? NoDispatcherGiven;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="path">The path to deploy the actor.</param>
        /// <param name="config">The configuration used when deploying the actor.</param>
        /// <param name="routerConfig">The router used in this deployment.</param>
        /// <param name="scope">The scope to bind to this deployment.</param>
        /// <param name="dispatcher">The dispatcher used in this deployment.</param>
        /// <param name="mailbox">The mailbox configured for the actor used in this deployment.</param>
        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher,
            string mailbox)
            : this()
        {
            Path = path;
            Config = config;
            RouterConfig = routerConfig;
            Scope = scope ?? NoScopeGiven;
            Dispatcher = dispatcher ?? NoDispatcherGiven;
            Mailbox = mailbox ?? NoMailboxGiven;
        }

        /// <summary>
        /// The path where the actor is deployed.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// The configuration used for this deployment.
        /// </summary>
        public Config Config { get; }

        /// <summary>
        /// The router used for this deployment.
        /// </summary>
        public RouterConfig RouterConfig { get; }

        /// <summary>
        /// The scope bound to this deployment.
        /// </summary>
        public Scope Scope { get; }

        /// <summary>
        /// The mailbox configured for the actor used in this deployment.
        /// </summary>
        public string Mailbox { get; }

        /// <summary>
        /// The dispatcher used in this deployment.
        /// </summary>
        public string Dispatcher { get; }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// <c>true</c> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(Deploy other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            
            return ((string.IsNullOrEmpty(Mailbox) && string.IsNullOrEmpty(other.Mailbox)) ||
                    string.Equals(Mailbox, other.Mailbox)) &&
                   string.Equals(Dispatcher, other.Dispatcher) &&
                   string.Equals(Path, other.Path) &&
                   RouterConfig.Equals(other.RouterConfig) &&
                   ((Config.IsNullOrEmpty() && other.Config.IsNullOrEmpty()) ||
                    Config.ToString().Equals(other.Config.ToString())) &&
                   Equals(Scope, other.Scope);
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="Deploy"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="Deploy"/>.</returns>
        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new DeploySurrogate
            {
                RouterConfig = RouterConfig,
                Scope = Scope,
                Path = Path,
                Config = Config,
                Mailbox = Mailbox,
                Dispatcher = Dispatcher
            };
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Deploy" /> from this deployment using another <see cref="Akka.Actor.Deploy" />
        /// to backfill options that might be missing from this deployment.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Deploy" />.
        /// </note>
        /// </summary>
        /// <param name="other">The <see cref="Akka.Actor.Deploy" /> used for fallback configuration.</param>
        /// <returns>A new <see cref="Akka.Actor.Deploy" /> using <paramref name="other" /> for fallback configuration.</returns>
        public Deploy WithFallback(Deploy other)
        {
            return new Deploy
                (
                Path,
                Config.Equals(other.Config) ? Config: Config.WithFallback(other.Config),
                RouterConfig.WithFallback(other.RouterConfig),
                Scope.WithFallback(other.Scope),
                Dispatcher == NoDispatcherGiven ? other.Dispatcher : Dispatcher,
                Mailbox == NoMailboxGiven ? other.Mailbox : Mailbox
                );
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Deploy" /> with a given <see cref="Akka.Actor.Scope" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Deploy" />.
        /// </note>
        /// </summary>
        /// <param name="scope">The <see cref="Akka.Actor.Scope" /> used to configure the new <see cref="Akka.Actor.Deploy" />.</param>
        /// <returns>A new <see cref="Akka.Actor.Deploy" /> with the provided <paramref name="scope" />.</returns>
        public Deploy WithScope(Scope scope)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                scope ?? Scope,
                Dispatcher,
                Mailbox
                );
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Deploy" /> with a given <paramref name="mailbox" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Deploy" />.
        /// </note>
        /// </summary>
        /// <param name="mailbox">The mailbox used to configure the new <see cref="Akka.Actor.Deploy" />.</param>
        /// <returns>A new <see cref="Akka.Actor.Deploy" /> with the provided <paramref name="mailbox" />.</returns>
        public Deploy WithMailbox(string mailbox)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                Scope,
                Dispatcher,
                mailbox
                );
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Deploy" /> with a given <paramref name="dispatcher" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Deploy" />.
        /// </note>
        /// </summary>
        /// <param name="dispatcher">The dispatcher used to configure the new <see cref="Akka.Actor.Deploy" />.</param>
        /// <returns>A new <see cref="Akka.Actor.Deploy" /> with the provided <paramref name="dispatcher" />.</returns>
        public Deploy WithDispatcher(string dispatcher)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                Scope,
                dispatcher,
                Mailbox
                );
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Deploy" /> with a given <see cref="Akka.Routing.RouterConfig" />.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Deploy" />.
        /// </note>
        /// </summary>
        /// <param name="routerConfig">The <see cref="Akka.Routing.RouterConfig" /> used to configure the new <see cref="Akka.Actor.Deploy" />.</param>
        /// <returns>A new <see cref="Akka.Actor.Deploy" /> with the provided <paramref name="routerConfig" />.</returns>
        public Deploy WithRouterConfig(RouterConfig routerConfig)
        {
            return new Deploy
                (
                Path,
                Config,
                routerConfig,
                Scope,
                Dispatcher,
                Mailbox
                );
        }

        public override bool Equals(object obj) => obj is Deploy deploy && Equals(deploy);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Path != null ? Path.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Config != null ? Config.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (RouterConfig != null ? RouterConfig.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Scope != null ? Scope.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Mailbox != null ? Mailbox.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Dispatcher != null ? Dispatcher.GetHashCode() : 0);
                return hashCode;
            }
        }

        /// <summary>
        /// This class represents a surrogate of a <see cref="Deploy"/> configuration object.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class DeploySurrogate : ISurrogate
        {
            /// <summary>
            /// The scope bound to this deployment.
            /// </summary>
            public Scope Scope { get; set; }
            /// <summary>
            /// The router used for this deployment.
            /// </summary>
            public RouterConfig RouterConfig { get; set; }
            /// <summary>
            /// The path where the actor is deployed.
            /// </summary>
            public string Path { get; set; }
            /// <summary>
            /// The configuration used for this deployment.
            /// </summary>
            public Config Config { get; set; }
            /// <summary>
            /// The mailbox configured for the actor used in this deployment.
            /// </summary>
            public string Mailbox { get; set; }
            /// <summary>
            /// The dispatcher used in this deployment.
            /// </summary>
            public string Dispatcher { get; set; }

            /// <summary>
            /// Creates a <see cref="Deploy"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="Deploy"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new Deploy(Path, Config, RouterConfig, Scope, Dispatcher, Mailbox);
            }
        }
    }
}
