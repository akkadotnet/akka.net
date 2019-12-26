// //-----------------------------------------------------------------------
// // <copyright file="ClusterMetricsMessageSerializer.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Cluster.Metrics.Serialization
{
    /// <summary>
    /// Protobuf serializer for <see cref="IClusterMetricMessage"/> types.
    /// </summary>
    public class ClusterMetricsMessageSerializer : SerializerWithStringManifest
    {
        private const int BufferSize = 4 * 1024;
        
        #region manifests
        
        private const string MetricsGossipEnvelopeManifest = "a";
        private const string AdaptiveLoadBalancingPoolManifest = "b";
        private const string MixMetricsSelectorManifest = "c";
        private const string CpuMetricsSelectorManifest = "d";
        private const string HeapMetricsSelectorManifest = "e";
        private const string SystemLoadAverageMetricsSelectorManifest = "f";
        
        #endregion
        
        private readonly ExtendedActorSystem _system;

        /// <inheritdoc />
        public ClusterMetricsMessageSerializer(ExtendedActorSystem system) 
            : base(system)
        {
            _system = system;
        }
        
        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case MetricsGossipEnvelope m: return Compress(MetricsGossipEnvelopeToProto(m));
                case AdaptiveLoadBalancingPool alb: return AdaptiveLoadBalancingPoolToBinary(alb);
                case MixMetricsSelector mms: return MixMetricSelectorToBinary(mms);
                /*case CpuMetricsSelector               => Array.emptyByteArray
                case HeapMetricsSelector              => Array.emptyByteArray
                case SystemLoadAverageMetricsSelector => Array.emptyByteArray*/
                default:
                    throw new ArgumentException($"Can't serialize object of type ${obj.GetType().Name} in [${GetType().Name}]");
            }
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case MetricsGossipEnvelopeManifest: return MetricsGossipEnvelopeFromBinary(bytes);
                case AdaptiveLoadBalancingPoolManifest: return AdaptiveLoadBalancingPoolFromBinary(bytes);
                case MixMetricsSelectorManifest: return MixMetricSelectorFromBinary(bytes) ;
                /*case CpuMetricsSelectorManifest               => CpuMetricsSelector
                case HeapMetricsSelectorManifest              => HeapMetricsSelector
                case SystemLoadAverageMetricsSelectorManifest => SystemLoadAverageMetricsSelector*/
                default:
                    throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in [${GetType().Name}");
            }
        }

        /// <inheritdoc />
        public override string Manifest(object o)
        {
            switch (o)
            {
                case MetricsGossipEnvelope _: return MetricsGossipEnvelopeManifest;
                case AdaptiveLoadBalancingPool _: return AdaptiveLoadBalancingPoolManifest;
                case MixMetricsSelector _: return MixMetricsSelectorManifest;
                /*case CpuMetricsSelector               => CpuMetricsSelectorManifest
                case HeapMetricsSelector              => HeapMetricsSelectorManifest
                case SystemLoadAverageMetricsSelector => SystemLoadAverageMetricsSelectorManifest*/
                default:
                    throw new ArgumentException($"Can't serialize object of type {o.GetType().Name} in [${GetType().Name}]");
            }
        }

        private MixMetricsSelector MixMetricSelectorFromBinary(byte[] bytes)
        {
            var mm = MixMetricsSelector.Parser.ParseFrom(bytes);
        }
    }
}