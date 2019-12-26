// //-----------------------------------------------------------------------
// // <copyright file="ClusterMetricsMessageSerializer.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Metrics.Extensions;
using Akka.Serialization;
using Google.Protobuf;

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
        
        /// <inheritdoc />
        public ClusterMetricsMessageSerializer(ExtendedActorSystem system) 
            : base(system)
        {
        }
        
        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case MetricsGossipEnvelope m: return m.ToProto().ToByteArray();
                case Metrics.AdaptiveLoadBalancingPool alb: return alb.ToProto(Manifest).ToByteArray();
                case Metrics.MixMetricsSelector mms: return mms.ToProto(Manifest).ToByteArray();
                case CpuMetricsSelector _: return new byte[0];
                case HeapMetricsSelector _: return new byte[0];
                case SystemLoadAverageMetricsSelector _: return new byte[0];
                default:
                    throw new ArgumentException($"Can't serialize object of type ${obj.GetType().Name} in [${GetType().Name}]");
            }
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case MetricsGossipEnvelopeManifest: return MetricsGossipEnvelope.Parser.ParseFrom(bytes).FromProto(); 
                case AdaptiveLoadBalancingPoolManifest: return AdaptiveLoadBalancingPool.Parser.ParseFrom(bytes).FromProto();
                case MixMetricsSelectorManifest: return Serialization.MixMetricsSelector.Parser.ParseFrom(bytes).FromProto();
                case CpuMetricsSelectorManifest: return CpuMetricsSelector.Instance;
                case HeapMetricsSelectorManifest: return HeapMetricsSelector.Instance;
                case SystemLoadAverageMetricsSelectorManifest: return SystemLoadAverageMetricsSelector.Instance;
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
                case CpuMetricsSelector _: return CpuMetricsSelectorManifest;
                case HeapMetricsSelector _: return HeapMetricsSelectorManifest;
                case SystemLoadAverageMetricsSelector _: return SystemLoadAverageMetricsSelectorManifest;
                default:
                    throw new ArgumentException($"Can't serialize object of type {o.GetType().Name} in [${GetType().Name}]");
            }
        }
    }
}