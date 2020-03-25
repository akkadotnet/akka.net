using System;
using Akka.Configuration;

namespace Akka.Coordination
{
    public sealed class TimeoutSettings
    {
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

        public TimeSpan HeartbeatInterval { get; }
        public TimeSpan HeartbeatTimeout { get; }
        public TimeSpan OperationTimeout { get; }

        public TimeoutSettings(TimeSpan heartbeatInterval, TimeSpan heartbeatTimeout, TimeSpan operationTimeout)
        {
            HeartbeatInterval = heartbeatInterval;
            HeartbeatTimeout = heartbeatTimeout;
            OperationTimeout = operationTimeout;
        }

        public TimeoutSettings WithHeartbeatInterval(TimeSpan heartbeatInterval)
        {
            return Copy(heartbeatInterval: heartbeatInterval);
        }

        public TimeoutSettings WithHeartbeatTimeout(TimeSpan heartbeatTimeout)
        {
            return Copy(heartbeatTimeout: heartbeatTimeout);
        }

        public TimeoutSettings withOperationTimeout(TimeSpan operationTimeout)
        {
            return Copy(operationTimeout: operationTimeout);
        }

        public TimeoutSettings Copy(TimeSpan? heartbeatInterval = null, TimeSpan? heartbeatTimeout = null, TimeSpan? operationTimeout = null)
        {
            return new TimeoutSettings(heartbeatInterval ?? HeartbeatInterval, heartbeatTimeout ?? HeartbeatTimeout, operationTimeout ?? OperationTimeout);
        }

        public override string ToString()
        {
            return $"TimeoutSettings({ HeartbeatInterval }, { HeartbeatTimeout }, { OperationTimeout })";
        }
    }
}


