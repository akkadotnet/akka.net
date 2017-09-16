//-----------------------------------------------------------------------
// <copyright file="RemoteSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Remote
{
    /// <summary>
    /// This class represents configuration information used when setting up remoting.
    /// </summary>
    public class RemoteSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteSettings"/> class.
        /// </summary>
        /// <param name="config">The configuration to use when setting up remoting.</param>
        public RemoteSettings(Config config)
        {
            //TODO: need to add value validation for each field
            Config = config;
            LogReceive = config.GetBoolean("akka.remote.log-received-messages");
            LogSend = config.GetBoolean("akka.remote.log-sent-messages");

            var bufferSizeLogKey = "akka.remote.log-buffer-size-exceeding";
            if (config.GetString(bufferSizeLogKey).ToLowerInvariant().Equals("off") ||
                config.GetString(bufferSizeLogKey).ToLowerInvariant().Equals("false"))
            {
                LogBufferSizeExceeding = Int32.MaxValue;
            }
            else
            {
                LogBufferSizeExceeding = config.GetInt(bufferSizeLogKey);
            }

            UntrustedMode = config.GetBoolean("akka.remote.untrusted-mode");
            TrustedSelectionPaths = new HashSet<string>(config.GetStringList("akka.remote.trusted-selection-paths"));
            RemoteLifecycleEventsLogLevel = config.GetString("akka.remote.log-remote-lifecycle-events") ?? "DEBUG";
            Dispatcher = config.GetString("akka.remote.use-dispatcher");
            if (RemoteLifecycleEventsLogLevel.Equals("on", StringComparison.OrdinalIgnoreCase)) RemoteLifecycleEventsLogLevel = "DEBUG";
            FlushWait = config.GetTimeSpan("akka.remote.flush-wait-on-shutdown");
            ShutdownTimeout = config.GetTimeSpan("akka.remote.shutdown-timeout");
            TransportNames = config.GetStringList("akka.remote.enabled-transports");
            Transports = (from transportName in TransportNames
                let transportConfig = TransportConfigFor(transportName)
                select new TransportSettings(transportConfig)).ToArray();
            Adapters = ConfigToMap(config.GetConfig("akka.remote.adapters"));
            BackoffPeriod = config.GetTimeSpan("akka.remote.backoff-interval");
            RetryGateClosedFor = config.GetTimeSpan("akka.remote.retry-gate-closed-for", TimeSpan.Zero);
            UsePassiveConnections = config.GetBoolean("akka.remote.use-passive-connections");
            SysMsgBufferSize = config.GetInt("akka.remote.system-message-buffer-size");
            SysResendTimeout = config.GetTimeSpan("akka.remote.resend-interval");
            SysResendLimit = config.GetInt("akka.remote.resend-limit");
            InitialSysMsgDeliveryTimeout = config.GetTimeSpan("akka.remote.initial-system-message-delivery-timeout");
            QuarantineSilentSystemTimeout = config.GetTimeSpan("akka.remote.quarantine-after-silence");
            SysMsgAckTimeout = config.GetTimeSpan("akka.remote.system-message-ack-piggyback-timeout");
            QuarantineDuration = config.GetTimeSpan("akka.remote.prune-quarantine-marker-after");

            StartupTimeout = config.GetTimeSpan("akka.remote.startup-timeout");
            CommandAckTimeout = config.GetTimeSpan("akka.remote.command-ack-timeout");

            WatchFailureDetectorConfig = config.GetConfig("akka.remote.watch-failure-detector");
            WatchFailureDetectorImplementationClass = WatchFailureDetectorConfig.GetString("implementation-class");
            WatchHeartBeatInterval = WatchFailureDetectorConfig.GetTimeSpan("heartbeat-interval");
            WatchUnreachableReaperInterval = WatchFailureDetectorConfig.GetTimeSpan("unreachable-nodes-reaper-interval");
            WatchHeartbeatExpectedResponseAfter = WatchFailureDetectorConfig.GetTimeSpan("expected-response-after");
        }

        /// <summary>
        /// Used for augmenting outbound messages with the Akka scheme
        /// </summary>
        public static readonly string AkkaScheme = "akka";

        /// <summary>
        /// TBD
        /// </summary>
        public Config Config { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public HashSet<string> TrustedSelectionPaths { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool UntrustedMode { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool LogSend { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool LogReceive { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int LogBufferSizeExceeding { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public string RemoteLifecycleEventsLogLevel { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public string Dispatcher { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan ShutdownTimeout { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan FlushWait { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public IList<string> TransportNames { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public IDictionary<string, string> Adapters { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public TransportSettings[] Transports { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan BackoffPeriod { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan RetryGateClosedFor { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public bool UsePassiveConnections { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public int SysMsgBufferSize { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public int SysResendLimit { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan SysResendTimeout { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan InitialSysMsgDeliveryTimeout { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan QuarantineSilentSystemTimeout { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan SysMsgAckTimeout { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan? QuarantineDuration { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan StartupTimeout { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan CommandAckTimeout { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public Config WatchFailureDetectorConfig { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public string WatchFailureDetectorImplementationClass { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan WatchHeartBeatInterval { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan WatchUnreachableReaperInterval { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan WatchHeartbeatExpectedResponseAfter { get; set; }

        private Config TransportConfigFor(string transportName)
        {
            return Config.GetConfig(transportName);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="props">TBD</param>
        /// <returns>TBD</returns>
        public Props ConfigureDispatcher(Props props)
        {
            return String.IsNullOrEmpty(Dispatcher) 
                ? props 
                : props.WithDispatcher(Dispatcher);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class TransportSettings
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="config">TBD</param>
            public TransportSettings(Config config)
            {
                TransportClass = config.GetString("transport-class");
                Adapters = config.GetStringList("applied-adapters").Reverse().ToList();
                Config = config;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Config Config { get; set; }

            /// <summary>
            /// TBD
            /// </summary>
            public IList<string> Adapters { get; set; }

            /// <summary>
            /// TBD
            /// </summary>
            public string TransportClass { get; set; }
        }

        private static IDictionary<string, string> ConfigToMap(Config cfg)
        {
            if(cfg.IsEmpty) return new Dictionary<string, string>();
            var unwrapped = cfg.Root.GetObject().Unwrapped;
            return unwrapped.ToDictionary(k => k.Key, v => v.Value != null? v.Value.ToString():null);
        }
    }
}

