//-----------------------------------------------------------------------
// <copyright file="RemoteSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Util;

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
        /// TBD
        /// </summary>
        public Config Config { get; }

        internal ArterySettings Artery { get; }

        public bool WarnAboutDirectUse { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public bool LogReceive { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public bool LogSend { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public int? LogFrameSizeExceeding { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public bool UntrustedMode { get; }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        // ARTERY: NOTE: this is very dangerous, there are TWO ways to retrieve UntrustedMode, the one above is, I assume, a backward compatibility property.
        [Obsolete]
        internal bool GetUntrustedMode {
            get => Artery.Enabled ? Artery.UntrustedMode : UntrustedMode;
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public ImmutableHashSet<string> TrustedSelectionPaths { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public string RemoteLifecycleEventsLogLevel { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public string Dispatcher { get; }

        [Obsolete("deprecated")]
        public Props ConfigureDispatcher(Props props)
        {
            if(Artery.Enabled)
            {
                return Artery.Advanced.Dispatcher.Count() == 0 ? 
                    props : props.WithDispatcher(Artery.Advanced.Dispatcher);
            }
            else
            {
                return Dispatcher.Count() == 0 ? props : props.WithDispatcher(Dispatcher);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan ShutdownTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan FlushWait { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan StartupTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan RetryGateClosedFor { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public bool UsePassiveConnections { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan BackoffPeriod { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public int LogBufferSizeExceeding { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan SysMsgAckTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan SysResendTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public int SysResendLimit { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public int SysMsgBufferSize { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan InitialSysMsgDeliveryTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan QuarantineSilentSystemTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan? QuarantineDuration { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Classic remoting is deprecated, use Artery")]
        public TimeSpan CommandAckTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool UseUnsafeRemoteFeaturesWithoutCluster { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool WarnUnsafeWatchWithoutCluster { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public Config WatchFailureDetectorConfig { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public string WatchFailureDetectorImplementationClass { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan WatchHeartBeatInterval { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan WatchUnreachableReaperInterval { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan WatchHeartbeatExpectedResponseAfter { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableList<TransportSettings> Transports { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableDictionary<string, string> Adapters { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableList<string> TransportNames { get; }

        /// <summary>
        /// TBD
        /// </summary>
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
                if (config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<TransportSettings>();

                TransportClass = config.GetString("transport-class", null);
                Adapters = config.GetStringList("applied-adapters", new string[] { }).Reverse().ToImmutableList();
                Config = config;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Config Config { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public ImmutableList<string> Adapters { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public string TransportClass { get; }
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

