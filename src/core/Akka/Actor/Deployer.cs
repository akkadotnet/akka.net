//-----------------------------------------------------------------------
// <copyright file="Deployer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Configuration;
using Akka.Routing;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    /// Used to configure and deploy actors.
    /// </summary>
    public class Deployer
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly Config Default;
        private readonly Settings _settings;
        private readonly AtomicReference<WildcardTree<Deploy>> _deployments = new AtomicReference<WildcardTree<Deploy>>(new WildcardTree<Deploy>());

        /// <summary>
        /// Initializes a new instance of the <see cref="Deployer"/> class.
        /// </summary>
        /// <param name="settings">The settings used to configure the deployer.</param>
        public Deployer(Settings settings)
        {
            _settings = settings;
            var config = settings.Config.GetConfig("akka.actor.deployment");
            Default = config.GetConfig("default");

            var rootObj = config.Root.GetObject();
            if (rootObj == null) return;
            var unwrapped = rootObj.Unwrapped.Where(d => !d.Key.Equals("default")).ToArray();
            foreach (var d in unwrapped.Select(x => ParseConfig(x.Key, config.GetConfig(x.Key.BetweenDoubleQuotes()))))
            {
                SetDeploy(d);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public Deploy Lookup(ActorPath path)
        {
            var rawElements = path.Elements;
            if (rawElements[0] != "user" || rawElements.Count < 2)
            {
                return Deploy.None;
            }

            var elements = rawElements.Drop(1);
            return Lookup(elements);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public Deploy Lookup(IEnumerable<string> path)
        {
            return Lookup(path.GetEnumerator());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public Deploy Lookup(IEnumerator<string> path)
        {
            return _deployments.Value.Find(path).Data;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="deploy">TBD</param>
        /// <exception cref="IllegalActorNameException">
        /// This exception is thrown if the actor name in the deployment path is empty or contains invalid ASCII.
        /// Valid ASCII includes letters and anything from <see cref="ActorPath.ValidSymbols"/>. Note that paths
        /// cannot start with the <c>$</c>.
        /// </exception>
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
                            throw new IllegalActorNameException($"Actor name in deployment [{d.Path}] must not be empty");
                        if (!ActorPath.IsValidPathElement(t))
                        {
                            throw new IllegalActorNameException(
                                $"Illegal actor name [{t}] in deployment [${d.Path}]. Actor paths MUST: not start with `$`, include only ASCII letters and can only contain these special characters: ${new string(ActorPath.ValidSymbols)}.");
                        }
                    }
                    set = _deployments.CompareAndSet(w, w.Insert(path.GetEnumerator(), d));
                } while (!set);
            };
            var elements = deploy.Path.Split('/').Drop(1).ToList();
            add(elements, deploy);
        }

        /// <summary>
        /// Creates an actor deployment to the supplied path, <paramref name="key"/>, using the supplied configuration, <paramref name="config"/>.
        /// </summary>
        /// <param name="key">The path used to deploy the actor.</param>
        /// <param name="config">The configuration used to configure the deployed actor.</param>
        /// <returns>A configured actor deployment to the given path.</returns>
        public virtual Deploy ParseConfig(string key, Config config)
        {
            var deployment = config.WithFallback(Default);
            var routerType = deployment.GetString("router");
            var router = CreateRouterConfig(routerType, deployment);
            var dispatcher = deployment.GetString("dispatcher");
            var mailbox = deployment.GetString("mailbox");
            var deploy = new Deploy(key, deployment, router, Deploy.NoScopeGiven, dispatcher, mailbox);
            return deploy;
        }

        private RouterConfig CreateRouterConfig(string routerTypeAlias, Config deployment)
        {
            if (routerTypeAlias == "from-code")
                return NoRouter.Instance;

            var path = string.Format("akka.actor.router.type-mapping.{0}", routerTypeAlias);
            var routerTypeName = _settings.Config.GetString(path);
            var routerType = Type.GetType(routerTypeName);
            Debug.Assert(routerType != null, "routerType != null");
            var routerConfig = (RouterConfig)Activator.CreateInstance(routerType, deployment);

            return routerConfig;
        }
    }
}
