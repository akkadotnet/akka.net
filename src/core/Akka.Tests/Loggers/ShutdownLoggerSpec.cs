// //-----------------------------------------------------------------------
// // <copyright file="ShutdownLoggerSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Loggers
{
    public class ShutdownLoggerSpec: AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
akka.loglevel = OFF
akka.stdout-loglevel = OFF
akka.stdout-logger-class = ""Akka.Tests.Loggers.ThrowingLogger, Akka.Tests""");

        public ShutdownLoggerSpec(ITestOutputHelper output) : base(Config, output)
        {
        }

        [Fact(DisplayName = "StandardOutLogger should not be called on shutdown when stdout-loglevel is set to OFF")]
        public async Task StandardOutLoggerShouldNotBeCalled()
        {
            Sys.Settings.StdoutLogger.Should().BeOfType<ThrowingLogger>();
            
            var probeRef = Sys.ActorOf(Props.Create(() => new LogProbe()));
            probeRef.Tell(new InitializeLogger(Sys.EventStream));
            var probe = await probeRef.Ask<LogProbe>("hey");
            
            await Sys.Terminate();

            await Task.Delay(RemainingOrDefault);
            if (probe.Events.Any(o => o is Error err && err.Cause is ShutdownLogException))
                throw new Exception("Test failed, log should not be called after shutdown.");
        }
    }

    internal class LogProbe : ReceiveActor
    {
        public List<LogEvent> Events { get; } = new List<LogEvent>();

        public LogProbe()
        {
            Receive<string>(msg => Sender.Tell(this));
            Receive<LogEvent>(Events.Add);
            Receive<InitializeLogger>(e =>
            {
                e.LoggingBus.Subscribe(Self, typeof (LogEvent));
                Sender.Tell(new LoggerInitialized());
            });

        }
    }

    internal class ShutdownLogException : Exception
    {
        public ShutdownLogException()
        { }
        
        public ShutdownLogException(string msg) : base(msg)
        { }
    }

    internal class ThrowingLogger : MinimalLogger
    {
        protected override void Log(object message)
        {
            throw new ShutdownLogException("This logger should never be called.");
        }
    }
}