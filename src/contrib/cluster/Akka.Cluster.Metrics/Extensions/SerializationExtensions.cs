// //-----------------------------------------------------------------------
// // <copyright file="SerializationExtensions.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Cluster.Metrics.Serialization;
using Akka.Dispatch;
using Akka.Util.Internal;
using Address = Akka.Actor.Address;

namespace Akka.Cluster.Metrics.Extensions
{
    public static class SerializationExtensions
    {
        
        
        /// <summary>
        /// Converts Akka.NET type into Protobuf serializable message
        /// </summary>
        public static Serialization.Address ToProto(this Address address)
        {
            return new Serialization.Address()
            {
                Hostname = address.Host,
                Protocol = address.Protocol,
                Port = (uint)(address.Port ?? 0),
                System = address.System,
            };
        }
        
        /// <summary>
        /// Converts Protobuf serializable message to Akka.NET type
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static Akka.Actor.Address FromProto(this Serialization.Address address)
        {
            return new Address(address.Protocol, address.System, address.Hostname, (int)address.Port);
        }

        public static Serialization.AdaptiveLoadBalancingPool ToProto(this AdaptiveLoadBalancingPool pool, Func<IMetricsSelector, string> manifests)
        {
            return new Serialization.AdaptiveLoadBalancingPool()
            {
                MetricsSelector = !pool.MetricsSelector.Equals(MixMetricsSelector.Instance) ? pool.MetricsSelector.ToProto(manifests(pool.MetricsSelector)) : null,
                RouterDispatcher = pool.RouterDispatcher != Dispatchers.DefaultDispatcherId ? pool.RouterDispatcher : null,
                NrOfInstances = (uint)pool.NrOfInstances,
                UsePoolDispatcher = pool.UsePoolDispatcher
            };
        }

        public static AdaptiveLoadBalancingPool FromProto(this Serialization.AdaptiveLoadBalancingPool pool)
        {
            return new AdaptiveLoadBalancingPool(
                pool.MetricsSelector.FromProto(), 
                (int)pool.NrOfInstances, 
                routerDispatcher: pool.RouterDispatcher, 
                usePoolDispatcher: pool.UsePoolDispatcher);
        }

        public static Serialization.MixMetricsSelector ToProto(this MixMetricsSelector selector, Func<IMetricsSelector, string> manifests)
        {
            return new Serialization.MixMetricsSelector()
            {
                Selectors = { selector.Selectors.Select(s => s.ToProto(manifests(s)) )}
            };
        }
        
        public static MixMetricsSelector FromProto(this Serialization.MixMetricsSelector selector)
        {
            // Assuming that MixMetticsSelector is always serialized with valid selector types (subclasses of CapacityMetricsSelector)
            return new MixMetricsSelector(selector.Selectors.Select(s => s.FromProto() as CapacityMetricsSelector).ToImmutableArray());
        }

        public static Serialization.MetricsSelector ToProto(this IMetricsSelector metricsSelector, string manifest)
        {
            var serializer = MetricsSelectorSerializersRegistry.GetByType(metricsSelector.GetType());
            if (!serializer.HasValue)
                throw new SerializationException($"Serializer not found for metric selector type {metricsSelector.GetType()}");
            
            return new MetricsSelector()
            {
                Manifest = manifest,
                SerializerId = serializer.Value.Id,
                Data = serializer.Value.Serialize(metricsSelector)
            };
        }
        
        public static IMetricsSelector FromProto(this Serialization.MetricsSelector metricsSelector)
        {
            var serializer = MetricsSelectorSerializersRegistry.GetById(metricsSelector.SerializerId);
            if (!serializer.HasValue)
                throw new SerializationException($"Serializer not found by serializerId {metricsSelector.SerializerId}");

            return serializer.Value.Deserialize(metricsSelector.Data.ToByteArray());
        }

        public static MetricsGossipEnvelope ToProto(this MetricsGossipEnvelope gossip)
        {
            gossip.Gossip.NodeMetrics.ForEach(m => m.AddressIndex = gossip.Gossip.AllAddresses.IndexOf(m.Address));
            return gossip;
        }

        public static MetricsGossipEnvelope FromProto(this MetricsGossipEnvelope gossip)
        {
            gossip.Gossip.NodeMetrics.ForEach(m => m.Address = gossip.Gossip.AllAddresses[m.AddressIndex]);
            return gossip;
        }
        
        // TODO: Define conversion for Number (store bites and type)
        // TODO: Verify all conversions
    }
}