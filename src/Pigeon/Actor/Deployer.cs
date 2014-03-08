using System;
using System.Data;
using System.Linq;
using Akka.Configuration;
using Akka.Routing;

namespace Akka.Actor
{
    public class Deploy
    {
        public static readonly Deploy Local = new Deploy(Scope.Local);

        public static readonly string NoDispatcherGiven = null;
        public static readonly string NoMailboxGiven = null;
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

        public Deploy(Scope scope)
            : this()
        {
            Scope = scope;
        }

        public Deploy(RouterConfig routerConfig, Scope scope)
            : this()
        {
            RouterConfig = routerConfig;
            Scope = scope;
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
            Scope = scope;
            Dispatcher = dispatcher;
        }

        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher,string mailbox)
        {
            Path = path;
            Config = config;
            RouterConfig = routerConfig;
            Scope = scope;
            Dispatcher = dispatcher;
            Mailbox = mailbox;
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

        private Deploy Copy()
        {
            return new Deploy
            {
                Config = Config,
                Dispatcher = Dispatcher,
                Mailbox = Mailbox,
                Path = Path,
                RouterConfig = RouterConfig,
                Scope = Scope,
            };
        }
    }

    public class Scope
    {
        public static readonly LocalScope Local = new LocalScope();
        private Scope fallback;

        public Scope WithFallback(Scope other)
        {
            Scope copy = Copy();
            copy.fallback = other;
            return copy;
        }

        private Scope Copy()
        {
            return new Scope
            {
                fallback = fallback,
            };
        }
    }

    public class LocalScope : Scope
    {
    }

    public class RemoteScope : Scope
    {
        [Obsolete("For Serialization only", true)]
        public RemoteScope()
        {
        }

        public RemoteScope(Address address)
        {
            Address = address;
        }

        public Address Address { get; set; }
    }


    public class Deployer
    {
        private readonly Config deployment;
        private readonly Config @default;
        private Settings settings;

        public Deployer(Settings settings)
        {
            this.settings = settings;
            deployment = settings.Config.GetConfig("akka.actor.deployment");
            @default = deployment.GetConfig("default");
        }

        public Deploy Lookup(ActorPath path)
        {
            if (path.Elements.Head() != "user" || path.Elements.Count() < 2)
                return Deploy.Local;

            var elements = path.Elements.Drop(1);
            var pathStr = "/" + elements.Join("/");            
            var deploy = ParseConfig(pathStr);
            return deploy;
        }

  //        def parseConfig(key: String, config: Config): Option[Deploy] = {
  //  val deployment = config.withFallback(default)
  //  val router = createRouterConfig(deployment.getString("router"), key, config, deployment)
  //  val dispatcher = deployment.getString("dispatcher")
  //  val mailbox = deployment.getString("mailbox")
  //  Some(Deploy(key, deployment, router, NoScopeGiven, dispatcher, mailbox))
  //}

        private Deploy ParseConfig(string key)
        {
            Config config = deployment.GetConfig(key).WithFallback(@default);
            var routerType = config.GetString("router");
            var router = CreateRouterConfig(routerType, key, config, deployment);
            var dispatcher = config.GetString("dispatcher");
            var mailbox = config.GetString("mailbox");
            var deploy = new Deploy(key, deployment, router, Deploy.NoScopeGiven,dispatcher,mailbox);
            return deploy;
        }

        private RouterConfig CreateRouterConfig(string routerType, string key, Config config, Config deployment)
        {
            if (routerType == "from-code")
                return RouterConfig.NoRouter;

            throw new NotImplementedException("Implement this");
        }
    }
}