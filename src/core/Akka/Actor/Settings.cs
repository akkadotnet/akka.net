//-----------------------------------------------------------------------
// <copyright file="Settings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Routing;
using Akka.Util;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Actor
{
    /// <summary>
    /// This class represents the overall <see cref="ActorSystem"/> settings which also provides a convenient
    /// access to the <see cref="Configuration.Config"/> object. For more detailed information about the
    /// different possible configuration options, look in the Akka.NET Documentation under Configuration
    /// (https://getakka.net/articles/configuration/config.html).
    /// </summary>
    public class Settings
    {
        // The akka.stdout-logger-class values that ship inside Akka.dll, constructed directly so the trimmer
        // and the Native AOT compiler can see the type without looking through Type.GetType.
        //
        // akka.conf leaves this setting empty, so the table is only ever reached from user config. Two
        // spellings, both deliberate: the bare name and the "Ns.T, Akka" form, which is what HOCON in the
        // wild carries. The lookup runs the configured value through TypeExtensions.StripAssemblyIdentity
        // first, so a full AssemblyQualifiedName - which Akka.Hosting writes into HOCON - matches the second
        // key whatever version, culture or public key token it names. A value that still misses the table
        // falls through to the reflection path, which is unavailable (and therefore throws) once dynamic type
        // loading is switched off. Do not remove a spelling, and do not add a versioned third key.
        private static readonly Dictionary<string, Func<MinimalLogger>> BuiltInStdoutLoggers =
            new(StringComparer.Ordinal)
            {
                ["Akka.Event.StandardOutLogger"] = static () => new StandardOutLogger(),
                ["Akka.Event.StandardOutLogger, Akka"] = static () => new StandardOutLogger()
            };

        // The akka.logger-formatter values that ship inside Akka.dll, constructed directly so the trimmer and
        // the Native AOT compiler can see the type without looking through Type.GetType.
        //
        // Two spellings, both deliberate: akka.conf ships the "Ns.T, Akka" form and HOCON in the wild also
        // carries the bare name. The lookup runs the configured value through
        // TypeExtensions.StripAssemblyIdentity first, so a full AssemblyQualifiedName - which Akka.Hosting
        // writes into HOCON - matches the second key whatever version, culture or public key token it names.
        // A value that still misses the table falls through to the reflection path, which is unavailable (and
        // therefore throws) once dynamic type loading is switched off. Do not remove a spelling, and do not
        // add a versioned third key.
        private static readonly Dictionary<string, Func<ILogMessageFormatter>> BuiltInLogMessageFormatters =
            new(StringComparer.Ordinal)
            {
                ["Akka.Event.DefaultLogMessageFormatter"] = static () => DefaultLogMessageFormatter.Instance,
                ["Akka.Event.DefaultLogMessageFormatter, Akka"] = static () => DefaultLogMessageFormatter.Instance,
                ["Akka.Event.SemanticLogMessageFormatter"] = static () => SemanticLogMessageFormatter.Instance,
                ["Akka.Event.SemanticLogMessageFormatter, Akka"] = static () => SemanticLogMessageFormatter.Instance
            };

        private readonly Config _userConfig;
        //internal static readonly Config AkkaDllConfig = ConfigurationFactory.FromResource<Settings>("Akka.Configuration.Pigeon.conf");
        private Config _fallbackConfig;
        private readonly object _configLock = new();

        private void RebuildConfig()
        {
            Config = _userConfig.SafeWithFallback(_fallbackConfig);

            //if we get a new config definition loaded after all ActorRefProviders have been started, such as Akka.Persistence...
            System?.Dispatchers?.ReloadPrerequisites(new DefaultDispatcherPrerequisites(System.EventStream, System.Scheduler, this, System.Mailboxes));
            if (System is Internal.ISupportSerializationConfigReload rs)
                rs.ReloadSerialization();
        }

        /// <summary>
        /// Injects a system config at the top of the fallback chain
        /// </summary>
        /// <param name="config">The latest config to be added to the front of the <see cref="Settings.Config"/> fallback chain</param>
        public void InjectTopLevelFallback(Config config)
        {
            if (Config.Contains(config)) 
                return;

            lock (_configLock)
            {
                _fallbackConfig = config.SafeWithFallback(_fallbackConfig);
                RebuildConfig();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Settings" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="config">The configuration.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown if the 'akka.actor.provider' configuration item is not a valid type name or a valid actor ref provider.
        /// </exception>
        public Settings(ActorSystem system, Config config) : this(system, config, ActorSystemSetup.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Settings" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="config">The configuration.</param>
        /// <param name="setup">The setup class used to help bootstrap the <see cref="ActorSystem"/></param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown if the 'akka.actor.provider' configuration item is not a valid type name or a valid actor ref provider.
        /// </exception>
        public Settings(ActorSystem system, Config config, ActorSystemSetup setup)
        {
            Setup = setup;
            _userConfig = config;
            _fallbackConfig = ConfigurationFactory.Default();
            RebuildConfig();

            System = system;

            var providerSelectionSetup = Setup.Get<BootstrapSetup>()
                .FlatSelect(_ => _.ActorRefProvider)
                .Select(_ => _.Fqn)
                .GetOrElse(Config.GetString("akka.actor.provider", null));

            ProviderSelectionType = ProviderSelection.GetProvider(providerSelectionSetup);

            ConfigVersion = Config.GetString("akka.version", null);
            ProviderClass = ProviderSelectionType.Fqn;
            HasCluster = ProviderSelectionType.HasCluster;

            // The three built-in providers do not need to be validated here: their type names are
            // compile-time constants and ActorSystemImpl.ConfigureProvider resolves each one from
            // its own constant so the trimmer / Native AOT compiler can keep the type. Validating
            // them through the ProviderClass property would reintroduce a dynamic Type.GetType call
            // that the trimmer cannot see through (IL2057), and under Native AOT it would fail here
            // even though the provider itself is perfectly resolvable.
            if (ProviderSelectionType is ProviderSelection.Custom)
            {
                var providerType = Type.GetType(ProviderClass);
                if (providerType == null)
                    throw new ConfigurationException($"'akka.actor.provider' is not a valid type name : '{ProviderClass}'");
                if (!typeof(IActorRefProvider).IsAssignableFrom(providerType))
                    throw new ConfigurationException($"'akka.actor.provider' is not a valid actor ref provider: '{ProviderClass}'");
            }

            SupervisorStrategyClass = Config.GetString("akka.actor.guardian-supervisor-strategy", null);

            AskTimeout = Config.GetTimeSpan("akka.actor.ask-timeout", null, allowInfinite: true);
            CreationTimeout = Config.GetTimeSpan("akka.actor.creation-timeout", null);
            UnstartedPushTimeout = Config.GetTimeSpan("akka.actor.unstarted-push-timeout", null);

            SerializeAllMessages = Config.GetBoolean("akka.actor.serialize-messages", false);
            SerializeAllCreators = Config.GetBoolean("akka.actor.serialize-creators", false);
            EmitActorTelemetry = Config.GetBoolean("akka.actor.telemetry.enabled", false);

            LogLevel = Config.GetString("akka.loglevel", null);
            StdoutLogLevel = Config.GetString("akka.stdout-loglevel", null);
            
            // FILTER MUST ALWAYS BE LOADED BEFORE STANDARD OUT LOGGER
            // check to see if we have a LogFilterSetup in the ActorSystemSetup
            var logFilterSetup = Setup.Get<LogFilterSetup>();
            if (logFilterSetup.HasValue)
            {
                LogFilter = logFilterSetup.Value.CreateEvaluator();
            }
            else
            {
                LogFilter = LogFilterEvaluator.NoFilters;
            }

            var stdoutClassName = Config.GetString("akka.stdout-logger-class", null);
            if (string.IsNullOrWhiteSpace(stdoutClassName))
            {
                StdoutLogger = new StandardOutLogger();
            }
            else if (BuiltInStdoutLoggers.TryGetValue(
                         TypeExtensions.StripAssemblyIdentity(stdoutClassName), out var stdoutLoggerFactory))
            {
                StdoutLogger = stdoutLoggerFactory();
            }
            else if (AkkaFeatures.IsDynamicTypeLoadingSupported)
            {
                StdoutLogger = CreateStdoutLogger(stdoutClassName);
            }
            else
            {
                throw new ConfigurationException(AkkaFeatures.NotBuiltIn(
                    "akka.stdout-logger-class", stdoutClassName, "one of the built-in standard out loggers"));
            }

            // set the filter
            StdoutLogger!.Filter = LogFilter;
            
            Loggers = Config.GetStringList("akka.loggers", new string[] { });
            LoggersDispatcher = Config.GetString("akka.loggers-dispatcher", null);
            LoggerStartTimeout = Config.GetTimeSpan("akka.logger-startup-timeout", null);
            LoggerAsyncStart = Config.GetBoolean("akka.logger-async-start", false);

            var loggerFormatterName = Config.GetString("akka.logger-formatter", null);
            if (string.IsNullOrWhiteSpace(loggerFormatterName))
            {
                LogFormatter = DefaultLogMessageFormatter.Instance;
            }
            else if (BuiltInLogMessageFormatters.TryGetValue(
                         TypeExtensions.StripAssemblyIdentity(loggerFormatterName), out var logFormatterFactory))
            {
                LogFormatter = logFormatterFactory();
            }
            else if (AkkaFeatures.IsDynamicTypeLoadingSupported)
            {
                LogFormatter = CreateLogMessageFormatter(loggerFormatterName);
            }
            else
            {
                throw new ConfigurationException(AkkaFeatures.NotBuiltIn(
                    "akka.logger-formatter", loggerFormatterName, "one of the built-in log message formatters"));
            }

            //handled
            LogConfigOnStart = Config.GetBoolean("akka.log-config-on-start", false);
            LogSerializerOverrideOnStart = Config.GetBoolean("akka.log-serializer-override-on-start", true);
            LogDeadLetters = 0;
            switch (Config.GetString("akka.log-dead-letters", null))
            {
                case "on":
                case "true":
                case "yes":
                    LogDeadLetters = int.MaxValue;
                    break;
                case "off":
                case "false":
                case "no":
                    LogDeadLetters = 0;
                    break;
                default:
                    LogDeadLetters = Config.GetInt("akka.log-dead-letters", 0);
                    break;
            }
            LogDeadLettersDuringShutdown = Config.GetBoolean("akka.log-dead-letters-during-shutdown", false);

            const string key = "akka.log-dead-letters-suspend-duration";
            LogDeadLettersSuspendDuration = Config.GetString(key, null) == "infinite" ? Timeout.InfiniteTimeSpan : Config.GetTimeSpan(key);

            AddLoggingReceive = Config.GetBoolean("akka.actor.debug.receive", false);
            DebugAutoReceive = Config.GetBoolean("akka.actor.debug.autoreceive", false);
            DebugLifecycle = Config.GetBoolean("akka.actor.debug.lifecycle", false);
            FsmDebugEvent = Config.GetBoolean("akka.actor.debug.fsm", false);
            DebugEventStream = Config.GetBoolean("akka.actor.debug.event-stream", false);
            DebugUnhandledMessage = Config.GetBoolean("akka.actor.debug.unhandled", false);
            DebugRouterMisconfiguration = Config.GetBoolean("akka.actor.debug.router-misconfiguration", false);
            DebugTimerScheduler = Config.GetBoolean("akka.actor.debug.log-timers");
            Home = Config.GetString("akka.home", "");
            DefaultVirtualNodesFactor = Config.GetInt("akka.actor.deployment.default.virtual-nodes-factor", 0);

            SchedulerClass = Config.GetString("akka.scheduler.implementation", null);
            SchedulerShutdownTimeout = Config.GetTimeSpan("akka.scheduler.shutdown-timeout", null);

            CoordinatedShutdownTerminateActorSystem = Config.GetBoolean("akka.coordinated-shutdown.terminate-actor-system");
            CoordinatedShutdownRunByActorSystemTerminate = Config.GetBoolean("akka.coordinated-shutdown.run-by-actor-system-terminate");

            if (CoordinatedShutdownRunByActorSystemTerminate && !CoordinatedShutdownTerminateActorSystem)
                throw new ConfigurationException(
                  "akka.coordinated-shutdown.run-by-actor-system-terminate=on and " +
                  "akka.coordinated-shutdown.terminate-actor-system=off is not a supported configuration combination.");
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
        /// The setup used to help bootstrap this <see cref="ActorSystem"/>.
        /// </summary>
        public ActorSystemSetup Setup { get; }

        /// <summary>
        /// Used to indicate whether or not clustering is enabled for this <see cref="ActorSystem"/>.
        /// </summary>
        public bool HasCluster { get; }

        /// <summary>
        /// INTENRAL API
        /// </summary>
        public ProviderSelection ProviderSelectionType { get; }

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
        /// When set to <c>true</c>, all actors will emit <see cref="IActorTelemetryEvent"/>s when they are created, stopped, or restarted.
        /// </summary>
        /// <remarks>
        /// Defaults to <c>false</c>.
        /// </remarks>
        /// <code>
        /// akka.actor.telemetry.enabled = on
        /// </code>
        public bool EmitActorTelemetry { get; }

        /// <summary>
        ///     Gets the default timeout for <see cref="Futures.Ask(ICanTell, object, TimeSpan?)">Futures.Ask</see> calls.
        /// </summary>
        /// <value>The ask timeout.</value>
        public TimeSpan AskTimeout { get; private set; }

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
        /// Returns a singleton instance of the standard out logger.
        /// </summary>
        public MinimalLogger StdoutLogger { get; }
        
        /// <summary>
        ///     Gets the loggers.
        /// </summary>
        /// <value>The loggers.</value>
        public IList<string> Loggers { get; private set; }

        /// <summary>
        ///     Gets the default loggers dispatcher.
        /// </summary>
        /// <value>The loggers dispatcher.</value>
        public string LoggersDispatcher { get; private set; }

        /// <summary>
        ///     Gets the logger start timeout.
        /// </summary>
        /// <value>The logger start timeout.</value>
        public TimeSpan LoggerStartTimeout { get; private set; }

        /// <summary>
        ///     Gets the logger start timeout.
        /// </summary>
        /// <value>The logger start timeout.</value>
        public bool LoggerAsyncStart { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether [log configuration on start].
        /// </summary>
        /// <value><c>true</c> if [log configuration on start]; otherwise, <c>false</c>.</value>
        public bool LogConfigOnStart { get; private set; }

        /// <summary>
        /// The default formatter used by the <see cref="ILoggingAdapter"/>.
        /// </summary>
        /// <remarks>
        /// Can be overridden on individual `Context.GetLogger()` calls.
        /// </remarks>
        public ILogMessageFormatter LogFormatter { get; }
        
        /// <summary>
        /// Used to filter log messages based on the log source and message content.
        /// </summary>
        /// <remarks>
        /// Not enabled by default and may not be supported in all third party logging implementations.
        /// </remarks>
        public LogFilterEvaluator LogFilter { get; }

        /// <summary>
        ///     Gets a value indicating whether [log serializer override on start].
        /// </summary>
        /// <value><c>true</c> if [log serializer override on start]; otherwise, <c>false</c>.</value>
        public bool LogSerializerOverrideOnStart { get; private set; }

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
        /// TBD
        /// </summary>
        public TimeSpan LogDeadLettersSuspendDuration { get; }

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
        ///     Gets a value indicating whether [debug lifecycle].
        /// </summary>
        /// <value><c>true</c> if [debug lifecycle]; otherwise, <c>false</c>.</value>
        public bool DebugLifecycle { get; private set; }
        
        /// <summary>
        ///     Should TimerScheduler emit debug logs
        /// </summary>
        public bool DebugTimerScheduler { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool FsmDebugEvent { get; private set; }

        /// <summary>
        /// The number of default virtual nodes to use with <see cref="ConsistentHashingRoutingLogic"/>.
        /// </summary>
        public int DefaultVirtualNodesFactor { get; private set; }

        /// <summary>
        /// Gets the scheduler implementation used by this system.
        /// </summary>
        public string SchedulerClass { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan SchedulerShutdownTimeout { get; private set; }

        public bool CoordinatedShutdownTerminateActorSystem { get; private set; }

        public bool CoordinatedShutdownRunByActorSystemTerminate { get; private set; }

        public override string ToString()
        {
            return Config.Root.ToString();
        }

        [RequiresUnreferencedCode("Loads the [akka.stdout-logger-class] type by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static MinimalLogger CreateStdoutLogger(string stdoutClassName)
        {
            var stdoutLoggerType = Type.GetType(stdoutClassName);
            if (stdoutLoggerType == null)
                throw new ArgumentException($"Could not load type of {stdoutClassName} for standard out logger.");
            if (!typeof(MinimalLogger).IsAssignableFrom(stdoutLoggerType))
                throw new ArgumentException("Standard out logger type must inherit from the MinimalLogger abstract class.");

            try
            {
                return (MinimalLogger)Activator.CreateInstance(stdoutLoggerType);
            }
            catch (MissingMethodException)
            {
                throw new MissingMethodException(
                    "Standard out logger type must inherit from the MinimalLogger abstract class and have an empty constructor.");
            }
        }

        [RequiresUnreferencedCode("Loads the [akka.logger-formatter] type by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static ILogMessageFormatter CreateLogMessageFormatter(string loggerFormatterName)
        {
            var logFormatType = Type.GetType(loggerFormatterName);
            if (logFormatType == null)
                throw new ArgumentException($"Could not load type of {loggerFormatterName} for ILogMessageFormatter.");
            if (!typeof(ILogMessageFormatter).IsAssignableFrom(logFormatType))
                throw new ArgumentException("Log formatter type must inherit from the ILogMessageFormatter interface.");

            // SPECIAL CASE - check for the default log message formatter, which does not have an empty constructor (it's private)
            if (logFormatType == typeof(DefaultLogMessageFormatter))
                return DefaultLogMessageFormatter.Instance;

            // SPECIAL CASE - check for the semantic log message formatter, which does not have an empty constructor (it's private)
            if (logFormatType == typeof(SemanticLogMessageFormatter))
                return SemanticLogMessageFormatter.Instance;

            try
            {
                return (ILogMessageFormatter)Activator.CreateInstance(logFormatType);
            }
            catch (MissingMethodException)
            {
                throw new MissingMethodException(
                    "Log message formatter must inherit from the ILogMessageFormatter and have an empty constructor.");
            }
        }
    }
}
