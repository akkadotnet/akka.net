//-----------------------------------------------------------------------
// <copyright file="AggregateServiceDiscovery.cs" company="Akka.NET Project">
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
using Akka.Util.Internal;

namespace Akka.Discovery.Aggregate
{
    [InternalApi]
    public sealed class AggregateServiceDiscoverySettings
    {
        public List<string> DiscoveryMethods { get; }

        public AggregateServiceDiscoverySettings(Configuration.Config config)
        {
            var discoveryMethods = config.GetStringList("discovery-methods");
            if (!discoveryMethods.Any())
            {
                throw new ArgumentOutOfRangeException(nameof(discoveryMethods), "At least one discovery method should be specified");
            }

            DiscoveryMethods = discoveryMethods.ToList();
        }
    }

    [InternalApi]
    public class AggregateServiceDiscovery : ServiceDiscovery
    {
        private readonly List<(string, ServiceDiscovery)> _methods;
        private readonly ILoggingAdapter log;

        public AggregateServiceDiscovery(ExtendedActorSystem system)
        {
            var settings = new AggregateServiceDiscoverySettings(system.Settings.Config.GetConfig("akka.discovery.aggregate"));
            var serviceDiscovery = Discovery.Get(system);
            _methods = settings.DiscoveryMethods.Select(method => (method, serviceDiscovery.LoadServiceDiscovery(method))).ToList();

            log = Logging.GetLogger(system, nameof(AggregateServiceDiscovery));
        }

        /// <summary>
        /// Each discovery method is given the resolveTimeout rather than reducing it each time between methods.
        /// </summary>
        public override Task<Resolved> Lookup(Lookup query, TimeSpan resolveTimeout) =>
            Resolve(_methods, query, resolveTimeout);

        private async Task<Resolved> Resolve(IReadOnlyCollection<(string, ServiceDiscovery)> sds, Lookup query, TimeSpan resolveTimeout)
        {
            var tail = sds.Skip(1).ToList();

            switch (sds.Head())
            {
                case (string method, ServiceDiscovery next) when tail.Any():
                {
                    log.Debug("Looking up [{0}] with [{1}]", query, method);
                    try
                    {
                        var resolved = await next.Lookup(query, resolveTimeout);
                        if (!resolved.Addresses.IsEmpty)
                            return resolved;
                        
                        log.Debug("Method[{0}] returned no ResolvedTargets, trying next", query);
                        return await Resolve(tail, query, resolveTimeout);
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, "[{0}] Service discovery failed. Trying next discovery method", method);
                        return await Resolve(tail, query, resolveTimeout);
                    } 
                }
                case (string method, ServiceDiscovery next):
                {
                    log.Debug("Looking up [{0}] with [{1}]", query, method);
                    return await next.Lookup(query, resolveTimeout);
                }
                default:
                    // This is checked in `AggregateServiceDiscoverySettings`, but silence default switch warning
                    throw new InvalidOperationException("At least one discovery method should be specified");
            }
        }
    }
}
