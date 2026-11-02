// -----------------------------------------------------------------------
//  <copyright file="LoggerSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting.TestKit.Internals;
using Xunit;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Akka.Hosting.TestKit.Tests;

public class LoggerSpec: TestKit
{
    public LoggerSpec(XunitTestOutputHelper output): base(output: output, logLevel: LogLevel.Debug)
    {
    }

    internal override async Task LoggerHook(ActorSystem system, IActorRegistry registry)
    {
        var extSystem = (ExtendedActorSystem)system;
        var logger = extSystem.SystemActorOf(Props.Create(() => new MockLogger()), "log-test");
        registry.Register<MockLogger>(logger);
        await logger.Ask<LoggerInitialized>(new InitializeLogger(system.EventStream));
    }
    
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
    }

    [Fact(DisplayName = "TestKit ILoggerFactory logger should log messages")]
    public void TestKitLoggerFactoryLoggerTest()
    {
        var loggerActor = ActorRegistry.Get<MockLogger>();
        loggerActor.Tell(TestActor);

        var logger = Event.Logging.GetLogger(Sys, "log-test");
        
        logger.Debug("debug");
        ExpectMsg<Debug>(i => i.Message.ToString() == "debug");
        
        logger.Info("info");
        ExpectMsg<Info>(i => i.Message.ToString() == "info");
        
        logger.Warning("warn");
        ExpectMsg<Warning>(i => i.Message.ToString() == "warn");
        
        logger.Error("err");
        ExpectMsg<Error>(i => i.Message.ToString() == "err");
    }
    
    private class MockLogger: TestKitLoggerFactoryLogger
    {
        private IActorRef? _probe;

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case IActorRef actor:
                    _probe = actor;
                    return true;
                default:
                    return base.Receive(message);
            }
        }

        protected override void Log(LogEvent log, ActorPath path)
        {
            if(log.LogSource.StartsWith("log-test"))
                _probe.Tell(log);
            base.Log(log, path);
        }
    }

}
