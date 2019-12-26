// //-----------------------------------------------------------------------
// // <copyright file="MessageSelectorSerializers.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Util;
using Akka.Util.Extensions;
using Google.Protobuf;

namespace Akka.Cluster.Metrics.Serialization
{
    /// <summary>
    /// Defines interface for <see cref="IMetricsSelector"/> serializers
    /// </summary>
    internal interface IMetricsSelectorSerializer
    {
        /// <summary>
        /// Unique serializer id
        /// </summary>
        uint Id { get; }
        /// <summary>
        /// Type of selector that is seriaized/deserialized
        /// </summary>
        Type SelectorType { get; }
        /// <summary>
        /// Serialize selector to bytes
        /// </summary>
        ByteString Serialize(IMetricsSelector selector);
        /// <summary>
        /// Deserialize selector from bytes
        /// </summary>
        /// <param name="selector"></param>
        /// <returns></returns>
        IMetricsSelector Deserialize(byte[] selector);
    }

    /// <summary>
    /// Contains all registered serializers for <see cref="IMetricsSelector"/> child type instances
    /// </summary>
    internal static class MetricsSelectorSerializersRegistry
    {
        private static readonly Dictionary<uint, IMetricsSelectorSerializer> Serializers = new IMetricsSelectorSerializer[]
        {
            new CpuMetricsSelectorSerializer(),
            new HeapMetricsSelectorSerializer(),
            new SystemLoadAverageMetricsSelectorSerializer()
        }.ToDictionary(s => s.Id, s => s);

        /// <summary>
        /// Gets registered serializer by Id
        /// </summary>
        public static Option<IMetricsSelectorSerializer> GetById(uint serializerId) 
            => Serializers.ContainsKey(serializerId) ? Serializers[serializerId].AsOption() : Option<IMetricsSelectorSerializer>.None;
        
        /// <summary>
        /// Gets serializer for given selector type
        /// </summary>
        public static Option<IMetricsSelectorSerializer> GetByType(Type selectorType) 
            => Serializers.Values.FirstOrDefault(s => s.SelectorType == selectorType)?.AsOption() ?? Option<IMetricsSelectorSerializer>.None;
        
        /// <summary>
        /// Gets serializer for given selector type
        /// </summary>
        public static Option<IMetricsSelectorSerializer> GetByType<TSelector>() where TSelector : IMetricsSelector
            => GetByType(typeof(TSelector));
    }
    
    /// <summary>
    /// CpuMetricsSelectorSerializer
    /// </summary>
    public class CpuMetricsSelectorSerializer : IMetricsSelectorSerializer
    {
        /// <inheritdoc />
        public uint Id => 1;

        /// <inheritdoc />
        public Type SelectorType => typeof(CpuMetricsSelector);

        /// <inheritdoc />
        public ByteString Serialize(IMetricsSelector selector) => ByteString.Empty;

        /// <inheritdoc />
        public IMetricsSelector Deserialize(byte[] selector) => CpuMetricsSelector.Instance;
    }
    
    /// <summary>
    /// HeapMetricsSelectorSerializer
    /// </summary>
    public class HeapMetricsSelectorSerializer : IMetricsSelectorSerializer
    {
        /// <inheritdoc />
        public uint Id => 2;

        /// <inheritdoc />
        public Type SelectorType => typeof(HeapMetricsSelector);

        /// <inheritdoc />
        public ByteString Serialize(IMetricsSelector selector) => ByteString.Empty;

        /// <inheritdoc />
        public IMetricsSelector Deserialize(byte[] selector) => HeapMetricsSelector.Instance;
    }
    
    /// <summary>
    /// SystemLoadAverageMetricsSelectorSerializer
    /// </summary>
    public class SystemLoadAverageMetricsSelectorSerializer : IMetricsSelectorSerializer
    {
        /// <inheritdoc />
        public uint Id => 3;

        /// <inheritdoc />
        public Type SelectorType => typeof(SystemLoadAverageMetricsSelector);

        /// <inheritdoc />
        public ByteString Serialize(IMetricsSelector selector) => ByteString.Empty;

        /// <inheritdoc />
        public IMetricsSelector Deserialize(byte[] selector) => SystemLoadAverageMetricsSelector.Instance;
    }
}