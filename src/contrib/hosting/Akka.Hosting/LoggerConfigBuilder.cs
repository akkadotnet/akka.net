// -----------------------------------------------------------------------
//  <copyright file="LoggerConfigBuilder.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.Hosting
{
    public sealed class LoggerConfigBuilder
    {
        private readonly List<Type> _loggers = [];
        private Type? _logMessageFormatter;
        internal AkkaConfigurationBuilder Builder { get; }

        internal LoggerConfigBuilder(AkkaConfigurationBuilder builder)
        {
            Builder = builder;
        }

        /// <summary>
        /// <para>
        /// Log level used by the configured loggers.
        /// </para>
        /// Defaults to <c>LogLevel.InfoLevel</c>
        /// </summary>
        public LogLevel? LogLevel { get; set; }
        
        /// <summary>
        /// <para>
        /// Log the complete configuration at INFO level when the actor system is started.
        /// This is useful when you are uncertain of what configuration is being used by the ActorSystem.
        /// </para>
        /// Defaults to false.
        /// </summary>
        public bool? LogConfigOnStart { get; set; }

        public DeadLetterOptions? DeadLetterOptions { get; set; }

        public DebugOptions? DebugOptions { get; set; }
        
        public LogFilterBuilder? LogFilterBuilder { get; set; }

        [Obsolete("Use the WithDefaultLogMessageFormatter<T> method instead")]
        public Type LogMessageFormatter
        {
            get => _logMessageFormatter ?? typeof(SemanticLogMessageFormatter);
            set
            {
                if (!typeof(ILogMessageFormatter).IsAssignableFrom(value))
                    throw new ConfigurationException($"{nameof(LogMessageFormatter)} must implement {nameof(ILogMessageFormatter)}");

                // Built-in formatters use private constructors with singleton Instance properties;
                // Akka.NET's Settings.cs handles these as special cases at runtime.
                if (value != typeof(SemanticLogMessageFormatter) && value != typeof(DefaultLogMessageFormatter))
                {
                    var ctor = value.GetConstructor([]);
                    if (ctor is null)
                        throw new ConfigurationException($"{nameof(LogMessageFormatter)} Type must have an empty constructor");
                }

                _logMessageFormatter = value;
            }
        }

        /// <summary>
        /// Clear all loggers currently registered.
        /// </summary>
        /// <returns>This <see cref="LoggerConfigBuilder"/> instance</returns>
        public LoggerConfigBuilder ClearLoggers()
        {
            _loggers.Clear();
            return this;
        }

        /// <summary>
        /// Register a logger
        /// </summary>
        /// <returns>This <see cref="LoggerConfigBuilder"/> instance</returns>
        public LoggerConfigBuilder AddLogger<T>() where T: IRequiresMessageQueue<ILoggerMessageQueueSemantics>
        {
            var logger = typeof(T);
            _loggers.Add(logger);
            return this;
        }
        
        /// <summary>
        /// Sets the formatter used by the logger.
        /// </summary>
        /// <remarks>
        /// As of Akka.NET 1.5.58, <see cref="SemanticLogMessageFormatter"/> is the default formatter
        /// and is configured automatically. This method is only needed if you have a custom
        /// <see cref="ILogMessageFormatter"/> implementation.
        /// </remarks>
        [Obsolete("SemanticLogMessageFormatter is now the default. Only use this method if you have a custom ILogMessageFormatter implementation.")]
        public LoggerConfigBuilder WithDefaultLogMessageFormatter<T>() where T: ILogMessageFormatter
        {
#pragma warning disable CS0618 // Type or member is obsolete
            LogMessageFormatter = typeof(T);
#pragma warning restore CS0618 // Type or member is obsolete
            return this;
        }

        public LoggerConfigBuilder WithLogFilter(Action<LogFilterBuilder> filterBuilder)
        {
            LogFilterBuilder ??= new LogFilterBuilder();
            filterBuilder(LogFilterBuilder);
            return this;
        }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used by logger extensions that needed to perform specific tasks before registering a logger type,
        /// such as setting up a Setup object with the builder
        /// </summary>
        /// <param name="logger">The logger <see cref="Type"/></param>
        internal void AddLogger(Type logger)
        {
            _loggers.Add(logger);
        }
        
        private Config? ToConfig()
        {
            var sb = new StringBuilder();
            
            if(LogLevel is not null)
                sb .Append("akka.loglevel=").AppendLine(ParseLogLevel(LogLevel).ToHocon());
            
            if(_loggers.Count > 0)
                sb.Append("akka.loggers=[").Append(string.Join(",", _loggers.Select(t => $"\"{t.AssemblyQualifiedName}\""))).AppendLine("]");
            
            if(LogConfigOnStart is not null)
                sb.Append("akka.log-config-on-start=").AppendLine(LogConfigOnStart.Value.ToHocon());
            
            if(_logMessageFormatter is not null)
                sb.Append("akka.logger-formatter=").AppendLine(_logMessageFormatter.AssemblyQualifiedName.ToHocon());
                
            if (DebugOptions is not null)
                sb.AppendLine(DebugOptions.ToString());
            
            if (DeadLetterOptions is not null)
                sb.AppendLine(DeadLetterOptions.ToString());
            
            return sb.Length > 0 ? ConfigurationFactory.ParseString(sb.ToString()) : null;
        }

        internal AkkaConfigurationBuilder Build(AkkaConfigurationBuilder builder)
        {
            var config = ToConfig();
            if(config is not null)
                builder.AddHoconConfiguration(config, HoconAddMode.Prepend);
            if (LogFilterBuilder is not null)
                builder.AddSetup(LogFilterBuilder.Build());
            
            return builder;
        }

        private static string ParseLogLevel(LogLevel? logLevel)
            => logLevel switch
            {
                Akka.Event.LogLevel.DebugLevel => "Debug",
                Akka.Event.LogLevel.InfoLevel => "Info",
                Akka.Event.LogLevel.WarningLevel => "Warning",
                Akka.Event.LogLevel.ErrorLevel => "Error",
                _ => throw new ConfigurationException($"Unknown {nameof(LogLevel)} enum value: {logLevel}")
            };
    }
}