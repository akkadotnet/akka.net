using System;
using System.Text;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;

// ReSharper disable once CheckNamespace
namespace Akka
{
    public class FluentConfig : FluentConfigInternals
    {
        private readonly StringBuilder _hoconConfiguration = new StringBuilder();

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
                ((FluentConfigInternals) self).AppendLine("akka.log-config-on-start = on");
            }
            return self;
        }

        public static FluentConfig StdOutLogLevel(this FluentConfig self, LogLevel logLevel)
        {
            self.AsInstanceOf<FluentConfigInternals>()
                .AppendLine(string.Format("akka.stdout-loglevel = {0}", logLevel.StringFor()));

            return self;
        }

        public static FluentConfig LogLevel(this FluentConfig self, LogLevel logLevel)
        {
            self.AsInstanceOf<FluentConfigInternals>()
                .AppendLine(string.Format("akka.loglevel = {0}", logLevel.StringFor()));

            return self;
        }

        private static FluentConfig DebugReceive(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.actor.debug.receive = on");
            }
            return self;
        }

        private static FluentConfig DebugAutoReceive(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.actor.debug.autoreceive = on");
            }
            return self;
        }

        private static FluentConfig DebugLifecycle(this FluentConfig self, bool on)
        {
            if (on)
            {
                self.AsInstanceOf<FluentConfigInternals>().AppendLine("akka.actor.debug.lifecycle = on");
            }
            return self;
        }

        private static FluentConfig DebugEventStream(this FluentConfig self, bool on)
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

        public static FluentConfig LogLocal(this FluentConfig self, bool receive = false, bool autoReceive = false,
            bool lifecycle = false, bool eventStream = false, bool unhandled = false)
        {
            return self.DebugReceive(receive)
                .DebugAutoReceive(autoReceive)
                .DebugLifecycle(lifecycle)
                .DebugEventStream(eventStream)
                .DebugUnhandled(unhandled);
        }

        public static FluentConfig DefaultDispatcher(this FluentConfig self, DispatcherType dispatcherType,
            int throughput = 100, TimeSpan? throughputDeadlineTimeout = null)
        {
            string type = dispatcherType.GetName();
            self.ConfigureDispatcher(type, throughput, throughputDeadlineTimeout);

            return self;
        }

        public static FluentConfig DefaultDispatcher(this FluentConfig self, Type dispatcherType, int throughput = 100,
            TimeSpan? throughputDeadlineTimeout = null)
        {
            string type = dispatcherType.AssemblyQualifiedName;
            self.ConfigureDispatcher(type, throughput, throughputDeadlineTimeout);
            return self;
        }

        private static void ConfigureDispatcher(this FluentConfig self, string type, int throughput,
            TimeSpan? throughputDeadlineTimeout)
        {
            self.AsInstanceOf<FluentConfigInternals>()
                .AppendLine(string.Format("akka.actor.default-dispatcher.type = '{0}'", type));
            self.AsInstanceOf<FluentConfigInternals>()
                .AppendLine(string.Format("akka.actor.default-dispatcher.throughput = {0}", throughput));
            self.AsInstanceOf<FluentConfigInternals>()
                .AppendLine(string.Format("akka.actor.default-dispatcher.throughput-deadline-time = {0}ms",
                    throughputDeadlineTimeout.GetValueOrDefault(TimeSpan.FromSeconds(0)).TotalMilliseconds));
        }
    }
}