//-----------------------------------------------------------------------
// <copyright file="AggregateServiceDiscovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Discovery.Aggregate
{
    [InternalApi]
    public sealed class AggregateServiceDiscoverySettings
    {
        public List<string> DiscoveryMethods { get; }

        public AggregateServiceDiscoverySettings(Configuration.Config config)
        {
            DiscoveryMethods = config.GetStringList("discovery-methods").ToList();
        }
    }

    [InternalApi]
    public class AggregateServiceDiscovery : ServiceDiscovery
    {
        private readonly ImmutableList<(string, ServiceDiscovery)> _methods;
        private readonly ILoggingAdapter _log;

        public AggregateServiceDiscovery(ExtendedActorSystem system)
        {
            _log = Logging.GetLogger(system, nameof(AggregateServiceDiscovery));
            var config = system.Settings.Config.GetConfig("akka.discovery.aggregate") ??
                throw new ArgumentException(
                    "Could not load aggregate discovery config from path [akka.discovery.aggregate]");
            
            if (!config.HasPath("discovery-methods"))
            {
                config = ConfigurationFactory.ParseString("akka.discovery.aggregate.discovery-methods=[]")
                    .WithFallback(config);
            }
            
            var settings = new AggregateServiceDiscoverySettings(config);
            if (!settings.DiscoveryMethods.Any())
            {
                _log.Warning(
                    "The config path [akka.discovery.aggregate.discovery-methods] is either missing or empty.\n" +
                    "Please make sure that this path exists and at least one discovery method is specified.");
            }

            var serviceDiscovery = Discovery.Get(system);
            _methods = settings.DiscoveryMethods.Select(method => (method, serviceDiscovery.LoadServiceDiscovery(method))).ToImmutableList();
        }

        /// <summary>
        /// Each discovery method is given the resolveTimeout rather than reducing it each time between methods.
        /// </summary>
        public override async Task<Resolved> Lookup(Lookup query, TimeSpan resolveTimeout)
        {
            if (_methods.Count == 0)
            {
                _log.Error("Aggregate service discovery failed. Service discovery list is empty.");
                return new Resolved(query.ServiceName);
            }

            var lastIndex = _methods.Count - 1;
            for (var i = 0; i < _methods.Count; ++i)
            {
                var (method, next) = _methods[i];
                _log.Debug("Looking up [{0}] with [{1}]", query, method);
                try
                {
                    var resolved = await next.Lookup(query, resolveTimeout);
                    if (!resolved.Addresses.IsEmpty)
                        return resolved;
                        
                    _log.Debug("Method[{0}] returned no ResolvedTargets{1}", query, i == lastIndex ? "" : ", trying next");
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Method[{0}] Service discovery failed.{1}", method, i == lastIndex ? "" : " Trying next discovery method");
                }
            }
            
            _log.Warning("Aggregate service discovery failed. None of the listed service discovery found any services.");
            return new Resolved(query.ServiceName);
        }
    }
}
