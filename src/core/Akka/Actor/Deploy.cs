using Akka.Configuration;
using Akka.Routing;
using System;
using Akka.Util;

namespace Akka.Actor
{
    public class Deploy : IEquatable<Deploy> , ISurrogated
    {
        public static readonly Deploy Local = new Deploy(Scope.Local);

        public static readonly string NoDispatcherGiven = string.Empty;
        public static readonly string NoMailboxGiven = string.Empty;
        public static readonly Scope NoScopeGiven = null;
        public static readonly Deploy None = null;
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
            public object FromSurrogate(ActorSystem system)
            {
                return new Deploy(Path, Config, RouterConfig, Scope, Dispatcher);
            }

            public Scope Scope { get; set; }

            public RouterConfig RouterConfig { get; set; }
            public string Path { get; set; }
            public Config Config { get; set; }
            public string Mailbox { get; set; }
            public string Dispatcher { get; set; }
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
                Dispatcher = Dispatcher,
            };
        }

        public Deploy()
        {
            Path = "";
            Config = ConfigurationFactory.Empty;
            RouterConfig = RouterConfig.NoRouter;
            Scope = NoScopeGiven;
            Dispatcher = NoDispatcherGiven;
            Mailbox = NoMailboxGiven;
        }

        public Deploy(string path, Scope scope)
            : this(scope)
        {
            Path = path;
        }

        public Deploy(Scope scope)
            : this()
        {
            Scope = scope ?? NoScopeGiven;
        }

        public Deploy(RouterConfig routerConfig, Scope scope)
            : this()
        {
            RouterConfig = routerConfig;
            Scope = scope ?? NoScopeGiven;
        }

        public Deploy(RouterConfig routerConfig) : this()
        {
            RouterConfig = routerConfig;
        }
        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher)
            : this()
        {
            Path = path;
            Config = config;
            RouterConfig = routerConfig;
            Scope = scope ?? NoScopeGiven;
            Dispatcher = dispatcher ?? NoDispatcherGiven;
        }

        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher, string mailbox)
            : this()
        {
            Path = path;
            Config = config;
            RouterConfig = routerConfig;
            Scope = scope ?? NoScopeGiven;
            Dispatcher = dispatcher ?? NoDispatcherGiven;
            Mailbox = mailbox ?? NoMailboxGiven;
        }

        public string Path { get; private set; }
        public Config Config { get; private set; }
        public RouterConfig RouterConfig { get; private set; }
        public Scope Scope { get; private set; }
        public string Mailbox { get; private set; }
        public string Dispatcher { get; private set; }

        public Deploy WithFallback(Deploy other)
        {
            return new Deploy
            {
                Path = Path,
                Config = Config.WithFallback(other.Config),
                RouterConfig = RouterConfig.WithFallback(other.RouterConfig),
                Scope = Scope.WithFallback(other.Scope),
                Dispatcher = Dispatcher == NoDispatcherGiven ? other.Dispatcher : Dispatcher,
                Mailbox = Mailbox == NoMailboxGiven ? other.Mailbox : Mailbox,
            };
        }

        public Deploy Copy(Scope scope = null)
        {
            return new Deploy
            {
                Config = Config,
                Dispatcher = Dispatcher,
                Mailbox = Mailbox,
                Path = Path,
                RouterConfig = RouterConfig,
                Scope = scope ?? Scope
            };
        }

        public Deploy WithMailbox(string path)
        {
            var copy = Copy();
            copy.Mailbox = path;
            return copy;
        }

        public Deploy WithDispatcher(string path)
        {
            var copy = Copy();
            copy.Dispatcher = path;
            return copy;
        }

        public Deploy WithRouterConfig(RouterConfig routerConfig)
        {
            var copy = Copy();
            copy.RouterConfig = routerConfig;
            return copy;
        }

        public bool Equals(Deploy other)
        {
            if (other == null) return false;
            return ((string.IsNullOrEmpty(Mailbox) && string.IsNullOrEmpty(other.Mailbox)) || string.Equals(Mailbox, other.Mailbox)) &&
                   string.Equals(Dispatcher, other.Dispatcher) &&
                   string.Equals(Path, other.Path) &&
                   RouterConfig.Equals(other.RouterConfig) &&
                   ((Config.IsNullOrEmpty() && other.Config.IsNullOrEmpty()) || Config.ToString().Equals(other.Config.ToString())) &&
                   (Scope == null && other.Scope == null || (Scope != null && Scope.Equals(other.Scope)));
        }
    }
}
