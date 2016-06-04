﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Event
{
    public class LoggerSpec : AkkaSpec
    {
        [Fact]
        public async Task LoggingBus_should_stop_all_loggers_on_termination()
        {
            var system = ActorSystem.Create("TestSystem", ConfigurationFactory.Default());

            system.EventStream.Subscribe(TestActor, typeof(Debug));
            await system.Terminate();

            var loggerStarted = ExpectMsg<Debug>(TestKitSettings.DefaultTimeout);
            loggerStarted.Message.ShouldBe("Shutting down: StandardOutLogger started");
            loggerStarted.LogClass.ShouldBe(typeof(EventStream));
            loggerStarted.LogSource.ShouldBe(typeof(EventStream).Name);

            var loggerStopped = ExpectMsg<Debug>(TestKitSettings.DefaultTimeout);
            loggerStopped.Message.ShouldBe("All default loggers stopped");
            loggerStopped.LogClass.ShouldBe(typeof(EventStream));
            loggerStopped.LogSource.ShouldBe(typeof(EventStream).Name);

            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }
    }
}
