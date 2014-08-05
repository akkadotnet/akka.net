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
        /// <summary>
        ///     Initializes a new instance of the <see cref="Settings" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="config">The configuration.</param>
        public Settings(ActorSystem system, Config config)
        {
            Config fallback = ConfigurationFactory.Default();

            Config merged = config == null ? fallback : new Config(config, fallback);

            System = system;
            Config = merged;

            ConfigVersion = Config.GetString("akka.version");
            ProviderClass = Config.GetString("akka.actor.provider");
            var providerType = Type.GetType(ProviderClass);
            if (providerType == null)
                throw new ConfigurationException(string.Format("'akka.actor.provider' is not a valid type name : '{0}'", ProviderClass));
            if (!typeof(ActorRefProvider).IsAssignableFrom(providerType))
                throw new ConfigurationException(string.Format("'akka.actor.provider' is not a valid actor ref provider: '{0}'", ProviderClass));
            
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
            DebugRouterMisConfiguration = Config.GetBoolean("akka.actor.debug.router-misconfiguration");
            Home = Config.GetString("akka.home") ?? "";

            //TODO: dunno.. we dont have FiniteStateMachines, dont know what the rest is
            /*              
                final val FsmDebugEvent: Boolean = getBoolean("akka.actor.debug.fsm")
                final val SchedulerClass: String = getString("akka.scheduler.implementation")
                final val Daemonicity: Boolean = getBoolean("akka.daemonic")                
                final val DefaultVirtualNodesFactor: Int = getInt("akka.actor.deployment.default.virtual-nodes-factor")
             */
        }

        /// <summary>
        ///     Gets the system.
        /// </summary>
        /// <value>The system.</value>
        public ActorSystem System { get; private set; }

        /// <summary>
        ///     Gets the configuration.
        /// </summary>
        /// <value>The configuration.</value>
        public Config Config { get; private set; }

        /// <summary>
        ///     Gets the configuration version.
        /// </summary>
        /// <value>The configuration version.</value>
        public string ConfigVersion { get; private set; }

        /// <summary>
        ///     Gets the provider class.
        /// </summary>
        /// <value>The provider class.</value>
        public string ProviderClass { get; private set; }

        /// <summary>
        ///     Gets the supervisor strategy class.
        /// </summary>
        /// <value>The supervisor strategy class.</value>
        public string SupervisorStrategyClass { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [serialize all messages].
        /// </summary>
        /// <value><c>true</c> if [serialize all messages]; otherwise, <c>false</c>.</value>
        public bool SerializeAllMessages { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [serialize all creators].
        /// </summary>
        /// <value><c>true</c> if [serialize all creators]; otherwise, <c>false</c>.</value>
        public bool SerializeAllCreators { get; private set; }

        /// <summary>
        ///     Gets the creation timeout.
        /// </summary>
        /// <value>The creation timeout.</value>
        public TimeSpan CreationTimeout { get; private set; }

        /// <summary>
        ///     Gets the unstarted push timeout.
        /// </summary>
        /// <value>The unstarted push timeout.</value>
        public TimeSpan UnstartedPushTimeout { get; private set; }

        /// <summary>
        ///     Gets the log level.
        /// </summary>
        /// <value>The log level.</value>
        public string LogLevel { get; private set; }

        /// <summary>
        ///     Gets the stdout log level.
        /// </summary>
        /// <value>The stdout log level.</value>
        public string StdoutLogLevel { get; private set; }

        /// <summary>
        ///     Gets the loggers.
        /// </summary>
        /// <value>The loggers.</value>
        public IList<string> Loggers { get; private set; }

        /// <summary>
        ///     Gets the logger start timeout.
        /// </summary>
        /// <value>The logger start timeout.</value>
        public TimeSpan LoggerStartTimeout { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [log configuration on start].
        /// </summary>
        /// <value><c>true</c> if [log configuration on start]; otherwise, <c>false</c>.</value>
        public bool LogConfigOnStart { get; private set; }

        /// <summary>
        ///     Gets the log dead letters.
        /// </summary>
        /// <value>The log dead letters.</value>
        public int LogDeadLetters { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [log dead letters during shutdown].
        /// </summary>
        /// <value><c>true</c> if [log dead letters during shutdown]; otherwise, <c>false</c>.</value>
        public bool LogDeadLettersDuringShutdown { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [add logging receive].
        /// </summary>
        /// <value><c>true</c> if [add logging receive]; otherwise, <c>false</c>.</value>
        public bool AddLoggingReceive { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [debug automatic receive].
        /// </summary>
        /// <value><c>true</c> if [debug automatic receive]; otherwise, <c>false</c>.</value>
        public bool DebugAutoReceive { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [debug event stream].
        /// </summary>
        /// <value><c>true</c> if [debug event stream]; otherwise, <c>false</c>.</value>
        public bool DebugEventStream { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [debug unhandled message].
        /// </summary>
        /// <value><c>true</c> if [debug unhandled message]; otherwise, <c>false</c>.</value>
        public bool DebugUnhandledMessage { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [debug router misconfiguration].
        /// </summary>
        /// <value><c>true</c> if [debug router misconfiguration]; otherwise, <c>false</c>.</value>
        public bool DebugRouterMisconfiguration { get; private set; }

        /// <summary>
        ///     Gets the home.
        /// </summary>
        /// <value>The home.</value>
        public string Home { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [debug router mis configuration].
        /// </summary>
        /// <value><c>true</c> if [debug router mis configuration]; otherwise, <c>false</c>.</value>
        public bool DebugRouterMisConfiguration { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [debug lifecycle].
        /// </summary>
        /// <value><c>true</c> if [debug lifecycle]; otherwise, <c>false</c>.</value>
        public bool DebugLifecycle { get; private set; }

        /// <summary>
        ///     Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="string" /> that represents this instance.</returns>
        public override string ToString()
        {
            return Config.ToString();
        }
    }
}