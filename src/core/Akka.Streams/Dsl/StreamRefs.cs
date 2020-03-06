//-----------------------------------------------------------------------
// <copyright file="StreamRefs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Streams.Implementation.StreamRef;

namespace Akka.Streams.Dsl
{

    /// <summary> 
    /// API MAY CHANGE: The functionality of stream refs is working, however it is expected that the materialized value
    /// will eventually be able to remove the Task wrapping the stream references. For this reason the API is now marked
    /// as API may change. See ticket https://github.com/akka/akka/issues/24372 for more details.
    /// 
    /// Factories for creating stream refs.
    /// </summary>
    [ApiMayChange]
    public static class StreamRefs
    {
        /// <summary>
        /// A local <see cref="Sink{TIn,TMat}"/> which materializes a <see cref="SourceRef{T}"/> which can be used by other streams (including remote ones),
        /// to consume data from this local stream, as if they were attached in the spot of the local Sink directly.
        /// 
        /// Adheres to <see cref="StreamRefAttributes"/>.
        /// </summary>
        /// <seealso cref="SourceRef{T}"/>
        [ApiMayChange]
        public static Sink<T, Task<ISourceRef<T>>> SourceRef<T>() =>
            Sink.FromGraph<T, Task<ISourceRef<T>>>(new SinkRefStageImpl<T>(null));

        /// <summary>
        /// A local <see cref="Sink{TIn,TMat}"/> which materializes a <see cref="SinkRef{T}"/> which can be used by other streams (including remote ones),
        /// to consume data from this local stream, as if they were attached in the spot of the local Sink directly.
        /// 
        /// Adheres to <see cref="StreamRefAttributes"/>.
        /// 
        /// See more detailed documentation on [[SinkRef]].
        /// </summary>
        /// <seealso cref="SinkRef{T}"/>
        [ApiMayChange]
        public static Source<T, Task<ISinkRef<T>>> SinkRef<T>() =>
            Source.FromGraph<T, Task<ISinkRef<T>>>(new SourceRefStageImpl<T>(null));
    }

    /// <summary>
    /// INTERNAL API.
    /// </summary>
    [InternalApi]
    public sealed class StreamRefSettings
    {
        public static StreamRefSettings Create(Config config)
        {
            // No need to check for Config.IsEmpty because this function supposed to process empty Config
            if (config == null)
                throw ConfigurationException.NullOrEmptyConfig<StreamRefSettings>();

            return new StreamRefSettings(
                bufferCapacity: config.GetInt("buffer-capacity", 32),
                demandRedeliveryInterval: config.GetTimeSpan("demand-redelivery-interval", TimeSpan.FromSeconds(1)),
                subscriptionTimeout: config.GetTimeSpan("subscription-timeout", TimeSpan.FromSeconds(30)),
                finalTerminationSignalDeadline: config.GetTimeSpan("final-termination-signal-deadline", TimeSpan.FromSeconds(2)));
        }

        public int BufferCapacity { get; }
        public TimeSpan DemandRedeliveryInterval { get; }
        public TimeSpan SubscriptionTimeout { get; }
        public TimeSpan FinalTerminationSignalDeadline { get; }

        public StreamRefSettings(int bufferCapacity, TimeSpan demandRedeliveryInterval, TimeSpan subscriptionTimeout, TimeSpan finalTerminationSignalDeadline)
        {
            BufferCapacity = bufferCapacity;
            DemandRedeliveryInterval = demandRedeliveryInterval;
            SubscriptionTimeout = subscriptionTimeout;
        }

        public string ProductPrefix => nameof(StreamRefSettings);

        public StreamRefSettings WithBufferCapacity(int value) => Copy(bufferCapacity: value);
        public StreamRefSettings WithDemandRedeliveryInterval(TimeSpan value) => Copy(demandRedeliveryInterval: value);
        public StreamRefSettings WithSubscriptionTimeout(TimeSpan value) => Copy(subscriptionTimeout: value);

        public StreamRefSettings Copy(int? bufferCapacity = null,
            TimeSpan? demandRedeliveryInterval = null,
            TimeSpan? subscriptionTimeout = null,
            TimeSpan? finalTerminationSignalDeadline = null) => new StreamRefSettings(
            bufferCapacity: bufferCapacity ?? this.BufferCapacity,
            demandRedeliveryInterval: demandRedeliveryInterval ?? this.DemandRedeliveryInterval,
            subscriptionTimeout: subscriptionTimeout ?? this.SubscriptionTimeout,
            finalTerminationSignalDeadline: finalTerminationSignalDeadline ?? this.FinalTerminationSignalDeadline);
    }
}
