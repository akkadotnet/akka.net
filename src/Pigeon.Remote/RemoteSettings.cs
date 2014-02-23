using Pigeon.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Remote
{
    public class RemoteSettings
    {
        public class TransportSettings
        {
            public TransportSettings(Config config)
            {
                this.TransportClass = config.GetString("transport-class");
                this.Config = config;
            }

            public Config Config { get; set; }

            public string TransportClass { get; set; }
        }

        public RemoteSettings(Configuration.Config config)
        {

            this.Config = config;
            LogReceive = config.GetBoolean("akka.remote.log-received-messages");
            LogSend = config.GetBoolean("akka.remote.log-sent-messages");
            UntrustedMode = config.GetBoolean("akka.remote.untrusted-mode");
            TrustedSelectionPaths = new HashSet<string>(config.GetStringList("akka.remote.trusted-selection-paths"));
            RemoteLifecycleEventsLogLevel = config.GetString("akka.remote.log-remote-lifecycle-events");
            ShutdownTimeout = config.GetMillisDuration("akka.remote.shutdown-timeout");
            TransportNames = config.GetStringList("akka.remote.enabled-transports");
            Transports = (from transportName in TransportNames
                          let transportConfig = TransportConfigFor(transportName)
                          select new TransportSettings(transportConfig)).ToArray();

        }

        private Config TransportConfigFor(string transportName)
        {
            return Config.GetConfig(transportName);
        }

        public Config Config { get;private set; }

        public HashSet<string> TrustedSelectionPaths { get; set; }

        public bool UntrustedMode { get; set; }

        public bool LogSend { get; set; }

        public bool LogReceive { get; set; }

        public string RemoteLifecycleEventsLogLevel { get; set; }

        public TimeSpan ShutdownTimeout { get; set; }

        public IList<string> TransportNames { get; set; }

        public TransportSettings[] Transports { get; set; }
    }
}
