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
            UntrustedMode = config.GetBoolean("akka.remote.untrusted-mode");
            TrustedSelectionPaths = new HashSet<string>(config.GetStringList("akka.remote.trusted-selection-paths"));
            RemoteLifecycleEventsLogLevel = config.GetString("akka.remote.log-remote-lifecycle-events") ?? "DEBUG";
            FlushWait = config.GetMillisDuration("akka.remote.flush-wait-on-shutdown");
            ShutdownTimeout = config.GetMillisDuration("akka.remote.shutdown-timeout");
            TransportNames = config.GetStringList("akka.remote.enabled-transports");
            Transports = (from transportName in TransportNames
                let transportConfig = TransportConfigFor(transportName)
                select new TransportSettings(transportConfig)).ToArray();
            Adapters = ConfigToMap(config.GetConfig("akka.remote.adapters"));
            BackoffPeriod = config.GetMillisDuration("akka.remote.backoff-internal", TimeSpan.FromSeconds(3));
            RetryGateClosedFor = config.GetMillisDuration("akka.remote.retry-gate-closed-for", TimeSpan.Zero);
            UsePassiveConnections = config.GetBoolean("akka.remote.use-passive-connections");
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

        public string RemoteLifecycleEventsLogLevel { get; set; }

        public TimeSpan ShutdownTimeout { get; set; }

        public TimeSpan FlushWait { get; set; }

        public IList<string> TransportNames { get; set; }

        public IDictionary<string, string> Adapters { get; set; }

        public TransportSettings[] Transports { get; set; }
        public TimeSpan BackoffPeriod { get; set; }
        public TimeSpan RetryGateClosedFor { get; set; }
        public bool UsePassiveConnections { get; set; }

        private Config TransportConfigFor(string transportName)
        {
            return Config.GetConfig(transportName);
        }

        public class TransportSettings
        {
            public TransportSettings(Config config)
            {
                TransportClass = config.GetString("transport-class");
                Config = config;
            }

            public Config Config { get; set; }

            public string TransportClass { get; set; }
        }

        private static IDictionary<string, string> ConfigToMap(Config cfg)
        {
            if(cfg.IsEmpty) return new Dictionary<string, string>();
            return cfg.Root.GetObject().Unwrapped.ToDictionary(k => k.Key, v => v.Value != null? v.Value.ToString():null);
        }
    }
}