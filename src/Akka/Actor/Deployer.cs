using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Routing;
using Akka.Tools;

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

        public Deploy(string path, Scope scope) : this(scope)
        {
            Path = path;
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

        public Deploy(string path, Config config, RouterConfig routerConfig, Scope scope, string dispatcher, string mailbox)
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

        public Deploy Copy()
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

    public class Scope
    {
        public static readonly LocalScope Local = new LocalScope();
        private Scope _fallback;

        public Scope WithFallback(Scope other)
        {
            Scope copy = Copy();
            copy._fallback = other;
            return copy;
        }

        private Scope Copy()
        {
            return new Scope
            {
                _fallback = _fallback,
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
        private readonly Config _deployment;
        private readonly Config _default;
        private readonly Settings _settings;
        private readonly AtomicReference<WildcardTree<Deploy>> _deployments = new AtomicReference<WildcardTree<Deploy>>(new WildcardTree<Deploy>());

        public Deployer(Settings settings)
        {
            _settings = settings;
            _deployment = settings.Config.GetConfig("akka.actor.deployment");
            _default = _deployment.GetConfig("default");
            Init();
        }

        private void Init()
        {
            var rootObj = deployment.Root.GetObject();
            if (rootObj == null) return;
            var unwrapped = rootObj.Unwrapped.Where(d => !d.Key.Equals("default")).ToArray();
            foreach (var d in unwrapped.Select(x => ParseConfig(x.Key)))
            {
                SetDeploy(d);
            }
        }

        public Deploy Lookup(ActorPath path)
        {
            if (path.Elements.Head() != "user" || path.Elements.Count() < 2)
                return Deploy.None;

            var elements = path.Elements.Drop(1);
            return Lookup(elements);
        }

        public Deploy Lookup(IEnumerable<string> path)
        {
            return Lookup(path.GetEnumerator());
        }

        public Deploy Lookup(IEnumerator<string> path)
        {
            return _deployments.Value.Find(path).Data;
        }

        public void SetDeploy(Deploy deploy)
        {
            Action<IList<string>, Deploy> add = (path, d) =>
            {
                bool set;
                do
                {
                    var w = _deployments.Value;
                    foreach (var t in path)
                    {
                        var curPath = t;
                        if (string.IsNullOrEmpty(curPath))
                            throw new IllegalActorNameException(string.Format("Actor name in deployment [{0}] must not be empty", d.Path));
                        if (!ActorPath.ElementRegex.IsMatch(t))
                        {
                            throw new IllegalActorNameException(string.Format("Actor name in deployment [{0}] must conform to {1}", d.Path, ActorPath.ElementRegex));
                        }
                    }
                    set = _deployments.CompareAndSet(w, w.Insert(path.GetEnumerator(), d));
                } while(!set);
            };

            add(deploy.Path.Split('/').Drop(1).ToList(), deploy);
        }

        protected virtual Deploy ParseConfig(string key)
        {
            Config config = _deployment.GetConfig(key).WithFallback(_default);
            var routerType = config.GetString("router");
            var router = CreateRouterConfig(routerType, key, config, _deployment);
            var dispatcher = config.GetString("dispatcher");
            var mailbox = config.GetString("mailbox");
            var scope = ParseScope(config);
            var deploy = new Deploy(key, deployment, router, scope, dispatcher, mailbox);
            return deploy;
        }

        protected virtual Scope ParseScope(Config config)
        {
            return Deploy.NoScopeGiven;
        }

        private RouterConfig CreateRouterConfig(string routerTypeAlias, string key, Config config, Config deployment)
        {
            if (routerTypeAlias == "from-code")
                return RouterConfig.NoRouter;

            var path = string.Format("akka.actor.router.type-mapping.{0}", routerTypeAlias);
            var routerTypeName = _settings.Config.GetString(path);
            var routerType = Type.GetType(routerTypeName);
            Debug.Assert(routerType != null, "routerType != null");
            var routerConfig = (RouterConfig)Activator.CreateInstance(routerType, config);

            return routerConfig;
        }
    }
}