using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Routing;
using Akka.Util;

namespace Akka.Actor
{
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
            var rootObj = _deployment.Root.GetObject();
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
            var elements = deploy.Path.Split('/').Drop(1).ToList();
            add(elements, deploy);
        }

        protected virtual Deploy ParseConfig(string key)
        {
            Config config = _deployment.GetConfig(key).WithFallback(_default);
            var routerType = config.GetString("router");
            var router = CreateRouterConfig(routerType, key, config, _deployment);
            var dispatcher = config.GetString("dispatcher");
            var mailbox = config.GetString("mailbox");
            var scope = ParseScope(config);
            var deploy = new Deploy(key, config, router, scope, dispatcher, mailbox);
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