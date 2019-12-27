// //-----------------------------------------------------------------------
// // <copyright file="ClusterMetricsMessageSerializer.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

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

        private Akka.Serialization.Serialization _serialization; 
        
        #endregion
        
        /// <inheritdoc />
        public ClusterMetricsMessageSerializer(ExtendedActorSystem system) 
            : base(system)
        {
            _serialization = new Akka.Serialization.Serialization(system);
        }
        
        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case MetricsGossipEnvelope m: return Compress(MetricsGossipEnvelopeToProto(m)); // TODO: Add compression here
                case Metrics.AdaptiveLoadBalancingPool alb: return AdaptiveLoadBalancingPoolToBinary(alb);
                case Metrics.MixMetricsSelector mms: return MixMetricsSelectorToBinary(mms);
                case CpuMetricsSelector _: return new byte[0];
                case HeapMetricsSelector _: return new byte[0];
                case SystemLoadAverageMetricsSelector _: return new byte[0];
                default:
                    throw new ArgumentException($"Can't serialize object of type ${obj.GetType().Name} in [${GetType().Name}]");
            }
        }

        private byte[] Compress(IMessage msg)
        {
            // TODO: Port this part for MetricsGossipEnvelope bytes compression and decompression
            // Probably should use this: https://docs.microsoft.com/ru-ru/dotnet/api/system.io.compression.gzipstream?view=netframework-4.8
            /*def compress(msg: MessageLite): Array[Byte] = {
                val bos = new ByteArrayOutputStream(BufferSize)
                val zip = new GZIPOutputStream(bos)
                try msg.writeTo(zip)
                    finally zip.close()
                bos.toByteArray
            }*/
            return msg.ToByteArray();
        }

        private byte[] Decompress(byte[] bytes)
        {
            // TODO: Port this part for MetricsGossipEnvelope bytes compression and decompression
            // Probably should use this: https://docs.microsoft.com/ru-ru/dotnet/api/system.io.compression.gzipstream?view=netframework-4.8
            /*def decompress(bytes: Array[Byte]): Array[Byte] = {
                val in = new GZIPInputStream(new ByteArrayInputStream(bytes))
                val out = new ByteArrayOutputStream()
                val buffer = new Array[Byte](BufferSize)

                @tailrec def readChunk(): Unit = in.read(buffer) match {
                    case -1 => ()
                    case n =>
                        out.write(buffer, 0, n)
                    readChunk()
                }

                try readChunk()
                    finally in.close()
                    out.toByteArray
            }*/
            return bytes;
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case MetricsGossipEnvelopeManifest: return MetricsGossipEnvelopeFromBinary(bytes); 
                case AdaptiveLoadBalancingPoolManifest: return AdaptiveLoadBalancingPoolFromBinary(bytes);
                case MixMetricsSelectorManifest: return MixMetricSelectorFromBinary(bytes);
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

        private byte[] AdaptiveLoadBalancingPoolToBinary(Metrics.AdaptiveLoadBalancingPool pool)
        {
            var proto = new AdaptiveLoadBalancingPool()
            {
                NrOfInstances = (uint)pool.NrOfInstances,
                UsePoolDispatcher = pool.UsePoolDispatcher
            };
            
            if (!pool.MetricsSelector.Equals(Metrics.MixMetricsSelector.Instance))
                proto.MetricsSelector = MetricsSelectorToProto(pool.MetricsSelector);

            if (pool.RouterDispatcher != Dispatchers.DefaultDispatcherId)
                proto.RouterDispatcher = pool.RouterDispatcher;

            return proto.ToByteArray();
        }

        private MetricsSelector MetricsSelectorToProto(IMetricsSelector selector)
        {
            var serializer = _serialization.FindSerializerFor(selector);
            
            return new MetricsSelector()
            {
                Data = ByteString.CopyFrom(serializer.ToBinary(selector)),
                SerializerId = (uint)serializer.Identifier,
                Manifest = selector.GetType().TypeQualifiedName()
            };
        }

        private byte[] MixMetricsSelectorToBinary(Metrics.MixMetricsSelector selector)
        {
            var proto = new MixMetricsSelector()
            {
                Selectors = { selector.Selectors.Select(MetricsSelectorToProto) }
            };
            return proto.ToByteArray();
        }
        
        /// <summary>
        /// Converts Akka.NET type into Protobuf serializable message
        /// </summary>
        public Serialization.Address AddressToProto(Actor.Address address)
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
        public Akka.Actor.Address AddressFromProto(Serialization.Address address)
        {
            return new Akka.Actor.Address(address.Protocol, address.System, address.Hostname, (int)address.Port);
        }

        private int MapWithErrorMessage<T>(IImmutableDictionary<T, int> dict, T value, string unknown)
        {
            if (dict.TryGetValue(value, out var elem))
                return elem;
            else
                throw new ArgumentOutOfRangeException($"Unknown {unknown} [{value}] in cluster message");
        }

        private MetricsGossipEnvelope MetricsGossipEnvelopeFromBinary(byte[] bytes)
        {
            return MetricsGossipEnvelopeFromProto(MetricsGossipEnvelope.Parser.ParseFrom(Decompress(bytes)));
        }

        private MetricsGossipEnvelope MetricsGossipEnvelopeToProto(MetricsGossipEnvelope envelope)
        {
            var allNodeMetrics = envelope.Gossip.Nodes;
            var allAddresses = allNodeMetrics.Select(m => m.Address).ToImmutableArray();
            var addressMapping = allAddresses.Select((a, i) => (Index: i, Value: a)).ToImmutableDictionary(p => p.Value, p => p.Index);
            var allMetricNames = allNodeMetrics.Aggregate(
                ImmutableHashSet<string>.Empty,
                (set, metrics) => set.Union(metrics.Metrics.Select(m => m.Name))).ToImmutableArray();
            var metricNamesMapping = allMetricNames.Select((a, i) => (Index: i, Value: a)).ToImmutableDictionary(p => p.Value, p => p.Index);
            
            int MapAddress(Actor.Address address) => MapWithErrorMessage(addressMapping, address, "address");
            int MapName(string name) => MapWithErrorMessage(metricNamesMapping, name, "metric name");

            Option<NodeMetrics.Types.EWMA> EwmaToProto(Option<NodeMetrics.Types.EWMA> ewma)
                => ewma.Select(e => new NodeMetrics.Types.EWMA((decimal)e.Value, (decimal)e.Alpha));

            NodeMetrics.Types.Number NumberToProto(decimal number)
            {
                // Since we do not have Number type in .NET and using decimal instead, 
                // just always use double as a Protobuf serialization type
                return new NodeMetrics.Types.Number()
                {
                    Type = NodeMetrics.Types.NumberType.Double,
                    Value64 = (ulong)BitConverter.DoubleToInt64Bits((double)number)
                };
            }

            NodeMetrics.Types.Metric MetricToProto(NodeMetrics.Types.Metric m)
            {
                var metric = new NodeMetrics.Types.Metric()
                {
                     NameIndex = MapName(m.Name),
                     Number = NumberToProto(m.DecimalNumber),
                };

                var ewma = EwmaToProto(m.Average);
                if (ewma.HasValue)
                    metric.Ewma = ewma.Value;

                return metric;
            }

            NodeMetrics NodeMetricsToProto(NodeMetrics nodeMetrics)
            {
                return new NodeMetrics()
                {
                    AddressIndex = MapAddress(nodeMetrics.Address),
                    Timestamp = nodeMetrics.Timestamp,
                    Metrics = { nodeMetrics.Metrics.Select(MetricToProto) }
                };
            }

            var nodeMetricsProto = allNodeMetrics.Select(NodeMetricsToProto);
            
            return new MetricsGossipEnvelope()
            {
                From = AddressToProto(envelope.FromAddress),
                Reply = envelope.Reply,
                Gossip = new MetricsGossip()
                {
                    AllAddresses = { allAddresses.Select(AddressToProto) },
                    AllMetricNames = { allMetricNames },
                    NodeMetrics = { nodeMetricsProto }
                }
            };
        }

        private MetricsGossipEnvelope MetricsGossipEnvelopeFromProto(MetricsGossipEnvelope envelope)
        {
            var gossip = envelope.Gossip;
            var addressMapping = gossip.AllAddresses.Select(AddressFromProto).ToImmutableArray();
            var metricNameMapping = gossip.AllMetricNames.ToImmutableArray();
            
            Option<NodeMetrics.Types.EWMA> EwmaFromProto(NodeMetrics.Types.EWMA ewma) 
                => new NodeMetrics.Types.EWMA((decimal)ewma.Value, (decimal)ewma.Alpha);

            decimal NumberFromProto(NodeMetrics.Types.Number number)
            {
                switch (number.Type)
                {
                    case NodeMetrics.Types.NumberType.Double:
                        return (decimal)BitConverter.Int64BitsToDouble((long)number.Value64);
                    case NodeMetrics.Types.NumberType.Float:
                        return (decimal)BitConverter.ToSingle(BitConverter.GetBytes(number.Value32), 0);
                    case NodeMetrics.Types.NumberType.Integer:
                        return number.Value32;
                    case NodeMetrics.Types.NumberType.Long:
                        return number.Value64;
                    case NodeMetrics.Types.NumberType.Serialized:
                        // TODO: This is what that have in scala (but .NET is missing Number class):
                        /*val in = new ClassLoaderObjectInputStream(
                            system.dynamicAccess.classLoader,
                            new ByteArrayInputStream(number.getSerialized.toByteArray))
                        val obj = in.readObject
                            in.close()
                        obj.asInstanceOf[jl.Number]*/
                        throw new NotImplementedException($"{NodeMetrics.Types.NumberType.Serialized} number type is not supported");
                    default:
                        throw new ArgumentOutOfRangeException(nameof(number));
                }
            }

            NodeMetrics.Types.Metric MetricFromProto(NodeMetrics.Types.Metric metric)
            {
                return new NodeMetrics.Types.Metric(
                    metricNameMapping[metric.NameIndex], 
                    NumberFromProto(metric.Number), 
                    metric.Ewma != null ? EwmaFromProto(metric.Ewma) : Option<NodeMetrics.Types.EWMA>.None);
            }

            NodeMetrics NodeMetricsFromProto(NodeMetrics metrics)
            {
                return new NodeMetrics(
                    addressMapping[metrics.AddressIndex], 
                    metrics.Timestamp, 
                    metrics.Metrics.Select(MetricFromProto).ToImmutableArray());
            }

            var nodeMetrics = gossip.NodeMetrics.Select(NodeMetricsFromProto).ToImmutableHashSet();
            
            return new MetricsGossipEnvelope(AddressFromProto(envelope.From), new MetricsGossip(nodeMetrics), envelope.Reply);
        }

        private Metrics.AdaptiveLoadBalancingPool AdaptiveLoadBalancingPoolFromBinary(byte[] bytes)
        {
            var proto = AdaptiveLoadBalancingPool.Parser.ParseFrom(bytes);

            IMetricsSelector selector;
            if (proto.MetricsSelector != null)
            {
                var s = proto.MetricsSelector;
                selector = _serialization.Deserialize(s.Data.ToByteArray(), (int)s.SerializerId, s.Manifest) as IMetricsSelector;
            }
            else
            {
                selector = Metrics.MixMetricsSelector.Instance;
            }
            
            return new Metrics.AdaptiveLoadBalancingPool(
                metricsSelector: selector, 
                nrOfInstances: (int)proto.NrOfInstances, 
                supervisorStrategy: null, 
                routerDispatcher: proto.RouterDispatcher ?? Dispatchers.DefaultDispatcherId, 
                usePoolDispatcher: proto.UsePoolDispatcher);
        }

        private Metrics.MixMetricsSelector MixMetricSelectorFromBinary(byte[] bytes)
        {
            var proto = MixMetricsSelector.Parser.ParseFrom(bytes);
            return new Metrics.MixMetricsSelector(proto.Selectors.Select(s =>
            {
                // should be safe because we serialized only the right subtypes of MetricsSelector
                return MetricSelectorFromProto(s) as CapacityMetricsSelector;
            }).ToImmutableArray());
        }

        private IMetricsSelector MetricSelectorFromProto(Serialization.MetricsSelector selector)
        {
            return _serialization.Deserialize(selector.Data.ToByteArray(), (int)selector.SerializerId, selector.Manifest) as IMetricsSelector;
        }
        
    }
}