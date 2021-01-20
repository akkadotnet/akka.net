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
    public class ConfigServicesParser
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
            _resolvedServices = ConfigServicesParser.Parse(
                system.Settings.Config.GetConfig(system.Settings.Config.GetString("akka.discovery.config.services-path")));

            var log = Logging.GetLogger(system, nameof(ConfigServiceDiscovery));
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
