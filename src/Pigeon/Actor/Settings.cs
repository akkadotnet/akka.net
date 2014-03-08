using System;
using System.Collections.Generic;
using Akka.Configuration;

namespace Akka.Actor
{
    /// <summary>
    ///     Settings are the overall ActorSystem Settings which also provides a convenient access to the Config object.
    ///     For more detailed information about the different possible configuration options, look in the Akka .NET
    ///     Documentation under "Configuration"
    /// </summary>
    public class Settings
    {
        public Settings(ActorSystem system, Config config)
        {
            Config fallback = ConfigurationFactory.Default();

            Config merged = config == null ? fallback : new Config(config, fallback);

            System = system;
            Config = merged;

            ConfigVersion = Config.GetString("akka.version");
            ProviderClass = Config.GetString("akka.actor.provider");
            SupervisorStrategyClass = Config.GetString("akka.actor.guardian-supervisor-strategy");

            CreationTimeout = Config.GetMillisDuration("akka.actor.creation-timeout");
            UnstartedPushTimeout = Config.GetMillisDuration("akka.actor.unstarted-push-timeout");

            SerializeAllMessages = Config.GetBoolean("akka.actor.serialize-messages");
            SerializeAllCreators = Config.GetBoolean("akka.actor.serialize-creators");

            LogLevel = Config.GetString("akka.loglevel");
            StdoutLogLevel = Config.GetString("akka.stdout-loglevel");
            Loggers = Config.GetStringList("akka.loggers");

            LoggerStartTimeout = Config.GetMillisDuration("akka.logger-startup-timeout");

            //handled
            LogConfigOnStart = Config.GetBoolean("akka.log-config-on-start");
            LogDeadLetters = 0;
            switch (Config.GetString("akka.log-dead-letters"))
            {
                case "on":
                case "true":
                    LogDeadLetters = int.MaxValue;
                    break;
                case "off":
                case "false":
                    LogDeadLetters = 0;
                    break;
                default:
                    LogDeadLetters = Config.GetInt("akka.log-dead-letters");
                    break;
            }
            LogDeadLettersDuringShutdown = Config.GetBoolean("akka.log-dead-letters-during-shutdown");
            AddLoggingReceive = Config.GetBoolean("akka.actor.debug.receive");
            DebugAutoReceive = Config.GetBoolean("akka.actor.debug.autoreceive");
            DebugLifecycle = Config.GetBoolean("akka.actor.debug.lifecycle");
            DebugEventStream = Config.GetBoolean("akka.actor.debug.event-stream");
            DebugUnhandledMessage = Config.GetBoolean("akka.actor.debug.unhandled");
            DebugRouterMisConfiguration = Config.GetBoolean("akka.actor.debug.router-misConfiguration");
            Home = Config.GetString("akka.home") ?? "";

            //TODO: dunno.. we dont have FiniteStateMachines, dont know what the rest is
            /*              
                final val FsmDebugEvent: Boolean = getBoolean("akka.actor.debug.fsm")
                final val SchedulerClass: String = getString("akka.scheduler.implementation")
                final val Daemonicity: Boolean = getBoolean("akka.daemonic")                
                final val DefaultVirtualNodesFactor: Int = getInt("akka.actor.deployment.default.virtual-nodes-factor")
             */
        }

        public ActorSystem System { get; private set; }
        public Config Config { get; private set; }

        public string ConfigVersion { get; private set; }
        public string ProviderClass { get; private set; }
        public string SupervisorStrategyClass { get; private set; }
        public bool SerializeAllMessages { get; private set; }
        public bool SerializeAllCreators { get; private set; }
        public TimeSpan CreationTimeout { get; private set; }
        public TimeSpan UnstartedPushTimeout { get; private set; }
        public string LogLevel { get; private set; }
        public string StdoutLogLevel { get; private set; }
        public IList<string> Loggers { get; private set; }
        public TimeSpan LoggerStartTimeout { get; private set; }
        public bool LogConfigOnStart { get; private set; }
        public int LogDeadLetters { get; private set; }
        public bool LogDeadLettersDuringShutdown { get; private set; }
        public bool AddLoggingReceive { get; private set; }
        public bool DebugAutoReceive { get; private set; }
        public bool DebugEventStream { get; private set; }
        public bool DebugUnhandledMessage { get; private set; }
        public bool DebugRouterMisconfiguration { get; private set; }
        public string Home { get; private set; }
        public bool DebugRouterMisConfiguration { get; private set; }
        public bool DebugLifecycle { get; private set; }

        public override string ToString()
        {
            return Config.ToString();
        }
    }
}