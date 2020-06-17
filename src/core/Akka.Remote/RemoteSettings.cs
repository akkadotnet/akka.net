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
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<RemoteSettings>();

            Config = config;

            Artery = new ArterySettings(Config.GetConfig("akka.remote.artery"));
            WarnAboutDirectUse = Config.GetBoolean("akka.remote.warn-about-direct-use");

            LogReceive = config.GetBoolean("akka.remote.log-received-messages");
            LogSend = config.GetBoolean("akka.remote.log-sent-messages");

            LogFrameSizeExceeding =
                config.GetString("akka.remote.log-frame-size-exceeding").ToLowerInvariant() == "off" ?
                null :
                (int?) config.GetByteSize("akka.remote.log-frame-size-exceeding");

            UntrustedMode = config.GetBoolean("akka.remote.untrusted-mode");

            TrustedSelectionPaths = config
                .GetStringList("akka.remote.trusted-selection-paths")
                .ToImmutableHashSet();

            var logRemoteLifecycleEvents = Config
                .GetString("akka.remote.log-remote-lifecycle-events")
                .ToLowerInvariant();
            switch (logRemoteLifecycleEvents)
            {
                case "on":
                case "true":
                case "yes":
                    RemoteLifecycleEventsLogLevel = "DEBUG";
                    break;
                case "off":
                case "false":
                case "no":
                case "debug":
                case "info":
                case "warning":
                case "error":
                    RemoteLifecycleEventsLogLevel = logRemoteLifecycleEvents.ToUpper();
                    break;
                default:
                    throw new ConfigurationException("Logging level must be one of (on, off, debug, info, warning, error)");
            }

            Dispatcher = Config.GetString("akka.remote.use-dispatcher");

            ShutdownTimeout = Config
                .GetTimeSpan("akka.remote.shutdown-timeout")
                .Requiring(ts => ts > TimeSpan.Zero, "shutdown-timeout must be > 0");

            FlushWait = Config
                .GetTimeSpan("akka.remote.flush-wait-on-shutdown")
                .Requiring(ts => ts > TimeSpan.Zero, "flush-wait-on-shutdown must be > 0");

            StartupTimeout = Config
                .GetTimeSpan("akka.remote.startup-timeout")
                .Requiring(ts => ts > TimeSpan.Zero, "startup-timeout must be > 0");

            RetryGateClosedFor = Config
                .GetTimeSpan("akka.remote.retry-gate-closed-for")
                .Requiring(ts => ts > TimeSpan.Zero, "retry-gate-closed-for must be > 0");

            UsePassiveConnections = Config.GetBoolean("akka.remote.use-passive-connections");

            BackoffPeriod = Config
                .GetTimeSpan("akka.remote.backoff-interval")
                .Requiring(ts => ts > TimeSpan.Zero, "backoff-interval must be > 0");

            var logBufferSizeExceeding = Config
                .GetString("akka.remote.log-buffer-size-exceeding")
                .ToLowerInvariant();
            switch(logBufferSizeExceeding)
            {
                case "off":
                case "false":
                case "no":
                    LogBufferSizeExceeding = int.MaxValue;
                    break;
                default:
                    LogBufferSizeExceeding = Config.GetInt("akka.remote.log-buffer-size-exceeding");
                    break;
            }

            SysMsgAckTimeout = Config
                .GetTimeSpan("akka.remote.system-message-ack-piggyback-timeout")
                .Requiring(ts => ts > TimeSpan.Zero, "system-message-ack-piggyback-timeout must be > 0");

            SysResendTimeout = Config
                .GetTimeSpan("akka.remote.resend-interval")
                .Requiring(ts => ts > TimeSpan.Zero, "resend-interval must be > 0");

            SysResendLimit = Config
                .GetInt("akka.remote.resend-limit")
                .Requiring(i => i > 0, "resend-limit must be > 0");

            SysMsgBufferSize = Config
                .GetInt("akka.remote.system-message-buffer-size")
                .Requiring(i => i > 0, "system-message-buffer-size must be > 0");

            InitialSysMsgDeliveryTimeout = Config
                .GetTimeSpan("akka.remote.initial-system-message-delivery-timeout")
                .Requiring(ts => ts > TimeSpan.Zero, "initial-system-message-delivery-timeout must be > 0");

            var quarantineSilentSystemTimeout = Config
                .GetString("akka.remote.quarantine-after-silence")
                .ToLowerInvariant();
            switch(quarantineSilentSystemTimeout)
            {
                case "off":
                case "false":
                case "no":
                    QuarantineSilentSystemTimeout = TimeSpan.Zero;
                    break;
                default:
                    QuarantineSilentSystemTimeout = Config
                        .GetTimeSpan("akka.remote.quarantine-after-silence")
                        .Requiring(ts => ts > TimeSpan.Zero, "quarantine-after-silence must be > 0");
                    break;
            }

            QuarantineDuration = Config
                .GetTimeSpan("akka.remote.prune-quarantine-marker-after")
                .Requiring(ts => ts > TimeSpan.Zero, "prune-quarantine-marker-after must be > 0");

            CommandAckTimeout = Config
                .GetTimeSpan("akka.remote.command-ack-timeout")
                .Requiring(ts => ts > TimeSpan.Zero, "command-ack-timeout must be > 0");

            UseUnsafeRemoteFeaturesWithoutCluster = Config.GetBoolean("akka.remote.use-unsafe-remote-features-outside-cluster");

            WarnUnsafeWatchWithoutCluster = Config.GetBoolean("akka.remote.warn-unsafe-watch-outside-cluster");

            WatchFailureDetectorConfig = Config.GetConfig("akka.remote.watch-failure-detector");

            WatchFailureDetectorImplementationClass = WatchFailureDetectorConfig.GetString("implementation-class");

            WatchHeartBeatInterval = WatchFailureDetectorConfig
                .GetTimeSpan("heartbeat-interval")
                .Requiring(ts => ts > TimeSpan.Zero, "watch-failure-detector.heartbeat-interval must be > 0");

            WatchUnreachableReaperInterval = WatchFailureDetectorConfig
                .GetTimeSpan("unreachable-nodes-reaper-interval")
                .Requiring(ts => ts > TimeSpan.Zero, "watch-failure-detector.unreachable-nodes-reaper-interval must be > 0");

            WatchHeartbeatExpectedResponseAfter = WatchFailureDetectorConfig
                .GetTimeSpan("expected-response-after")
                .Requiring(ts => ts > TimeSpan.Zero, "watch-failure-detector.expected-response-after must be > 0");

            TransportNames = Config.GetStringList("akka.remote.enabled-transports").ToImmutableList();

            Transports = (from name in TransportNames
                          let transportConfig = TransportConfigFor(name)
                          select new TransportSettings(transportConfig)).ToImmutableList();

            Adapters = ConfigToMap(Config.GetConfig("akka.remote.adapters")).ToImmutableDictionary();
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
        public bool LogReceive { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool LogSend { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int? LogFrameSizeExceeding { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool UntrustedMode { get; }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        // ARTERY: NOTE: this is very dangerous, there are TWO ways to retrieve UntrustedMode, the one above is, I assume, a backward compatibility property.
        internal bool GetUntrustedMode {
            get => Artery.Enabled ? Artery.UntrustedMode : UntrustedMode;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableHashSet<string> TrustedSelectionPaths { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public string RemoteLifecycleEventsLogLevel { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public string Dispatcher { get; }

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
        public TimeSpan ShutdownTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan FlushWait { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan StartupTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan RetryGateClosedFor { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool UsePassiveConnections { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan BackoffPeriod { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int LogBufferSizeExceeding { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan SysMsgAckTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan SysResendTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int SysResendLimit { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int SysMsgBufferSize { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan InitialSysMsgDeliveryTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan QuarantineSilentSystemTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan? QuarantineDuration { get; }

        /// <summary>
        /// TBD
        /// </summary>
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

        private static IDictionary<string, string> ConfigToMap(Config cfg)
        {
            // adjusted API to match stand-alone HOCON per https://github.com/akkadotnet/HOCON/pull/191#issuecomment-577455865
            if (cfg.IsEmpty) return new Dictionary<string, string>();
            var unwrapped = cfg.Root.GetObject().Unwrapped;
            return unwrapped.ToDictionary(k => k.Key, v => v.Value?.ToString());
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
    }
}

