//-----------------------------------------------------------------------
// <copyright file="RemoteSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Remote
{
    public class RemoteSettings
    {
        public RemoteSettings(Config config)
        {
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
            if (RemoteLifecycleEventsLogLevel.Equals("on")) RemoteLifecycleEventsLogLevel = "DEBUG";
            if (RemoteLifecycleEventsLogLevel.Equals("off")) RemoteLifecycleEventsLogLevel = "WARNING";
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
            InitialSysMsgDeliveryTimeout = config.GetTimeSpan("akka.remote.initial-system-message-delivery-timeout");
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

        public Config Config { get; private set; }

        public HashSet<string> TrustedSelectionPaths { get; set; }

        public bool UntrustedMode { get; set; }

        public bool LogSend { get; set; }

        public bool LogReceive { get; set; }

        public int LogBufferSizeExceeding { get; set; }

        public string RemoteLifecycleEventsLogLevel { get; set; }

        public string Dispatcher { get; set; }

        public TimeSpan ShutdownTimeout { get; set; }

        public TimeSpan FlushWait { get; set; }

        public IList<string> TransportNames { get; set; }

        public IDictionary<string, string> Adapters { get; set; }

        public TransportSettings[] Transports { get; set; }
        public TimeSpan BackoffPeriod { get; set; }
        public TimeSpan RetryGateClosedFor { get; set; }
        public bool UsePassiveConnections { get; set; }
        public int SysMsgBufferSize { get; set; }
        public TimeSpan SysResendTimeout { get; set; }
        public TimeSpan InitialSysMsgDeliveryTimeout { get; set; }
        public TimeSpan SysMsgAckTimeout { get; set; }
        public TimeSpan? QuarantineDuration { get; set; }
        public TimeSpan StartupTimeout { get; set; }
        public TimeSpan CommandAckTimeout { get; set; }

        public Config WatchFailureDetectorConfig { get; set; }
        public string WatchFailureDetectorImplementationClass { get; set; }
        public TimeSpan WatchHeartBeatInterval { get; set; }
        public TimeSpan WatchUnreachableReaperInterval { get; set; }
        public TimeSpan WatchHeartbeatExpectedResponseAfter { get; set; }

        private Config TransportConfigFor(string transportName)
        {
            return Config.GetConfig(transportName);
        }

        public Props ConfigureDispatcher(Props props)
        {
            return String.IsNullOrEmpty(Dispatcher) ? props : props.WithDispatcher(Dispatcher);
        }

        public class TransportSettings
        {
            public TransportSettings(Config config)
            {
                TransportClass = config.GetString("transport-class");
                Adapters = config.GetStringList("applied-adapters").Reverse().ToList();
                Config = config;
            }

            public Config Config { get; set; }

            public IList<string> Adapters { get; set; }

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
