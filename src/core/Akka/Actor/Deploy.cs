﻿//-----------------------------------------------------------------------
// <copyright file="Deploy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    public class Deploy : IEquatable<Deploy>, ISurrogated
    {
        /// <summary>
        /// A deployment configuration that is bound to the <see cref="Akka.Actor.Scope.Local"/> scope.
        /// </summary>
        public static readonly Deploy Local = new(Scope.Local);
        /// <summary>
        /// This deployment does not have a dispatcher associated with it.
        /// </summary>
        public static readonly string NoDispatcherGiven = string.Empty;
        /// <summary>
        /// This deployment does not have a mailbox associated with it.
        /// </summary>
        public static readonly string NoMailboxGiven = string.Empty;
        
        /// <summary>
        /// No stash size set.
        /// </summary>
        public const int NoStashSize = -1;

        internal const string DispatcherSameAsParent = "..";

        /// <summary>
        /// This deployment has an unspecified scope associated with it.
        /// </summary>
        public static readonly Scope NoScopeGiven = Actor.NoScopeGiven.Instance;
        /// <summary>
        /// A deployment configuration where none of the options have been configured.
        /// </summary>
        public static readonly Deploy None = new();
        private readonly Config _config;
        private readonly string _dispatcher;
        private readonly string _mailbox;
        private readonly string _path;
        private readonly int _boundedStashCapacity;
        private readonly RouterConfig _routerConfig;
        private readonly Scope _scope;

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        public Deploy()
        {
            _path = "";
            _config = ConfigurationFactory.Empty;
            _routerConfig = NoRouter.Instance;
            _scope = NoScopeGiven;
            _dispatcher = NoDispatcherGiven;
            _mailbox = NoMailboxGiven;
            _boundedStashCapacity = NoStashSize;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="path">The actor path associated with this deployment.</param>
        /// <param name="scope">The scope to bind to this deployment.</param>
        public Deploy(string path, Scope scope)
            : this(scope)
        {
            _path = path;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="scope">The scope to bind to this deployment.</param>
        public Deploy(Scope scope)
            : this()
        {
            _scope = scope ?? NoScopeGiven;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="routerConfig">The router to use for this deployment.</param>
        /// <param name="scope">The scope to bind to this deployment.</param>
        public Deploy(RouterConfig routerConfig, Scope scope)
            : this()
        {
            _routerConfig = routerConfig;
            _scope = scope ?? NoScopeGiven;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deploy"/> class.
        /// </summary>
        /// <param name="routerConfig">The router to use for this deployment.</param>
        public Deploy(RouterConfig routerConfig) : this()
        {
            _routerConfig = routerConfig;
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
            _path = path;
            _config = config;
            _routerConfig = routerConfig;
            _scope = scope ?? NoScopeGiven;
            _dispatcher = dispatcher ?? NoDispatcherGiven;
            _boundedStashCapacity = NoStashSize;
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
            _path = path;
            _config = config;
            _routerConfig = routerConfig;
            _scope = scope ?? NoScopeGiven;
            _dispatcher = dispatcher ?? NoDispatcherGiven;
            _mailbox = mailbox ?? NoMailboxGiven;
            _boundedStashCapacity = NoStashSize; //means unset
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
        /// <param name="stashCapacity">If this actor is using a stash, the bounded stash size.</param>
        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher,
            string mailbox, int stashCapacity)
            : this()
        {
            _path = path;
            _config = config;
            _routerConfig = routerConfig;
            _scope = scope ?? NoScopeGiven;
            _dispatcher = dispatcher ?? NoDispatcherGiven;
            _mailbox = mailbox ?? NoMailboxGiven;
            _boundedStashCapacity = stashCapacity;
        }

        /// <summary>
        /// The path where the actor is deployed.
        /// </summary>
        public string Path
        {
            get { return _path; }
        }

        /// <summary>
        /// The configuration used for this deployment.
        /// </summary>
        public Config Config
        {
            get { return _config; }
        }

        /// <summary>
        /// The router used for this deployment.
        /// </summary>
        public RouterConfig RouterConfig
        {
            get { return _routerConfig; }
        }

        /// <summary>
        /// The scope bound to this deployment.
        /// </summary>
        public Scope Scope
        {
            get { return _scope; }
        }

        /// <summary>
        /// The mailbox configured for the actor used in this deployment.
        /// </summary>
        public string Mailbox
        {
            get { return _mailbox; }
        }

        /// <summary>
        /// The dispatcher used in this deployment.
        /// </summary>
        public string Dispatcher
        {
            get { return _dispatcher; }
        }

        /// <summary>
        /// The size of the <see cref="IStash"/>, if there's one configured.
        /// </summary>
        /// <remarks>
        /// Defaults to -1, which means an unbounded stash.
        /// </remarks>
        public int StashCapacity
        {
            get
            {
                return _boundedStashCapacity;
            }
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// <c>true</c> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(Deploy other)
        {
            if (other == null) return false;
            return ((string.IsNullOrEmpty(_mailbox) && string.IsNullOrEmpty(other._mailbox)) ||
                    string.Equals(_mailbox, other._mailbox)) &&
                   string.Equals(_dispatcher, other._dispatcher) &&
                   string.Equals(_path, other._path) &&
                     _boundedStashCapacity == other._boundedStashCapacity &&
                   _routerConfig.Equals(other._routerConfig) &&
                   ((_config.IsNullOrEmpty() && other._config.IsNullOrEmpty()) ||
                    _config.Root.ToString().Equals(other._config.Root.ToString())) &&
                   (_scope == null && other._scope == null || (_scope != null && _scope.Equals(other._scope)));
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
                Dispatcher = Dispatcher,
                StashCapacity = StashCapacity
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
        public virtual Deploy WithFallback(Deploy other)
        {
            return new Deploy
                (
                Path,
                Config.Equals(other.Config) ? Config: Config.WithFallback(other.Config),
                RouterConfig.WithFallback(other.RouterConfig),
                Scope.WithFallback(other.Scope),
                Dispatcher == NoDispatcherGiven ? other.Dispatcher : Dispatcher,
                Mailbox == NoMailboxGiven ? other.Mailbox : Mailbox,
                StashCapacity == -1 ? other.StashCapacity : StashCapacity
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
        public virtual Deploy WithScope(Scope scope)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                scope ?? Scope,
                Dispatcher,
                Mailbox,
                StashCapacity
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
        public virtual Deploy WithMailbox(string mailbox)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                Scope,
                Dispatcher,
                mailbox,
                StashCapacity
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
        public virtual Deploy WithDispatcher(string dispatcher)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                Scope,
                dispatcher,
                Mailbox,
                StashCapacity
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
        public virtual Deploy WithRouterConfig(RouterConfig routerConfig)
        {
            return new Deploy
                (
                Path,
                Config,
                routerConfig,
                Scope,
                Dispatcher,
                Mailbox,
                StashCapacity
                );
        }
        
        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Deploy" /> with a given <paramref name="stashSize" />.
        ///
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Deploy" />.
        /// </note>
        /// </summary>
        /// The size of the <see cref="IStash"/>, if there's one configured.
        /// <returns>A new <see cref="Akka.Actor.Deploy" /> with a given <paramref name="stashSize" />.</returns>
        public virtual Deploy WithStashCapacity(int stashSize)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                Scope,
                Dispatcher,
                Mailbox,
                stashSize
                );
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
            /// The size of the stash used in this deployment.
            /// </summary>
            public int StashCapacity { get; set; }

            /// <summary>
            /// Creates a <see cref="Deploy"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="Deploy"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new Deploy(Path, Config, RouterConfig, Scope, Dispatcher, Mailbox, StashCapacity);
            }
        }
    }
}
