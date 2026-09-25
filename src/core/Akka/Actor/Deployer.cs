//-----------------------------------------------------------------------
// <copyright file="Deployer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        /// The routers that Akka.NET's own <c>akka.conf</c> maps under
        /// <c>akka.actor.router.type-mapping</c>, constructed directly so that neither the trimmer nor the
        /// Native AOT compiler has to see through a <see cref="Type.GetType(string)"/> call.
        ///
        /// The keys are the mapped type names, never the aliases: any alias can be pointed at a different
        /// type in user HOCON - <c>round-robin-pool = "MyApp.MyRouter, MyApp"</c> is legal - and that
        /// remapping has to keep working, so the alias itself says nothing about which router to build.
        /// </summary>
        private static readonly Dictionary<string, Func<Config, RouterConfig>> BuiltInRouterConfigs =
            BuildBuiltInRouterConfigs();

        /// <summary>
        /// Builds <see cref="BuiltInRouterConfigs"/>: the 13 routers <c>akka.actor.router.type-mapping</c>
        /// maps onto types inside Akka.dll and that can actually reach this table.
        ///
        /// Two spellings per router, both deliberate: <c>akka.conf</c> ships the bare name and HOCON in the
        /// wild also carries the <c>Ns.T, Akka</c> form. The lookup runs the configured value through
        /// <see cref="Akka.Util.TypeExtensions.StripAssemblyIdentity"/> first, so a full
        /// <see cref="Type.AssemblyQualifiedName"/> - which Akka.Hosting writes into HOCON - matches the
        /// second key whatever version, culture or public key token it names. A value that still misses the
        /// table falls through to the reflection path, which is unavailable (and therefore throws) once
        /// dynamic type loading is switched off. Do not remove a spelling, and do not add a versioned third
        /// key.
        /// </summary>
        private static Dictionary<string, Func<Config, RouterConfig>> BuildBuiltInRouterConfigs()
        {
            var builtIn = new Dictionary<string, Func<Config, RouterConfig>>(StringComparer.Ordinal);

            // NoRouter is deliberately absent: the only alias akka.conf maps onto it is "from-code", which
            // CreateRouterConfig short-circuits before it ever reaches this table, and NoRouter has no public
            // Config constructor - so an entry here would only change what happens when a user points some
            // other alias at it.
            Add<RoundRobinPool>(static deployment => new RoundRobinPool(deployment));
            Add<RoundRobinGroup>(static deployment => new RoundRobinGroup(deployment));
            Add<RandomPool>(static deployment => new RandomPool(deployment));
            Add<RandomGroup>(static deployment => new RandomGroup(deployment));
            Add<SmallestMailboxPool>(static deployment => new SmallestMailboxPool(deployment));
            Add<BroadcastPool>(static deployment => new BroadcastPool(deployment));
            Add<BroadcastGroup>(static deployment => new BroadcastGroup(deployment));
            Add<ScatterGatherFirstCompletedPool>(static deployment => new ScatterGatherFirstCompletedPool(deployment));
            Add<ScatterGatherFirstCompletedGroup>(static deployment => new ScatterGatherFirstCompletedGroup(deployment));
            Add<ConsistentHashingPool>(static deployment => new ConsistentHashingPool(deployment));
            Add<ConsistentHashingGroup>(static deployment => new ConsistentHashingGroup(deployment));
            Add<TailChoppingPool>(static deployment => new TailChoppingPool(deployment));
            Add<TailChoppingGroup>(static deployment => new TailChoppingGroup(deployment));

            return builtIn;

            // typeof(TRouter) is what keeps this trimmer-safe: the trimmer sees the type, keeps it, and hands
            // us its own names, so no spelling can drift out of step with the type it maps to.
            void Add<TRouter>(Func<Config, RouterConfig> factory) where TRouter : RouterConfig
            {
                var routerType = typeof(TRouter);

                // "Akka.Routing.RoundRobinPool"
                builtIn[routerType.FullName] = factory;
                // "Akka.Routing.RoundRobinPool, Akka"
                builtIn[$"{routerType.FullName}, {routerType.Assembly.GetName().Name}"] = factory;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected readonly Config Default;
        private readonly Settings _settings;
        private readonly AtomicReference<WildcardIndex<Deploy>> _deployments = new(new WildcardIndex<Deploy>());

        /// <summary>
        /// Initializes a new instance of the <see cref="Deployer"/> class.
        /// </summary>
        /// <param name="settings">The settings used to configure the deployer.</param>
        public Deployer(Settings settings)
        {
            _settings = settings;
            var config = _settings.Config.GetConfig("akka.actor.deployment");
            Default = config.GetConfig("default");

            var rootObj = config.Root.GetObject();
            if (rootObj == null) return;
            var deploys = rootObj.Items
                .Where(d => !d.Key.Equals("default"))
                .Select(kvp => ParseConfig(kvp.Key, kvp.Value.ToConfig()));
            foreach (var d in deploys)
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
            return _deployments.Value.Find(path);
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
            void Add(IList<string> path, Deploy d)
            {
                var w = _deployments.Value;
                foreach (var t in path)
                {
                    if (string.IsNullOrEmpty(t))
                        throw new IllegalActorNameException($"Actor name in deployment [{d.Path}] must not be empty");
                    if (!ActorPath.IsValidPathElement(t))
                    {
                        throw new IllegalActorNameException(
                            $"Illegal actor name [{t}] in deployment [${d.Path}]. {ActorPath.ValidActorNameDescription}");
                    }
                }
                if (!_deployments.CompareAndSet(w, w.Insert(path, d))) Add(path, d);
            }

            var elements = deploy.Path.Split('/').Drop(1).ToList();
            Add(elements, deploy);
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
            var routerType = deployment.GetString("router", "from-code");
            // var router = CreateRouterConfig(routerType, key, config, deployment);
            var router = CreateRouterConfig(routerType, deployment);
            var dispatcher = deployment.GetString("dispatcher", "");
            var mailbox = deployment.GetString("mailbox", "");
            var stashCapacity = deployment.GetInt("stash-capacity", Deploy.NoStashSize);
            var deploy = new Deploy(key, deployment, router, Deploy.NoScopeGiven, dispatcher, mailbox, stashCapacity);
            return deploy;
        }

        private RouterConfig CreateRouterConfig(string routerTypeAlias, Config deployment)
        {
            if (routerTypeAlias == "from-code")
                return NoRouter.Instance;

            if (deployment.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<RouterConfig>();

            var path = string.Format("akka.actor.router.type-mapping.{0}", routerTypeAlias);
            var routerTypeName = _settings.Config.GetString(path, null);

            if(routerTypeName == null)
            {
                var message = $"Could not find type mapping for router alias [{routerTypeAlias}].";
                if (routerTypeAlias is
                    "cluster-metrics-adaptive-group" or
                    "cluster-metrics-adaptive-pool")
                    message += " Please install Akka.Cluster.Metrics extension nuget package.";
                else
                    message += " Did you forgot to install a specific router extension?";

                throw new ConfigurationException(message);
            }

            if (BuiltInRouterConfigs.TryGetValue(
                    Akka.Util.TypeExtensions.StripAssemblyIdentity(routerTypeName), out var routerFactory))
                return routerFactory(deployment);

            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw new ConfigurationException(AkkaFeatures.NotBuiltIn(
                    $"akka.actor.router.type-mapping.{routerTypeAlias}", routerTypeName, "one of the built-in routers"));

            return CreateRouterConfigFromTypeName(routerTypeName, routerTypeAlias, deployment);
        }

        [RequiresUnreferencedCode("Loads the router type mapped under [akka.actor.router.type-mapping] by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static RouterConfig CreateRouterConfigFromTypeName(string routerTypeName, string routerTypeAlias, Config deployment)
        {
            Type routerType;
            try
            {
                routerType = Type.GetType(routerTypeName);
            }
            catch (ArgumentNullException e)
            {
                var message = $"Could not find extension Type [{routerTypeAlias}] for router alias [{routerTypeAlias}].";
                if (routerTypeAlias is "cluster-metrics-adaptive-group" or "cluster-metrics-adaptive-pool")
                    message += " Please install Akka.Cluster.Metrics extension nuget package.";
                else
                    message += " Did you forgot to install a specific router extension?";

                throw new ConfigurationException(message, e);
            }

            Debug.Assert(routerType != null, "routerType != null");
            return (RouterConfig)Activator.CreateInstance(routerType, deployment);
        }
    }
}
