//-----------------------------------------------------------------------
// <copyright file="RemoteSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<RemoteSettings>();

            Config = config;
            LogReceive = config.GetBoolean("akka.remote.log-received-messages", false);
            LogSend = config.GetBoolean("akka.remote.log-sent-messages", false);

            // TODO: what is the default value if the key wasn't found?
            var bufferSizeLogKey = "akka.remote.log-buffer-size-exceeding";
            var useBufferSizeLog = config.GetString(bufferSizeLogKey, string.Empty).ToLowerInvariant();
            if (useBufferSizeLog.Equals("off") ||
                useBufferSizeLog.Equals("false") ||
                useBufferSizeLog.Equals("no"))
            {
                LogBufferSizeExceeding = Int32.MaxValue;
            }
            else
            {
                LogBufferSizeExceeding = config.GetInt(bufferSizeLogKey, 0);
            }

            UntrustedMode = config.GetBoolean("akka.remote.untrusted-mode", false);
            TrustedSelectionPaths = new HashSet<string>(config.GetStringList("akka.remote.trusted-selection-paths", new string[] { }));
            RemoteLifecycleEventsLogLevel = config.GetString("akka.remote.log-remote-lifecycle-events", "DEBUG");
            if (RemoteLifecycleEventsLogLevel.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                RemoteLifecycleEventsLogLevel.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                RemoteLifecycleEventsLogLevel.Equals("true", StringComparison.OrdinalIgnoreCase)
                ) RemoteLifecycleEventsLogLevel = "DEBUG";
            Dispatcher = config.GetString("akka.remote.use-dispatcher", null);
            FlushWait = config.GetTimeSpan("akka.remote.flush-wait-on-shutdown", null);
            ShutdownTimeout = config.GetTimeSpan("akka.remote.shutdown-timeout", null);
            TransportNames = config.GetStringList("akka.remote.enabled-transports", new string[] { });
            Transports = (from transportName in TransportNames
                let transportConfig = TransportConfigFor(transportName)
                select new TransportSettings(transportConfig)).ToArray();
            Adapters = ConfigToMap(config.GetConfig("akka.remote.adapters"));
            BackoffPeriod = config.GetTimeSpan("akka.remote.backoff-interval", null);
            RetryGateClosedFor = config.GetTimeSpan("akka.remote.retry-gate-closed-for", TimeSpan.Zero);
            UsePassiveConnections = config.GetBoolean("akka.remote.use-passive-connections", false);
            SysMsgBufferSize = config.GetInt("akka.remote.system-message-buffer-size", 0);
            SysResendTimeout = config.GetTimeSpan("akka.remote.resend-interval", null);
            SysResendLimit = config.GetInt("akka.remote.resend-limit", 0);
            InitialSysMsgDeliveryTimeout = config.GetTimeSpan("akka.remote.initial-system-message-delivery-timeout", null);
            QuarantineSilentSystemTimeout = config.GetTimeSpan("akka.remote.quarantine-after-silence", null);
            SysMsgAckTimeout = config.GetTimeSpan("akka.remote.system-message-ack-piggyback-timeout", null);
            QuarantineDuration = config.GetTimeSpan("akka.remote.prune-quarantine-marker-after", null);

            StartupTimeout = config.GetTimeSpan("akka.remote.startup-timeout", null);
            CommandAckTimeout = config.GetTimeSpan("akka.remote.command-ack-timeout", null);

            WatchFailureDetectorConfig = config.GetConfig("akka.remote.watch-failure-detector");
            WatchFailureDetectorImplementationClass = WatchFailureDetectorConfig.GetString("implementation-class", null);
            WatchHeartBeatInterval = WatchFailureDetectorConfig.GetTimeSpan("heartbeat-interval", null);
            WatchUnreachableReaperInterval = WatchFailureDetectorConfig.GetTimeSpan("unreachable-nodes-reaper-interval", null);
            WatchHeartbeatExpectedResponseAfter = WatchFailureDetectorConfig.GetTimeSpan("expected-response-after", null);
        }

        /// <summary>
        /// Used for augmenting outbound messages with the Akka scheme
        /// </summary>
        public static readonly string AkkaScheme = "akka";

        /// <summary>
        /// Gets the HOCON configuration used to construct these remoting settings.
        /// </summary>
        public Config Config { get; private set; }

        /// <summary>
        /// Gets or sets actor selection paths allowed while untrusted mode is enabled.
        /// </summary>
        public HashSet<string> TrustedSelectionPaths { get; set; }

        /// <summary>
        /// Gets or sets whether incoming messages are subject to untrusted-mode filtering.
        /// </summary>
        public bool UntrustedMode { get; set; }

        /// <summary>
        /// Gets or sets whether sent remote messages are logged.
        /// </summary>
        public bool LogSend { get; set; }

        /// <summary>
        /// Gets or sets whether received remote messages are logged.
        /// </summary>
        public bool LogReceive { get; set; }

        /// <summary>
        /// Gets or sets the endpoint writer buffer size above which a warning is logged.
        /// </summary>
        public int LogBufferSizeExceeding { get; set; }

        /// <summary>
        /// Gets or sets the log level used for remoting lifecycle events.
        /// </summary>
        public string RemoteLifecycleEventsLogLevel { get; set; }

        /// <summary>
        /// Gets or sets the dispatcher ID used by remoting actors when configured.
        /// </summary>
        public string Dispatcher { get; set; }

        /// <summary>
        /// Gets or sets the timeout for graceful remoting shutdown.
        /// </summary>
        public TimeSpan ShutdownTimeout { get; set; }

        /// <summary>
        /// Gets or sets how long endpoint writers wait to flush messages during shutdown.
        /// </summary>
        public TimeSpan FlushWait { get; set; }

        /// <summary>
        /// The fully-qualified HOCON config paths of the enabled classic remoting transports,
        /// read from <c>akka.remote.enabled-transports</c> (default:
        /// <c>akka.remote.dot-netty.tcp</c>). Only governs classic remoting -- the Artery
        /// transport is switched on separately via <c>akka.remote.artery.enabled</c> and
        /// carries its own configuration block.
        /// </summary>
        public IList<string> TransportNames { get; set; }

        /// <summary>
        /// Gets or sets the configuration for classic transport adapters.
        /// </summary>
        public IDictionary<string, string> Adapters { get; set; }

        /// <summary>
        /// The <see cref="TransportSettings"/> resolved from each config block named in
        /// <see cref="TransportNames"/>; classic remoting instantiates one transport driver
        /// per entry.
        /// </summary>
        public TransportSettings[] Transports { get; set; }
        /// <summary>
        /// Gets or sets the backoff interval used when retrying transport operations.
        /// </summary>
        public TimeSpan BackoffPeriod { get; set; }
        /// <summary>
        /// Gets or sets how long a failed remote address remains gated before another connection attempt is allowed.
        /// </summary>
        public TimeSpan RetryGateClosedFor { get; set; }
        /// <summary>
        /// Gets or sets whether an inbound connection may also be used for outbound writes when no writable endpoint exists.
        /// </summary>
        public bool UsePassiveConnections { get; set; }
        /// <summary>
        /// Gets or sets the maximum number of unacknowledged system messages retained for an endpoint.
        /// </summary>
        public int SysMsgBufferSize { get; set; }
        /// <summary>
        /// Gets or sets the maximum number of buffered system messages sent in one resend attempt.
        /// </summary>
        public int SysResendLimit { get; set; }
        /// <summary>
        /// Gets or sets the interval between retries for unacknowledged system messages.
        /// </summary>
        public TimeSpan SysResendTimeout { get; set; }
        /// <summary>
        /// Gets or sets the period allowed to deliver pending system messages before the association is abandoned.
        /// </summary>
        public TimeSpan InitialSysMsgDeliveryTimeout { get; set; }
        /// <summary>
        /// Gets or sets how long an endpoint may remain without system-message activity before it is quarantined.
        /// </summary>
        public TimeSpan QuarantineSilentSystemTimeout { get; set; }
        /// <summary>
        /// Gets or sets the timeout used to decide when to send a standalone acknowledgement for system messages.
        /// </summary>
        public TimeSpan SysMsgAckTimeout { get; set; }
        /// <summary>
        /// Gets or sets how long quarantine markers are retained before they are pruned; <c>null</c> disables pruning.
        /// </summary>
        public TimeSpan? QuarantineDuration { get; set; }
        /// <summary>
        /// Gets or sets how long remoting startup waits for its transport to become available.
        /// </summary>
        public TimeSpan StartupTimeout { get; set; }
        /// <summary>
        /// Gets or sets the timeout for transport management command acknowledgements.
        /// </summary>
        public TimeSpan CommandAckTimeout { get; set; }

        /// <summary>
        /// Gets or sets the configuration for the failure detector used by remote death watch.
        /// </summary>
        public Config WatchFailureDetectorConfig { get; set; }
        /// <summary>
        /// Gets or sets the configured failure detector implementation class name for remote death watch.
        /// </summary>
        public string WatchFailureDetectorImplementationClass { get; set; }
        /// <summary>
        /// Gets or sets the interval at which the remote watcher sends heartbeats.
        /// </summary>
        public TimeSpan WatchHeartBeatInterval { get; set; }
        /// <summary>
        /// Gets or sets how often the remote watcher checks for unreachable nodes.
        /// </summary>
        public TimeSpan WatchUnreachableReaperInterval { get; set; }
        /// <summary>
        /// Gets or sets the expected response delay used by the remote watch failure detector.
        /// </summary>
        public TimeSpan WatchHeartbeatExpectedResponseAfter { get; set; }

        private Config TransportConfigFor(string transportName)
        {
            return Config.GetConfig(transportName);
        }

        /// <summary>
        /// Applies the configured remoting dispatcher to actor properties when one is set.
        /// </summary>
        /// <param name="props">The actor properties to configure.</param>
        /// <returns>The properties with the configured dispatcher, or the original properties when no dispatcher is configured.</returns>
        public Props ConfigureDispatcher(Props props)
        {
            return String.IsNullOrEmpty(Dispatcher) 
                ? props 
                : props.WithDispatcher(Dispatcher);
        }

        /// <summary>
        /// Configuration for one enabled classic remoting transport.
        /// </summary>
        public class TransportSettings
        {
            /// <summary>
            /// Creates transport settings from a transport configuration block.
            /// </summary>
            /// <param name="config">The HOCON configuration for the transport and its adapters.</param>
            public TransportSettings(Config config)
            {
                if (config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<TransportSettings>();

                TransportClass = config.GetString("transport-class", null);
                Adapters = config.GetStringList("applied-adapters", new string[] { }).Reverse().ToList();
                Config = config;
            }

            /// <summary>
            /// Gets or sets the HOCON configuration for this transport.
            /// </summary>
            public Config Config { get; set; }

            /// <summary>
            /// Gets or sets the configured adapter names for this transport.
            /// </summary>
            public IList<string> Adapters { get; set; }

            /// <summary>
            /// Gets or sets the fully qualified type name of the transport implementation.
            /// </summary>
            public string TransportClass { get; set; }
        }

        private static IDictionary<string, string> ConfigToMap(Config cfg)
        {
            // adjusted API to match stand-alone HOCON per https://github.com/akkadotnet/HOCON/pull/191#issuecomment-577455865
            if (cfg.IsEmpty) return new Dictionary<string, string>();
            var unwrapped = cfg.Root.GetObject().Unwrapped;
            return unwrapped.ToDictionary(k => k.Key, v => v.Value != null ? v.Value.ToString() : null);
        }
    }
}

