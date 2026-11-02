// -----------------------------------------------------------------------
//  <copyright file="LoggingExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Event;
using Akka.Hosting.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Akka.Hosting
{
    public static class LoggingExtensions
    {
        /// <summary>
        /// Fluent interface to configure the Akka.NET logger system
        /// </summary>
        /// <param name="builder">The <see cref="AkkaConfigurationBuilder"/> being configured</param>
        /// <param name="configurator">An action that can be used to modify the logging configuration</param>
        /// <returns>The original <see cref="AkkaConfigurationBuilder"/> instance</returns>
        public static AkkaConfigurationBuilder ConfigureLoggers(this AkkaConfigurationBuilder builder, Action<LoggerConfigBuilder> configurator)
        {
            var setup = new LoggerConfigBuilder(builder);
            configurator(setup);
            return setup.Build(builder);
        }
        
        /// <summary>
        /// Add the default Akka.NET logger that sinks all log events to the console
        /// </summary>
        /// <param name="configBuilder">The <see cref="LoggerConfigBuilder"/> instance </param>
        /// <returns>the original <see cref="LoggerConfigBuilder"/> used to configure the logger system</returns>
        public static LoggerConfigBuilder AddDefaultLogger(this LoggerConfigBuilder configBuilder)
        {
            configBuilder.AddLogger<DefaultLogger>();
            return configBuilder;
        }
        
        /// <summary>
        /// Add the <see cref="ILoggerFactory"/> logger that sinks all log events to the default
        /// <see cref="ILoggerFactory"/> instance registered in the host <see cref="ServiceProvider"/>
        /// </summary>
        /// <param name="configBuilder">The <see cref="LoggerConfigBuilder"/> instance </param>
        /// <returns>the original <see cref="LoggerConfigBuilder"/> used to configure the logger system</returns>
        public static LoggerConfigBuilder AddLoggerFactory(this LoggerConfigBuilder configBuilder)
        {
            configBuilder.AddLogger(typeof(LoggerFactoryLogger));
            return configBuilder;
        }
        
        /// <summary>
        /// Add the <see cref="ILoggerFactory"/> logger that sinks all log events to the provided <see cref="ILoggerFactory"/>
        /// </summary>
        /// <param name="configBuilder">The <see cref="LoggerConfigBuilder"/> instance </param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory"/> instance to be used as the log sink</param>
        /// <returns>the original <see cref="LoggerConfigBuilder"/> used to configure the logger system</returns>
        public static LoggerConfigBuilder AddLoggerFactory(this LoggerConfigBuilder configBuilder, ILoggerFactory loggerFactory)
        {
            var builder = configBuilder.Builder;
            builder.AddSetup(new LoggerFactorySetup(loggerFactory));
            configBuilder.AddLogger(typeof(LoggerFactoryLogger));
            return configBuilder;
        }
        
    }
}