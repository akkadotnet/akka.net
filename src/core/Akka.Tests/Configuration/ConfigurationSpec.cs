//-----------------------------------------------------------------------
// <copyright file="ConfigurationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Akka.Configuration.Hocon;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Tests.Configuration
{
    public class ConfigurationSpec : AkkaSpec
    {
        public ConfigurationSpec(ITestOutputHelper output) : base(ConfigurationFactory.Default(), output)
        {
        }

        [Fact]
        public void The_default_configuration_file_contain_all_configuration_properties()
        {
            var settings = Sys.Settings;
            var config = Sys.Settings.Config;

            // settings.ConfigVersion.ShouldBe(ActorSystem.Version);
            settings.Loggers.Count.ShouldBe(1);
            settings.Loggers[0].ShouldBe(typeof (DefaultLogger).FullName);
            // settings.LoggingFilter.ShouldBe(typeof(DefaultLoggingFilter));
            settings.LoggersDispatcher.ShouldBe(Dispatchers.DefaultDispatcherId);
            settings.LoggerStartTimeout.Seconds.ShouldBe(5);
            settings.LogLevel.ShouldBe("INFO");
            settings.StdoutLogLevel.ShouldBe("WARNING");
            settings.LogConfigOnStart.ShouldBeFalse();
            settings.LogDeadLetters.ShouldBe(10);
            settings.LogDeadLettersDuringShutdown.ShouldBeFalse();
            settings.LogDeadLettersSuspendDuration.ShouldBe(TimeSpan.FromMinutes(5));

            settings.ProviderClass.ShouldBe(typeof (LocalActorRefProvider).FullName);
            settings.SupervisorStrategyClass.ShouldBe(typeof (DefaultSupervisorStrategy).FullName);
            settings.CreationTimeout.Seconds.ShouldBe(20);
            settings.AskTimeout.ShouldBe(Timeout.InfiniteTimeSpan);
            settings.SerializeAllMessages.ShouldBeFalse();
            settings.SerializeAllCreators.ShouldBeFalse();
            settings.UnstartedPushTimeout.Seconds.ShouldBe(10);
            settings.DefaultVirtualNodesFactor.ShouldBe(10);

            settings.AddLoggingReceive.ShouldBeFalse();
            settings.DebugAutoReceive.ShouldBeFalse();
            settings.DebugLifecycle.ShouldBeFalse();
            settings.FsmDebugEvent.ShouldBe(false);
            settings.DebugEventStream.ShouldBeFalse();
            settings.DebugUnhandledMessage.ShouldBeFalse();
            settings.DebugRouterMisconfiguration.ShouldBeFalse();

            settings.SchedulerClass.ShouldBe(typeof (HashedWheelTimerScheduler).FullName);
        }

        [Fact]
        public void Deserializes_hocon_configuration_from_net_config_file()
        {
#if !CORECLR
            var section = (AkkaConfigurationSection)System.Configuration.ConfigurationManager.GetSection("akka");
            Assert.NotNull(section);
            Assert.False(string.IsNullOrEmpty(section.Hocon.Content));
            var akkaConfig = section.AkkaConfig;
            Assert.NotNull(akkaConfig);
#else
            // Skip this test for Linux targets
            Output.WriteLine("This test is skipped.");
#endif
        }

        // unit test for bug #4330
        [Fact]
        public void Should_load_config_from_app_config_file()
        {
#if !CORECLR
            var system = ActorSystem.Create(Guid.NewGuid().ToString());
            system.Settings.Config.GetBoolean("nonsense.entry").ShouldBeTrue();
            system.Terminate();
#else
            // Skip this test for Linux targets
            Output.WriteLine("This test is skipped.");
#endif
        }

    }

}
