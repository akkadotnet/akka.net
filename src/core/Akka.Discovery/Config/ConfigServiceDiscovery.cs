//-----------------------------------------------------------------------
// <copyright file="ConfigServiceDiscovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        private const string DefaultPath = "config";
        private const string DefaultConfigPath = "akka.discovery." + DefaultPath;
        
        private readonly ILoggingAdapter _log;
        private ImmutableDictionary<string, Resolved> _resolvedServices;

        // Backward compatibility constructor
        public ConfigServiceDiscovery(ExtendedActorSystem system)
            : this(system, system.Settings.Config.GetConfig(DefaultConfigPath))
        {
        }
        
        public ConfigServiceDiscovery(ExtendedActorSystem system, Configuration.Config config)
        {
            if(config is null)
                throw new ArgumentException("Config based discovery HOCON config is null");
                
            _log = Logging.GetLogger(system, nameof(ConfigServiceDiscovery));
            
            var servicePath = config.GetString("services-path");
            if (string.IsNullOrWhiteSpace(servicePath))
            {
                _log.Warning(
                    "The config path [akka.discovery.config] must contain field `service-path` that points to a " +
                    "configuration path that contains an array of node services for Discovery to contact.");
                _resolvedServices = ImmutableDictionary<string, Resolved>.Empty;
            }
            else
            {
                var services = system.Settings.Config.GetConfig(servicePath);
                if (services == null)
                {
                    _log.Warning(
                        "You are trying to use config based discovery service and the settings path described in\n" +
                        $"`akka.discovery.config.services-path` does not exists. Make sure that [{servicePath}] path \n" +
                        "exists and to fill this setting with pre-defined node addresses to make sure that a cluster \n" +
                        "can be formed");
                    _resolvedServices = ImmutableDictionary<string, Resolved>.Empty;
                }
                else
                {
                    _resolvedServices = ConfigServicesParser.Parse(services).ToImmutableDictionary();
                    if(_resolvedServices.Count == 0)
                        _log.Warning(
                            $"You are trying to use config based discovery service and the settings path [{servicePath}]\n" +
                            "described `akka.discovery.config.services-path` is empty. Make sure to fill this setting \n" +
                            "with pre-defined node addresses to make sure that a cluster can be formed.");
                }
            }

            _log.Debug($"Config discovery serving: {string.Join(", ", _resolvedServices.Values)}");
        }

        [InternalStableApi]
        public bool TryRemoveEndpoint(string serviceName, ResolvedTarget target)
        {
            if (!_resolvedServices.TryGetValue(serviceName, out var resolved))
            {
                _log.Info($"Could not find service {serviceName}, adding a new service. Available services: {string.Join(", ", _resolvedServices.Keys)}");
                resolved = new Resolved(serviceName);
                _resolvedServices = _resolvedServices.SetItem(serviceName, resolved);
            }

            if (!resolved.Addresses.Contains(target))
            {
                _log.Info($"ResolvedTarget was not in service {serviceName}, nothing to remove.");
                return false;
            }

            var newResolved = new Resolved(serviceName, resolved.Addresses.Remove(target));
            _resolvedServices = _resolvedServices.SetItem(serviceName, newResolved);
            
            _log.Debug($"ResolvedTarget {target} has been removed from service {serviceName}");
            return true;
        }

        [InternalStableApi]
        public bool TryAddEndpoint(string serviceName, ResolvedTarget target)
        {
            if (!_resolvedServices.TryGetValue(serviceName, out var resolved))
            {
                _log.Info($"Could not find service {serviceName}, adding a new service. Available services: {string.Join(", ", _resolvedServices.Keys)}");
                resolved = new Resolved(serviceName);
                _resolvedServices = _resolvedServices.SetItem(serviceName, resolved);
            }

            if (resolved.Addresses.Contains(target))
            {
                _log.Info($"ResolvedTarget is already in service {serviceName}, nothing to add.");
                return false;
            }
            
            var newResolved = new Resolved(serviceName, resolved.Addresses.Add(target));
            _resolvedServices = _resolvedServices.SetItem(serviceName, newResolved);
            
            _log.Debug($"ResolvedTarget {target} has been added to service {serviceName}");
            return true;
        }

        public override Task<Resolved> Lookup(Lookup lookup, TimeSpan resolveTimeout)
        {
            return Task.FromResult(!_resolvedServices.TryGetValue(lookup.ServiceName, out var resolved)
                ? new Resolved(lookup.ServiceName, null)
                : resolved);
        }
    }
}
