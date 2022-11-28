﻿//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Metrics.Helpers;
using Akka.Dispatch;
using Akka.Cluster.Metrics.Serialization.Proto.Msg;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Serialization;
using Akka.Util;
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

        private readonly ExtendedActorSystem _system;
        private Akka.Serialization.Serialization _ser;

        private Akka.Serialization.Serialization Serialization
        {
            get => _ser ?? (_ser = new Akka.Serialization.Serialization(_system));
        }
        
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
                case MetricsGossipEnvelope m: return Compress(MetricsGossipEnvelopeToProto(m)); // TODO: Add compression here
                case AdaptiveLoadBalancingPool alb: return AdaptiveLoadBalancingPoolToBinary(alb);
                case MixMetricsSelector mms: return MixMetricsSelectorToBinary(mms);
                case CpuMetricsSelector _: return Array.Empty<byte>();
                case MemoryMetricsSelector _: return Array.Empty<byte>();
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
                case HeapMetricsSelectorManifest: return MemoryMetricsSelector.Instance;
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
                case MemoryMetricsSelector _: return HeapMetricsSelectorManifest;
                default:
                    throw new ArgumentException($"Can't serialize object of type {o.GetType().Name} in [{GetType().Name}]");
            }
        }

        private byte[] AdaptiveLoadBalancingPoolToBinary(AdaptiveLoadBalancingPool pool)
        {
            var proto = new Akka.Cluster.Metrics.Serialization.Proto.Msg.AdaptiveLoadBalancingPool()
            {
                NrOfInstances = (uint)pool.NrOfInstances,
                UsePoolDispatcher = pool.UsePoolDispatcher
            };
            
            if (!pool.MetricsSelector.Equals(MixMetricsSelector.Instance))
                proto.MetricsSelector = MetricsSelectorToProto(pool.MetricsSelector);

            if (pool.RouterDispatcher != Dispatchers.DefaultDispatcherId)
                proto.RouterDispatcher = pool.RouterDispatcher;

            return proto.ToByteArray();
        }

        private MetricsSelector MetricsSelectorToProto(IMetricsSelector selector)
        {
            var serializer = Serialization.FindSerializerFor(selector);
            
            return new MetricsSelector()
            {
                Data = ByteString.CopyFrom(serializer.ToBinary(selector)),
                SerializerId = (uint)serializer.Identifier,
                Manifest = selector.GetType().TypeQualifiedName()
            };
        }

        private byte[] MixMetricsSelectorToBinary(MixMetricsSelector selector)
        {
            var proto = new Akka.Cluster.Metrics.Serialization.Proto.Msg.MixMetricsSelector
            {
                Selectors = { selector.Selectors.Select(MetricsSelectorToProto) }
            };
            return proto.ToByteArray();
        }
        
        /// <summary>
        /// Converts Akka.NET type into Protobuf serializable message
        /// </summary>
        private AddressData AddressToProto(Address address)
        {
            return new AddressData()
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
        private Address AddressFromProto(AddressData address)
        {
            return new Address(address.Protocol, address.System, address.Hostname, (int)address.Port);
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
            return MetricsGossipEnvelopeFromProto(Akka.Cluster.Metrics.Serialization.Proto.Msg.MetricsGossipEnvelope.Parser.ParseFrom(Decompress(bytes)));
        }

        private Akka.Cluster.Metrics.Serialization.Proto.Msg.MetricsGossipEnvelope MetricsGossipEnvelopeToProto(MetricsGossipEnvelope envelope)
        {
            var allNodeMetrics = envelope.Gossip.Nodes;
            var allAddresses = allNodeMetrics.Select(m => m.Address).ToImmutableArray();
            var addressMapping = allAddresses.Select((a, i) => (Index: i, Value: a)).ToImmutableDictionary(p => p.Value, p => p.Index);
            var allMetricNames = allNodeMetrics.Aggregate(
                ImmutableHashSet<string>.Empty,
                (set, metrics) => set.Union(metrics.Metrics.Select(m => m.Name))).ToImmutableArray();
            var metricNamesMapping = allMetricNames.Select((a, i) => (Index: i, Value: a)).ToImmutableDictionary(p => p.Value, p => p.Index);
            
            int MapAddress(Address address) => MapWithErrorMessage(addressMapping, address, "address");
            int MapName(string name) => MapWithErrorMessage(metricNamesMapping, name, "metric name");

            Option<Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.EWMA> EwmaToProto(Option<NodeMetrics.Types.EWMA> ewma)
                => ewma.Select(e => new Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.EWMA{Value = e.Value, Alpha = e.Alpha});

            Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.Number NumberToProto(AnyNumber number)
            {
                var proto = new Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.Number();
                switch (number.Type)
                {
                    case AnyNumber.NumberType.Int:
                        proto.Type = Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Integer;
                        proto.Value32 = Convert.ToUInt32(number.LongValue);
                        break;
                    case AnyNumber.NumberType.Long:
                        proto.Type = Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Long;
                        proto.Value64 = Convert.ToUInt64(number.LongValue);
                        break;
                    case AnyNumber.NumberType.Float:
                        proto.Type = Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Float;
                        proto.Value32 = (uint)BitConverter.ToInt32(BitConverter.GetBytes((float)number.DoubleValue), 0);
                        break;
                    case AnyNumber.NumberType.Double:
                        proto.Type = Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Double;
                        proto.Value64 = (ulong)BitConverter.DoubleToInt64Bits(number.DoubleValue);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return proto;
            }

            Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.Metric MetricToProto(NodeMetrics.Types.Metric m)
            {
                var metric = new Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.Metric
                {
                     NameIndex = MapName(m.Name),
                     Number = NumberToProto(m.Value),
                };

                var ewma = EwmaToProto(m.Average);
                if (ewma.HasValue)
                    metric.Ewma = ewma.Value;

                return metric;
            }

            Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics NodeMetricsToProto(NodeMetrics nodeMetrics)
            {
                return new Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics
                {
                    AddressIndex = MapAddress(nodeMetrics.Address),
                    Timestamp = nodeMetrics.Timestamp,
                    Metrics = { nodeMetrics.Metrics.Select(MetricToProto) }
                };
            }

            var nodeMetricsProto = allNodeMetrics.Select(NodeMetricsToProto);
            
            return new Akka.Cluster.Metrics.Serialization.Proto.Msg.MetricsGossipEnvelope
            {
                From = AddressToProto(envelope.FromAddress),
                Reply = envelope.Reply,
                Gossip = new Akka.Cluster.Metrics.Serialization.Proto.Msg.MetricsGossip
                {
                    AllAddresses = { allAddresses.Select(AddressToProto) },
                    AllMetricNames = { allMetricNames },
                    NodeMetrics = { nodeMetricsProto }
                }
            };
        }

        private MetricsGossipEnvelope MetricsGossipEnvelopeFromProto(Akka.Cluster.Metrics.Serialization.Proto.Msg.MetricsGossipEnvelope envelope)
        {
            var gossip = envelope.Gossip;
            var addressMapping = gossip.AllAddresses.Select(AddressFromProto).ToImmutableArray();
            var metricNameMapping = gossip.AllMetricNames.ToImmutableArray();
            
            Option<NodeMetrics.Types.EWMA> EwmaFromProto(Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.EWMA ewma) 
                => new NodeMetrics.Types.EWMA(ewma.Value, ewma.Alpha);

            AnyNumber NumberFromProto(Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.Number number)
            {
                switch (number.Type)
                {
                    case Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Double:
                        return BitConverter.Int64BitsToDouble((long)number.Value64);
                    case Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Float:
                        return BitConverter.ToSingle(BitConverter.GetBytes((int)number.Value32), 0);
                    case Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Integer:
                        return Convert.ToInt32(number.Value32);
                    case Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Long:
                        return Convert.ToInt64(number.Value64);
                    case Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Serialized:
                        // TODO: Should we somehow port this?
                        /*val in = new ClassLoaderObjectInputStream(
                            system.dynamicAccess.classLoader,
                            new ByteArrayInputStream(number.getSerialized.toByteArray))
                        val obj = in.readObject
                            in.close()
                        obj.asInstanceOf[jl.Number]*/
                        throw new NotImplementedException($"{Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.NumberType.Serialized} number type is not supported");
                    default:
                        throw new ArgumentOutOfRangeException(nameof(number));
                }
            }

            NodeMetrics.Types.Metric MetricFromProto(Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics.Types.Metric metric)
            {
                return new NodeMetrics.Types.Metric(
                    metricNameMapping[metric.NameIndex], 
                    NumberFromProto(metric.Number), 
                    metric.Ewma != null ? EwmaFromProto(metric.Ewma) : Option<NodeMetrics.Types.EWMA>.None);
            }

            NodeMetrics NodeMetricsFromProto(Akka.Cluster.Metrics.Serialization.Proto.Msg.NodeMetrics metrics)
            {
                return new NodeMetrics(
                    addressMapping[metrics.AddressIndex], 
                    metrics.Timestamp, 
                    metrics.Metrics.Select(MetricFromProto).ToImmutableArray());
            }

            var nodeMetrics = gossip.NodeMetrics.Select(NodeMetricsFromProto).ToImmutableHashSet();
            
            return new MetricsGossipEnvelope(AddressFromProto(envelope.From), new MetricsGossip(nodeMetrics), envelope.Reply);
        }

        private AdaptiveLoadBalancingPool AdaptiveLoadBalancingPoolFromBinary(byte[] bytes)
        {
            var proto = Akka.Cluster.Metrics.Serialization.Proto.Msg.AdaptiveLoadBalancingPool.Parser.ParseFrom(bytes);

            IMetricsSelector selector;
            if (proto.MetricsSelector != null)
            {
                var s = proto.MetricsSelector;
                selector = Serialization.Deserialize(s.Data.ToByteArray(), (int)s.SerializerId, s.Manifest) as IMetricsSelector;
            }
            else
            {
                selector = MixMetricsSelector.Instance;
            }
            
            return new AdaptiveLoadBalancingPool(
                metricsSelector: selector, 
                nrOfInstances: (int)proto.NrOfInstances, 
                supervisorStrategy: null, 
                routerDispatcher: !string.IsNullOrEmpty(proto.RouterDispatcher) ? proto.RouterDispatcher : Dispatchers.DefaultDispatcherId, 
                usePoolDispatcher: proto.UsePoolDispatcher);
        }

        private MixMetricsSelector MixMetricSelectorFromBinary(byte[] bytes)
        {
            var proto = Akka.Cluster.Metrics.Serialization.Proto.Msg.MixMetricsSelector.Parser.ParseFrom(bytes);
            return new MixMetricsSelector(proto.Selectors.Select(s =>
            {
                // should be safe because we serialized only the right subtypes of MetricsSelector
                return (CapacityMetricsSelector) MetricSelectorFromProto(s);
            }).ToImmutableArray());
        }

        private IMetricsSelector MetricSelectorFromProto(MetricsSelector selector)
        {
            return Serialization.Deserialize(selector.Data.ToByteArray(), (int)selector.SerializerId, selector.Manifest) as IMetricsSelector;
        }
        
    }
}
