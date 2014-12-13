using Akka.Configuration;
using Akka.Dispatch;
using Akka.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public class Deploy
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

        public Deploy(RouterConfig routerConfig)
        {
            RouterConfig = routerConfig;
        }
        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher)
        {
            Path = path;
            Config = config;
            RouterConfig = routerConfig;
            Scope = scope ?? NoScopeGiven;
            Dispatcher = dispatcher ?? NoDispatcherGiven;
        }

        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher, string mailbox)
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
    }
}
