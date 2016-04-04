//-----------------------------------------------------------------------
// <copyright file="Deploy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Routing;
using Akka.Util;

namespace Akka.Actor
{
    public class Deploy : IEquatable<Deploy>, ISurrogated
    {
        public static readonly Deploy Local = new Deploy(Scope.Local);
        public static readonly string NoDispatcherGiven = string.Empty;
        public static readonly string NoMailboxGiven = string.Empty;
        public static readonly Scope NoScopeGiven = Actor.NoScopeGiven.Instance;
        public static readonly Deploy None = new Deploy();
        private readonly Config _config;
        private readonly string _dispatcher;
        private readonly string _mailbox;
        private readonly string _path;
        private readonly RouterConfig _routerConfig;
        private readonly Scope _scope;

        public Deploy()
        {
            _path = "";
            _config = ConfigurationFactory.Empty;
            _routerConfig = RouterConfig.NoRouter;
            _scope = NoScopeGiven;
            _dispatcher = NoDispatcherGiven;
            _mailbox = NoMailboxGiven;
        }

        public Deploy(string path, Scope scope)
            : this(scope)
        {
            _path = path;
        }

        public Deploy(Scope scope)
            : this()
        {
            _scope = scope ?? NoScopeGiven;
        }

        public Deploy(RouterConfig routerConfig, Scope scope)
            : this()
        {
            _routerConfig = routerConfig;
            _scope = scope ?? NoScopeGiven;
        }

        public Deploy(RouterConfig routerConfig) : this()
        {
            _routerConfig = routerConfig;
        }

        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher)
            : this()
        {
            _path = path;
            _config = config;
            _routerConfig = routerConfig;
            _scope = scope ?? NoScopeGiven;
            _dispatcher = dispatcher ?? NoDispatcherGiven;
        }

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
        }

        public string Path
        {
            get { return _path; }
        }

        public Config Config
        {
            get { return _config; }
        }

        public RouterConfig RouterConfig
        {
            get { return _routerConfig; }
        }

        public Scope Scope
        {
            get { return _scope; }
        }

        public string Mailbox
        {
            get { return _mailbox; }
        }

        public string Dispatcher
        {
            get { return _dispatcher; }
        }

        public bool Equals(Deploy other)
        {
            if (other == null) return false;
            return ((string.IsNullOrEmpty(_mailbox) && string.IsNullOrEmpty(other._mailbox)) ||
                    string.Equals(_mailbox, other._mailbox)) &&
                   string.Equals(_dispatcher, other._dispatcher) &&
                   string.Equals(_path, other._path) &&
                   _routerConfig.Equals(other._routerConfig) &&
                   ((_config.IsNullOrEmpty() && other._config.IsNullOrEmpty()) ||
                    _config.ToString().Equals(other._config.ToString())) &&
                   (_scope == null && other._scope == null || (_scope != null && _scope.Equals(other._scope)));
        }

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

        public Deploy WithMailbox(string path)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                Scope,
                Dispatcher,
                path
                );
        }

        public Deploy WithDispatcher(string path)
        {
            return new Deploy
                (
                Path,
                Config,
                RouterConfig,
                Scope,
                path,
                Mailbox
                );
        }

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

        /*
         path: String = "",
  config: Config = ConfigFactory.empty,
  routerConfig: RouterConfig = NoRouter,
  scope: Scope = NoScopeGiven,
  dispatcher: String = Deploy.NoDispatcherGiven,
  mailbox: String = Deploy.NoMailboxGiven)
         */

        public class DeploySurrogate : ISurrogate
        {
            public Scope Scope { get; set; }
            public RouterConfig RouterConfig { get; set; }
            public string Path { get; set; }
            public Config Config { get; set; }
            public string Mailbox { get; set; }
            public string Dispatcher { get; set; }

            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new Deploy(Path, Config, RouterConfig, Scope, Dispatcher, Mailbox);
            }
        }
    }
}

