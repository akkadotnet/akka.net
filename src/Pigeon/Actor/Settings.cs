using Akka.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public class Settings
    {        
        public Settings(ActorSystem system, Config config)
        {
            var fallback = ConfigurationFactory.Default();

            var merged =  config == null ? fallback : new Config(config, fallback);

            this.System = system;
            this.Config = merged;

            this.ConfigVersion = Config.GetString("akka.version");
            this.ProviderClass = Config.GetString("akka.actor.provider");
            this.SupervisorStrategyClass = Config.GetString("akka.actor.guardian-supervisor-strategy");

            this.CreationTimeout = Config.GetMillisDuration("akka.actor.creation-timeout");
            this.UnstartedPushTimeout = Config.GetMillisDuration("akka.actor.unstarted-push-timeout");

            this.SerializeAllMessages = Config.GetBoolean("akka.actor.serialize-messages");
            this.SerializeAllCreators = Config.GetBoolean("akka.actor.serialize-creators");

            this.LogLevel = Config.GetString("akka.loglevel");
            this.StdoutLogLevel = Config.GetString("akka.stdout-loglevel");
            this.Loggers = Config.GetStringList("akka.loggers");

            this.LoggerStartTimeout = Config.GetMillisDuration("akka.logger-startup-timeout");

            //handled
            this.LogConfigOnStart = Config.GetBoolean("akka.log-config-on-start");
            this.LogDeadLetters = 0;
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
            this.LogDeadLettersDuringShutdown = Config.GetBoolean("akka.log-dead-letters-during-shutdown");
            this.AddLoggingReceive = Config.GetBoolean("akka.actor.debug.receive");
            this.DebugAutoReceive = Config.GetBoolean("akka.actor.debug.autoreceive");
            this.DebugLifecycle = Config.GetBoolean("akka.actor.debug.lifecycle");
            this.DebugEventStream = Config.GetBoolean("akka.actor.debug.event-stream");
            this.DebugUnhandledMessage = Config.GetBoolean("akka.actor.debug.unhandled");
            this.DebugRouterMisConfiguration = Config.GetBoolean("akka.actor.debug.router-misConfiguration");
            this.Home = Config.GetString("akka.home") ?? "";

            //TODO: dunno.. we dont have FiniteStateMachines, dont know what the rest is
            /*              
                final val FsmDebugEvent: Boolean = getBoolean("akka.actor.debug.fsm")
                final val SchedulerClass: String = getString("akka.scheduler.implementation")
                final val Daemonicity: Boolean = getBoolean("akka.daemonic")                
                final val DefaultVirtualNodesFactor: Int = getInt("akka.actor.deployment.default.virtual-nodes-factor")
             */
        }

        public ActorSystem System { get; private set; }
        public Config Config { get;private set; }

        public string ConfigVersion { get;private set; }
        public string ProviderClass { get; private set; }
        public string SupervisorStrategyClass { get; private set; }
        public bool SerializeAllMessages { get;private set; }
        public bool SerializeAllCreators { get; private set; }
        public TimeSpan CreationTimeout { get;private set; }
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
