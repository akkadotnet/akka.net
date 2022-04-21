//-----------------------------------------------------------------------
// <copyright file="ConfigServiceDiscovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;

namespace Akka.Discovery.Config
{
    [InternalApi]
    public static class ConfigServicesParser
    {
        public static Dictionary<string, ServiceDiscovery.Resolved> Parse(Configuration.Config config)
        {
            return config.AsEnumerable()
                .Select(pair => (pair.Key, config.GetConfig(pair.Key)))
                .ToDictionary(pair => pair.Key, pair =>
                {
                    var (serviceName, full) = pair;
                    var endpoints = full.GetStringList("endpoints");
                    var resolvedTargets = endpoints.Select(e =>
                    {
                        var values = e.Split(':');
                        return new ServiceDiscovery.ResolvedTarget(values[0], int.TryParse(values.Skip(1).FirstOrDefault(), out var i) ? i : default(int?));
                    }).ToArray();
                    return new ServiceDiscovery.Resolved(serviceName, resolvedTargets);
                });
        }
    }

    [InternalApi]
    public class ConfigServiceDiscovery : ServiceDiscovery
    {
        private readonly Dictionary<string, Resolved> _resolvedServices;

        public ConfigServiceDiscovery(ExtendedActorSystem system)
        {
            var log = Logging.GetLogger(system, nameof(ConfigServiceDiscovery));
            
            var config = system.Settings.Config.GetConfig("akka.discovery.config") ??
                throw new ArgumentException(
                    "Could not load config based discovery config from path [akka.discovery.config]");
            
            var servicePath = config.GetString("services-path");
            if (string.IsNullOrWhiteSpace(servicePath))
            {
                log.Warning(
                    "The config path [akka.discovery.config] must contain field `service-path` that points to a " +
                    "configuration path that contains an array of node services for Discovery to contact.");
                _resolvedServices = new Dictionary<string, Resolved>();
            }
            else
            {
                var services = system.Settings.Config.GetConfig(servicePath);
                if (services == null)
                {
                    log.Warning(
                        "You are trying to use config based discovery service and the settings path described in\n" +
                        $"`akka.discovery.config.services-path` does not exists. Make sure that [{servicePath}] path \n" +
                        "exists and to fill this setting with pre-defined node addresses to make sure that a cluster \n" +
                        "can be formed");
                    _resolvedServices = new Dictionary<string, Resolved>();
                }
                else
                {
                    _resolvedServices = ConfigServicesParser.Parse(services);
                    if(_resolvedServices.Count == 0)
                        log.Warning(
                            $"You are trying to use config based discovery service and the settings path [{servicePath}]\n" +
                            "described `akka.discovery.config.services-path` is empty. Make sure to fill this setting \n" +
                            "with pre-defined node addresses to make sure that a cluster can be formed.");
                }
            }

            log.Debug($"Config discovery serving: {string.Join(", ", _resolvedServices.Values)}");
        }

        public override Task<Resolved> Lookup(Lookup lookup, TimeSpan resolveTimeout)
        {
            return Task.FromResult(!_resolvedServices.TryGetValue(lookup.ServiceName, out var resolved)
                ? new Resolved(lookup.ServiceName, null)
                : resolved);
        }
    }
}
