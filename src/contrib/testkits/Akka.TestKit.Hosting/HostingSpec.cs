// -----------------------------------------------------------------------
//  <copyright file="HostingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Hosting.Internals;
using FluentAssertions.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.TestKit.Hosting
{
    public abstract partial class HostingSpec: IAsyncLifetime
    {
        private IHost _host;
        public IHost Host
        {
            get
            {
                AssertNotNull(_host);
                return _host;
            }
        }

        private TestKitBaseUnWrapper _testKit;
        public Xunit2.TestKit TestKit
        {
            get
            {
                AssertNotNull(_testKit);
                return _testKit;
            }
        }

        public ActorRegistry ActorRegistry => Host.Services.GetService<ActorRegistry>();
        
        public TimeSpan StartupTimeout { get; }
        public string ActorSystemName { get; }
        public ITestOutputHelper Output { get; }
        public LogLevel LogLevel { get; }

        protected HostingSpec(string actorSystemName, ITestOutputHelper output = null, TimeSpan? startupTimeout = null, LogLevel logLevel = LogLevel.Information)
        {
            ActorSystemName = actorSystemName;
            Output = output;
            LogLevel = logLevel;
            StartupTimeout = startupTimeout ?? 10.Seconds();
        }
        
        protected virtual void ConfigureHostConfiguration(IConfigurationBuilder builder)
        { }
        
        protected virtual void ConfigureAppConfiguration(HostBuilderContext context, IConfigurationBuilder builder)
        { }

        protected virtual void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        { }
        
        private void InternalConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            ConfigureServices(context, services);
            
            services.AddAkka(ActorSystemName, (builder, provider) =>
            {
                builder.ConfigureLoggers(logger =>
                {
                    logger.LogLevel = ToAkkaLogLevel(LogLevel);
                    if (Output != null)
                    {
                        logger.ClearLoggers();
                        logger.AddLoggerFactory();
                    }
                });

                ConfigureAkka(builder, provider);
            });
        }

        protected virtual void ConfigureLogging(ILoggingBuilder builder)
        { }
        
        protected virtual void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
        { }
        
        public async Task InitializeAsync()
        {
            var hostBuilder = new HostBuilder();
            if (Output != null)
                hostBuilder.ConfigureLogging(logger =>
                {
                    logger.ClearProviders();
                    logger.AddProvider(new XUnitLoggerProvider(Output, LogLevel));
                    logger.AddFilter("Akka.*", LogLevel);
                    ConfigureLogging(logger);
                });
            hostBuilder
                .ConfigureHostConfiguration(ConfigureHostConfiguration)
                .ConfigureAppConfiguration(ConfigureAppConfiguration)
                .ConfigureServices(InternalConfigureServices);

            _host = hostBuilder.Build();

            var cts = new CancellationTokenSource(StartupTimeout);
            cts.Token.Register(() =>
                throw new TimeoutException($"Host failed to start within {StartupTimeout.Seconds} seconds"));
            try
            {
                await _host.StartAsync(cts.Token);
            }
            finally
            {
                cts.Dispose();
            }

            _sys = _host.Services.GetRequiredService<ActorSystem>();
            _testKit = new TestKitBaseUnWrapper(_sys);
        }

        public async Task DisposeAsync()
        {
            if(_host != null)
                await _host.StopAsync();
        }

        private static Event.LogLevel ToAkkaLogLevel(LogLevel logLevel)
            => logLevel switch
            {
                LogLevel.Trace => Event.LogLevel.DebugLevel,
                LogLevel.Debug => Event.LogLevel.DebugLevel,
                LogLevel.Information => Event.LogLevel.InfoLevel,
                LogLevel.Warning => Event.LogLevel.WarningLevel,
                LogLevel.Error => Event.LogLevel.ErrorLevel,
                LogLevel.Critical => Event.LogLevel.ErrorLevel,
                _ => Event.LogLevel.ErrorLevel
            };
        
        private static void AssertNotNull(object obj)
        {
            if(obj is null)
                throw new XunitException("Test has not been initialized yet"); 
        }
    }    
}

