//-----------------------------------------------------------------------
// <copyright file="Discovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;

namespace Akka.Discovery
{
    public class Discovery : IExtension
    {
        private readonly ExtendedActorSystem _system;
        private readonly Lazy<ServiceDiscovery> _defaultImpl;
        private readonly ConcurrentDictionary<string, Lazy<ServiceDiscovery>> _implementations =
            new ConcurrentDictionary<string, Lazy<ServiceDiscovery>>();
        private readonly ILoggingAdapter _log;

        public Discovery(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(DiscoveryProvider.DefaultConfiguration());

            _log = Logging.GetLogger(_system, GetType());
            var defaultImplMethod = new Lazy<string>(() =>
            {
                var method = system.Settings.Config.GetString("akka.discovery.method");
                if (string.IsNullOrWhiteSpace(method) || method == "<method>")
                {
                    _log.Warning(
                        "No default service discovery implementation configured in `akka.discovery.method`.\n" +
                        "Make sure to configure this setting to your preferred implementation such as 'config'\n" +
                        "in your application.conf (from the akka-discovery module). Falling back to default config\n" +
                        "based discovery method");
                    method = "config";
                }
                return method;
            });

            _defaultImpl = new Lazy<ServiceDiscovery>(() => LoadServiceDiscovery(defaultImplMethod.Value));
        }

        /// <summary>
        /// Default <see cref="ServiceDiscovery"/> as configured in `akka.discovery.method`.
        /// </summary>
        public ServiceDiscovery Default => _defaultImpl.Value;

        /// <summary>
        /// Create a <see cref="ServiceDiscovery"/> from configuration property.
        /// </summary>
        /// <param name="method">Used to find configuration property "akka.discovery.[method].class".</param>
        /// <returns>
        /// The `ServiceDiscovery` instance for a given `method` will be created once,
        /// and subsequent requests for the same `method` will return the same instance.
        /// </returns>
        public ServiceDiscovery LoadServiceDiscovery(string method) =>
            _implementations.GetOrAdd(method, new Lazy<ServiceDiscovery>(() => CreateServiceDiscovery(method))).Value;

        [InternalApi]
        private ServiceDiscovery CreateServiceDiscovery(string method)
        {
            var config = _system.Settings.Config.GetConfig($"akka.discovery.{method}");
            if (config is null)
                throw new ArgumentException($"Could not load discovery config from path [akka.discovery.{method}]");
            if(!config.HasPath("class"))
                throw new ArgumentException($"akka.discovery.{method} must contain field `class` that is a FQN of an `Akka.Discovery.ServiceDiscovery` implementation");

            Try<ServiceDiscovery> Create(string typeName)
            {
                var dynamic = DynamicAccess.CreateInstanceFor<ServiceDiscovery>(typeName, _system);
                return dynamic.RecoverWith(ex => ex is TypeLoadException || ex is MissingMethodException 
                    ? DynamicAccess.CreateInstanceFor<ServiceDiscovery>(typeName) 
                    : dynamic);
            }

            var className = config.GetString("class");
            _log.Info($"Starting Discovery service using [{method}] method, class: [{className}]");
            var instanceTry = Create(className);

            return instanceTry.IsSuccess switch
            {
                true => instanceTry.Get(),
                false when instanceTry.Failure.Value is TypeLoadException || instanceTry.Failure.Value is MissingMethodException =>
                    throw new ArgumentException(nameof(method), $"Illegal akka.discovery.{method}.class value or incompatible class! \n" +
                        "The implementation class MUST extend Akka.Discovery.ServiceDiscovery and take an \n" +
                        "ExtendedActorSystem as constructor argument."),
                _ => throw instanceTry.Failure.Value
            };
        }

        public static Discovery Get(ActorSystem system) => system.WithExtension<Discovery, DiscoveryProvider>();
    }

    public class DiscoveryProvider : ExtensionIdProvider<Discovery>
    {
        public override Discovery CreateExtension(ExtendedActorSystem system) => new Discovery(system);

        /// <summary>
        /// Returns a default configuration for the Akka Discovery module.
        /// </summary>
        public static Configuration.Config DefaultConfiguration() => ConfigurationFactory.FromResource<Discovery>("Akka.Discovery.Resources.reference.conf");
    }
}
