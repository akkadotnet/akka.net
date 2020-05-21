// //-----------------------------------------------------------------------
// // <copyright file="LoggerStartupSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Loggers
{
    public class LoggerStartupSpec : TestKit.Xunit2.TestKit
    {
        private const int LoggerResponseDelayMs = 10_000;
        
        [Fact]
        public async Task Logger_async_start_configuration_helps_to_ignore_hanging_loggers()
        {
            var loggerAsyncStartDisabledConfig = ConfigurationFactory.ParseString($"akka.logger-async-start = false");
            var loggerAsyncStartEnabledConfig = ConfigurationFactory.ParseString($"akka.logger-async-start = true");
            
            var slowLoggerConfig = ConfigurationFactory.ParseString($"akka.loggers = [\"{typeof(SlowLoggerActor).FullName}, {typeof(SlowLoggerActor).Assembly.GetName().Name}\"]");

            // Without logger async start, ActorSystem creation will hang
            this.Invoking(_ => ActorSystem.Create("handing", slowLoggerConfig.WithFallback(loggerAsyncStartDisabledConfig)))
                .ShouldThrow<Exception>("System can not start - logger timed out");
            
            // With logger async start, ActorSystem is created without issues
             // Created without timeouts
            var system = ActorSystem.Create("working", slowLoggerConfig.WithFallback(loggerAsyncStartEnabledConfig));
        }
        
        public class SlowLoggerActor : ReceiveActor
        {
            public SlowLoggerActor()
            {
                ReceiveAsync<InitializeLogger>(async _ =>
                {
                    // Ooops... Logger is responding too slow
                    await Task.Delay(LoggerResponseDelayMs);
                    Sender.Tell(new LoggerInitialized());
                });
            }

            private void Log(LogLevel level, string str)
            {
            }
        }
    }
}