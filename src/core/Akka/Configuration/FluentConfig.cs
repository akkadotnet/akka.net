using Akka.Configuration;
using Akka.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka
{
    public class FluentConfig : FluentConfigInternals
    {
        private StringBuilder _hoconConfiguration = new StringBuilder();

        void FluentConfigInternals.AppendLine(string configString)
        {
            _hoconConfiguration.AppendLine(configString);
        }

        public static FluentConfig Begin()
        {
            return new FluentConfig();
        }

        public Config Build()
        {
            return ConfigurationFactory.ParseString(_hoconConfiguration.ToString());
        }
    }

    public interface FluentConfigInternals
    {
        void AppendLine(string configString);
    }

    public static class FluentConfigLogging
    {
        /*
    log-config-on-start = on
    stdout-loglevel = DEBUG
    loglevel = ERROR
    actor {
        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
        
        debug {  
          receive = on 
          autoreceive = on
          lifecycle = on
          event-stream = on
          unhandled = on
        }
*/
        public static FluentConfig LogConfigOnStart(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.log-config-on-start = on");
            }
            return self;
        }
        public static FluentConfig StdOutLogLevel(this FluentConfig self, LogLevel logLevel)
        {
            self.AsInstanceOf<FluentConfigInternals>().AppendLine(string.Format("akka.stdout-loglevel = {0}", logLevel.StringFor()));

            return self;
        }

        public static FluentConfig LogLevel(this FluentConfig self, LogLevel logLevel)
        {
            self.AsInstanceOf<FluentConfigInternals>().AppendLine(string.Format("akka.loglevel = {0}", logLevel.StringFor()));

            return self;
        }
        public static FluentConfig DebugReceive(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.actor.debug.receive = on");
            }
            return self;
        }
        public static FluentConfig DebugAutoReceive(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.actor.debug.autoreceive = on");
            }
            return self;
        }
        public static FluentConfig DebugLifecycle(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.actor.debug.lifecycle = on");
            }
            return self;
        }
        public static FluentConfig DebugEventStream(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.actor.debug.event-stream = on");
            }
            return self;
        }
        public static FluentConfig DebugUnhandled(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.actor.debug.unhandled = on");
            }
            return self;
        }
    }
}
