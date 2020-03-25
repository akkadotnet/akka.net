//-----------------------------------------------------------------------
// <copyright file="TimeoutSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;

namespace Akka.Coordination
{
    /// <summary>
    /// The timeout settings used for the <see cref="Lease"/>
    /// </summary>
    public sealed class TimeoutSettings
    {
        /// <summary>
        /// Creates a new <see cref="TimeoutSettings"/> instance.
        /// </summary>
        /// <param name="config">Lease config</param>
        /// <returns>The requested settings.</returns>
        public static TimeoutSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<TimeoutSettings>();

            var heartBeatTimeout = config.GetTimeSpan("heartbeat-timeout");

            TimeSpan heartBeatInterval;
            var iv = config.GetValue("heartbeat-interval");
            if (iv.IsString() && string.IsNullOrEmpty(iv.GetString()))
                heartBeatInterval = TimeSpan.FromMilliseconds(Math.Max(heartBeatTimeout.TotalMilliseconds / 10, 5000));
            else
                heartBeatInterval = iv.GetTimeSpan();

            if (heartBeatInterval.TotalMilliseconds >= (heartBeatTimeout.TotalMilliseconds / 2))
                throw new ArgumentException("heartbeat-interval must be less than half heartbeat-timeout");

            return new TimeoutSettings(heartBeatInterval, heartBeatTimeout, config.GetTimeSpan("lease-operation-timeout"));
        }

        /// <summary>
        /// Interval for communicating with the third party to confirm the lease is still held
        /// </summary>
        public TimeSpan HeartbeatInterval { get; }

        /// <summary>
        /// If the node that acquired the leases crashes, how long should the lease be held before another owner can get it
        /// </summary>

        public TimeSpan HeartbeatTimeout { get; }
        /// <summary>
        /// Lease implementations are expected to time out acquire and release calls or document that they do not implement an operation timeout
        /// </summary>
        public TimeSpan OperationTimeout { get; }

        /// <summary>
        /// Creates a new <see cref="TimeoutSettings"/> instance.
        /// </summary>
        /// <param name="heartbeatInterval">Interval for communicating with the third party to confirm the lease is still held</param>
        /// <param name="heartbeatTimeout">If the node that acquired the leases crashes, how long should the lease be held before another owner can get it</param>
        /// <param name="operationTimeout">Lease implementations are expected to time out acquire and release calls or document that they do not implement an operation timeout</param>
        public TimeoutSettings(TimeSpan heartbeatInterval, TimeSpan heartbeatTimeout, TimeSpan operationTimeout)
        {
            HeartbeatInterval = heartbeatInterval;
            HeartbeatTimeout = heartbeatTimeout;
            OperationTimeout = operationTimeout;
        }

        /// <summary>
        /// Create a <see cref="TimeoutSettings"/> with specified heartbeat interval.
        /// </summary>
        /// <param name="heartbeatInterval">Interval for communicating with the third party to confirm the lease is still held</param>
        /// <returns></returns>
        public TimeoutSettings WithHeartbeatInterval(TimeSpan heartbeatInterval)
        {
            return Copy(heartbeatInterval: heartbeatInterval);
        }

        /// <summary>
        /// Create a <see cref="TimeoutSettings"/> with specified heartbeat timeout.
        /// </summary>
        /// <param name="heartbeatTimeout">If the node that acquired the leases crashes, how long should the lease be held before another owner can get it</param>
        /// <returns></returns>
        public TimeoutSettings WithHeartbeatTimeout(TimeSpan heartbeatTimeout)
        {
            return Copy(heartbeatTimeout: heartbeatTimeout);
        }

        /// <summary>
        /// Create a <see cref="TimeoutSettings"/> with specified operation timeout.
        /// </summary>
        /// <param name="operationTimeout">Lease implementations are expected to time out acquire and release calls or document that they do not implement an operation timeout</param>
        /// <returns></returns>
        public TimeoutSettings withOperationTimeout(TimeSpan operationTimeout)
        {
            return Copy(operationTimeout: operationTimeout);
        }

        private TimeoutSettings Copy(TimeSpan? heartbeatInterval = null, TimeSpan? heartbeatTimeout = null, TimeSpan? operationTimeout = null)
        {
            return new TimeoutSettings(heartbeatInterval ?? HeartbeatInterval, heartbeatTimeout ?? HeartbeatTimeout, operationTimeout ?? OperationTimeout);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"TimeoutSettings({ HeartbeatInterval }, { HeartbeatTimeout }, { OperationTimeout })";
        }
    }
}


