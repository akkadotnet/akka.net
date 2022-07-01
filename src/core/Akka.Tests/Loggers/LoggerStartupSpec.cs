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
        
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ActorSystem_should_start_with_loggers_timing_out(bool useAsync)
        {
            var slowLoggerConfig = ConfigurationFactory.ParseString($@"
akka.stdout-logger-class = ""{typeof(XUnitOutLogger).FullName}, {typeof(XUnitOutLogger).Assembly.GetName().Name}""
akka.loggers = [""{typeof(SlowLoggerActor).FullName}, {typeof(SlowLoggerActor).Assembly.GetName().Name}""]
akka.logger-startup-timeout = 100ms").WithFallback(DefaultConfig);

            var config = ConfigurationFactory.ParseString($"akka.logger-async-start = {(useAsync ? "true" : "false")}")
                .WithFallback(slowLoggerConfig);

            ActorSystem sys = null;
            try
            {
                this.Invoking(_ => sys = ActorSystem.Create("test", config)).Should()
                    .NotThrow("System should not fail to start when a logger timed out when logger is created synchronously");

                // Logger actor should die
                var probe = CreateTestProbe(sys);
                SlowLoggerActor.Probe = probe;
                Logging.GetLogger(sys, this).Error("TEST");
                probe.ExpectNoMsg(200.Milliseconds());
            }
            finally
            {
                if(sys != null)
                    Shutdown(sys);
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
