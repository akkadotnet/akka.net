//-----------------------------------------------------------------------
// <copyright file="LoggerStartupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
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
        
        [Theory(DisplayName = "ActorSystem should start with loggers timing out")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ActorSystem_should_start_with_loggers_timing_out(bool useAsync)
        {
            var slowLoggerConfig = ConfigurationFactory.ParseString($@"
akka.loglevel = DEBUG
akka.stdout-logger-class = ""{typeof(XUnitOutLogger).FullName}, {typeof(XUnitOutLogger).Assembly.GetName().Name}""
akka.loggers = [
    ""Akka.Event.DefaultLogger"",
    ""{typeof(SlowLoggerActor).FullName}, {typeof(SlowLoggerActor).Assembly.GetName().Name}""
]
akka.logger-startup-timeout = 100ms").WithFallback(DefaultConfig);

            var config = ConfigurationFactory.ParseString($"akka.logger-async-start = {(useAsync ? "true" : "false")}")
                .WithFallback(slowLoggerConfig);

            ActorSystem sys = null;
            try
            {
                this.Invoking(_ => sys = ActorSystem.Create("test", config)).Should()
                    .NotThrow("System should not fail to start when a logger timed out");
                var probe = CreateTestProbe(sys);
                SlowLoggerActor.Probe = probe;
                var logProbe = CreateTestProbe(sys);
                sys.EventStream.Subscribe(logProbe, typeof(LogEvent));

                // Logger actor should eventually initialize
                await AwaitAssertAsync(() =>
                {
                    var dbg = logProbe.ExpectMsg<Debug>();
                    dbg.Message.ToString().Should().Contain(nameof(SlowLoggerActor)).And.Contain("started");
                });
                
                var logger = Logging.GetLogger(sys, this);
                logger.Error("TEST");
                await AwaitAssertAsync(() =>
                {
                    probe.ExpectMsg<string>().Should().Be("TEST");
                });
            }
            finally
            {
                if(sys != null)
                    Shutdown(sys);
            }
        }

        [Theory(DisplayName = "ActorSystem should start with loggers throwing exceptions")]
        [InlineData("ctor")]
        [InlineData("PreStart")]
        [InlineData("Receive")]
        public void ActorSystem_should_start_with_logger_exception(string when)
        {
            var config = ConfigurationFactory.ParseString($@"
akka.loglevel = DEBUG
akka.stdout-logger-class = ""{typeof(XUnitOutLogger).FullName}, {typeof(XUnitOutLogger).Assembly.GetName().Name}""
akka.loggers = [
    ""Akka.Event.DefaultLogger"",
    ""{typeof(ThrowingLogger).FullName}, {typeof(ThrowingLogger).Assembly.GetName().Name}""
]
akka.logger-startup-timeout = 100ms").WithFallback(DefaultConfig);

            ThrowingLogger.ThrowWhen = when;
            ActorSystem sys = null;
            try
            {
                this.Invoking(_ => sys = ActorSystem.Create("test", config)).Should()
                    .NotThrow("System should not fail to start when a logger throws an exception");
            }
            finally
            {
                if(sys != null)
                    Shutdown(sys);
            }
        }
        
        [Fact(DisplayName = "ActorSystem should throw and fail to start on invalid logger FQCN entry")]
        public void ActorSystem_should_fail_on_invalid_logger()
        {
            var config = ConfigurationFactory.ParseString($@"
akka.loglevel = DEBUG
akka.stdout-logger-class = ""{typeof(XUnitOutLogger).FullName}, {typeof(XUnitOutLogger).Assembly.GetName().Name}""
akka.loggers = [
    ""Akka.Event.InvalidLogger, NonExistantAssembly""
]
akka.logger-startup-timeout = 100ms").WithFallback(DefaultConfig);

            ActorSystem sys = null;
            try
            {
                this.Invoking(_ => sys = ActorSystem.Create("test", config)).Should()
                    .ThrowExactly<ConfigurationException>("System should fail to start with invalid logger FQCN");
            }
            finally
            {
                if(sys != null)
                    Shutdown(sys);
            }
        }

        private class TestException: Exception
        {
            public TestException(string message) : base(message)
            { }
        }
        
        private class ThrowingLogger : ActorBase, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
        {
            public static string ThrowWhen = "Receive";

            public ThrowingLogger()
            {
                if(ThrowWhen == "ctor")
                    throw new TestException(".ctor BOOM!");
            }

            protected override void PreStart()
            {
                base.PreStart();
                if(ThrowWhen == "PreStart")
                    throw new TestException("PreStart BOOM!");
            }

            protected override bool Receive(object message)
            {
                if(message is InitializeLogger)
                {
                    if(ThrowWhen == "Receive")
                        throw new TestException("Receive BOOM!");
                
                    Sender.Tell(new LoggerInitialized());
                    return true;
                }

                return false;
            }
        }
        
        private class SlowLoggerActor : ActorBase, IWithUnboundedStash, IRequiresMessageQueue<ILoggerMessageQueueSemantics> 
        {
            public static TestProbe Probe;
            
            public SlowLoggerActor()
            {
                Become(Starting);
            }

            private bool Starting(object message)
            {
                switch (message)
                {
                    case InitializeLogger _:
                        var sender = Sender;
                        Task.Delay(LoggerResponseDelayMs).PipeTo(Self, sender, success: () => Done.Instance);
                        Become(Initializing);
                        return true;
                    default:
                        Stash.Stash();
                        return true;
                }
            }

            private bool Initializing(object message)
            {
                switch (message)
                {
                    case Done _:
                        Sender.Tell(new LoggerInitialized());
                        Become(Initialized);
                        Stash.UnstashAll();
                        return true;
                    default:
                        Stash.Stash();
                        return true;
                }
            }

            private bool Initialized(object message)
            {
                switch (message)
                {
                    case LogEvent evt:
                        Probe.Tell(evt.Message.ToString());
                        return true;
                    default:
                        return false;
                }
            }

            protected override bool Receive(object message)
            {
                throw new NotImplementedException();
            }

            public IStash Stash { get; set; }
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
