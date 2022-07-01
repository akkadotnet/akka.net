//-----------------------------------------------------------------------
// <copyright file="LoggerStartupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Loggers
{
    public class LoggerStartupSpec : TestKit.Xunit2.TestKit
    {
        private const int LoggerResponseDelayMs = 1_000;

        public LoggerStartupSpec(ITestOutputHelper helper) : base(nameof(LoggerStartupSpec), helper)
        {
            XUnitOutLogger.Helper = helper;
        }
        
        [Fact]
        public void ActorSystem_should_start_with_loggers_timing_out()
        {
            var slowLoggerConfig = ConfigurationFactory.ParseString($@"
akka.stdout-logger-class = ""{typeof(XUnitOutLogger).FullName}, {typeof(XUnitOutLogger).Assembly.GetName().Name}""
akka.loggers = [""{typeof(SlowLoggerActor).FullName}, {typeof(SlowLoggerActor).Assembly.GetName().Name}""]
akka.logger-startup-timeout = 100ms").WithFallback(DefaultConfig);
            
            var loggerAsyncStartDisabledConfig = ConfigurationFactory.ParseString("akka.logger-async-start = false")
                .WithFallback(slowLoggerConfig);
            var loggerAsyncStartEnabledConfig = ConfigurationFactory.ParseString("akka.logger-async-start = true")
                .WithFallback(slowLoggerConfig);

            ActorSystem sys1 = null;
            ActorSystem sys2 = null;
            try
            {
                this.Invoking(_ => sys1 = ActorSystem.Create("handing", loggerAsyncStartDisabledConfig)).Should()
                    .NotThrow("System should not fail to start when a logger timed out when logger is created synchronously");

                // Logger actor should die
                var probe1 = CreateTestProbe(sys1);
                SlowLoggerActor.Probe = probe1;
                Logging.GetLogger(sys1, this).Error("TEST");
                probe1.ExpectNoMsg(1.Seconds());

                this.Invoking(_ => sys2 = ActorSystem.Create("working", loggerAsyncStartEnabledConfig)).Should()
                    .NotThrow("System should not fail to start when a logger timed out when logger is created asynchronously (setting should be ignored internally)");

                // Logger actor should die
                var probe2 = CreateTestProbe(sys2);
                SlowLoggerActor.Probe = probe2;
                Logging.GetLogger(sys2, this).Error("TEST");
                probe2.ExpectNoMsg(1.Seconds());
            }
            finally
            {
                if(sys1 != null)
                    Shutdown(sys1);
                if(sys2 != null)
                    Shutdown(sys2);
            }
        }

        public class SlowLoggerActor : ReceiveActor
        {
            public static TestProbe Probe;
            
            public SlowLoggerActor()
            {
                ReceiveAsync<InitializeLogger>(async _ =>
                {
                    // Ooops... Logger is responding too slow
                    await Task.Delay(LoggerResponseDelayMs);
                    Sender.Tell(new LoggerInitialized());
                });
                Receive<LogEvent>(log =>
                {
                    Probe.Tell(log.Message);
                });
            }
        }

        public class XUnitOutLogger : MinimalLogger
        {
            public static ITestOutputHelper Helper;
            
            protected override void Log(object message)
            {
                Helper.WriteLine(message.ToString());
            }
        }
    }
}
