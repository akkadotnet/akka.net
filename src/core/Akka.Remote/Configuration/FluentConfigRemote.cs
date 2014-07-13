using Akka.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka
{
    public static class FluentConfigRemote
    {
        /*
remote {
		log-received-messages = on
		log-sent-messages = on
        #log-remote-lifecycle-events = on

        #this is the new upcoming remoting support, which enables multiple transports
       helios.tcp {
            transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
		    applied-adapters = []
		    transport-protocol = tcp
		    port = 8081
		    hostname = 0.0.0.0 #listens on ALL ips for this machine
            public-hostname = localhost #but only accepts connections on localhost (usually 127.0.0.1)
        }
        log-remote-lifecycle-events = INFO
    }*/
        public static FluentConfig StartRemotingOn(this FluentConfig self, string hostname)
        {
            return self.StartRemotingOn(hostname, 0);
        }
        public static FluentConfig StartRemotingOn(this FluentConfig self, string hostname,int port)
        {
            string remoteConfig = @"
akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
akka.remote.helios.tcp.transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
akka.remote.helios.tcp.applied-adapters = []
akka.remote.helios.tcp.transport-protocol = tcp
akka.remote.helios.tcp.port = {0}
akka.remote.helios.tcp.hostname = 0.0.0.0 #listens on ALL ips for this machine
akka.remote.helios.tcp.public-hostname = {1} #but only accepts connections on localhost (usually 127.0.0.1)
";
            self.AsInstanceOf<FluentConfigInternals>().AppendLine(string.Format(remoteConfig,port,hostname));

            return self;
        }
        private static FluentConfig LogRemoteLifecycleEvents(this FluentConfig self, LogLevel logLevel)
        {
            self.AsInstanceOf<FluentConfigInternals>().AppendLine(string.Format("akka.remote.log-remote-lifecycle-events = {0}", logLevel.StringFor()));

            return self;
        }
        private static FluentConfig LogReceivedMessages(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.remote.log-received-messages = on");
            }
            return self;
        }
        private static FluentConfig LogSentMessages(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.remote.log-sent-messages = on");
            }
            return self;
        }
        public static FluentConfig LogRemote(this FluentConfig self,LogLevel lifecycleEvents=LogLevel.DebugLevel,bool receivedMessages=false,bool sentMessages=false)
        {
            return self
                .LogRemoteLifecycleEvents(lifecycleEvents)
                .LogReceivedMessages(receivedMessages)
                .LogSentMessages(sentMessages);
        }

    }
}
