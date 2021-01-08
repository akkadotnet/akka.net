//-----------------------------------------------------------------------
// <copyright file="LoggerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
using FluentAssertions;
using Akka.Configuration;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Tests.Event
{
    public class LoggerSpec : AkkaSpec
    {
        public static readonly Config Config = @"akka.loglevel = DEBUG";

        public LoggerSpec(ITestOutputHelper helper) : base(Config, helper) { }

        [Theory]
        [InlineData(LogLevel.ErrorLevel, false, "foo", new object[] { })]
        [InlineData(LogLevel.ErrorLevel, true, "foo", new object[] { })]
        [InlineData(LogLevel.ErrorLevel, false, "foo {0}", new object[] { 1 })]
        [InlineData(LogLevel.ErrorLevel, true, "foo {0}", new object[] { 1 })]
        [InlineData(LogLevel.WarningLevel, false, "foo", new object[] { })]
        [InlineData(LogLevel.WarningLevel, true, "foo", new object[] { })]
        [InlineData(LogLevel.WarningLevel, false, "foo {0}", new object[] { 1 })]
        [InlineData(LogLevel.WarningLevel, true, "foo {0}", new object[] { 1 })]
        [InlineData(LogLevel.InfoLevel, false, "foo", new object[] { })]
        [InlineData(LogLevel.InfoLevel, true, "foo", new object[] { })]
        [InlineData(LogLevel.InfoLevel, false, "foo {0}", new object[] { 1 })]
        [InlineData(LogLevel.InfoLevel, true, "foo {0}", new object[] { 1 })]
        [InlineData(LogLevel.DebugLevel, false, "foo", new object[]{})]
        [InlineData(LogLevel.DebugLevel, true, "foo", new object[] { })]
        [InlineData(LogLevel.DebugLevel, false, "foo {0}", new object[] { 1 })]
        [InlineData(LogLevel.DebugLevel, true, "foo {0}", new object[] { 1 })]
        public void LoggingAdapter_should_log_all_information(LogLevel logLevel, bool includeException, string formatStr, object [] args)
        {
            Sys.EventStream.Subscribe(TestActor, typeof(LogEvent));
            var msg = args != null ? string.Format(formatStr, args) : formatStr;
            var ex = new Exception("errrrrrr");
            switch (logLevel)
            {
                case LogLevel.DebugLevel when includeException:
                    Log.Debug(ex, formatStr, args);
                    break;
                case LogLevel.DebugLevel:
                    Log.Debug(formatStr, args);
                    break;
                case LogLevel.InfoLevel when includeException:
                    Log.Info(ex, formatStr, args);
                    break;
                case LogLevel.InfoLevel:
                    Log.Info(formatStr, args);
                    break;
                case LogLevel.WarningLevel when includeException:
                    Log.Warning(ex, formatStr, args);
                    break;
                case LogLevel.WarningLevel:
                    Log.Warning(formatStr, args);
                    break;
                case LogLevel.ErrorLevel when includeException:
                    Log.Error(ex, formatStr, args);
                    break;
                case LogLevel.ErrorLevel:
                    Log.Error(formatStr, args);
                    break;
            }

            // log a second log message using the generic method
            if (includeException)
            {
                Log.Log(logLevel, ex, formatStr, args);
               
            }
            else
            {
                Log.Log(logLevel, formatStr, args);
            }

            void ProcessLog(LogEvent logEvent)
            {
                logEvent.Message.ToString().Should().Be(msg);
                logEvent.LogLevel().Should().Be(logLevel);
                if (includeException)
                {
                    logEvent.Cause.Should().Be(ex);
                    logEvent.ToString().Should().Contain(ex.Message);
                }
                else
                    logEvent.Cause.Should().BeNull();
            }

            var log = ExpectMsg<LogEvent>();
            ProcessLog(log);

            var log2 = ExpectMsg<LogEvent>();
            ProcessLog(log2);
        }

        [Fact]
        public async Task LoggingBus_should_stop_all_loggers_on_termination()
        {
            var system = ActorSystem.Create("TestSystem", ConfigurationFactory.Default());

            system.EventStream.Subscribe(TestActor, typeof(Debug));
            await system.Terminate();

            await AwaitAssertAsync(() =>
            {
                var shutdownInitiated = ExpectMsg<Debug>(TestKitSettings.DefaultTimeout);
                shutdownInitiated.Message.ShouldBe("System shutdown initiated");
            });
            
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

