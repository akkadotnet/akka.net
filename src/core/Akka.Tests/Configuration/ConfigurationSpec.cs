//-----------------------------------------------------------------------
// <copyright file="ConfigurationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

namespace Akka.Tests.Configuration
{
    public class ConfigurationSpec : AkkaSpec
    {
        public ConfigurationSpec(ITestOutputHelper output) : base(AkkaConfigurationFactory.DefaultConfig, output)
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
            settings.LogDeadLettersDuringShutdown.ShouldBeTrue();

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
            // Skip this test for Linux targets
            var OS = new OSInfo();
            if (OS.IsWindows)
            {
                Output.WriteLine("This test is NOT skipped.");
                var section = (AkkaConfigurationSection)System.Configuration.ConfigurationManager.GetSection("akka");
                Assert.NotNull(section);
                Assert.False(string.IsNullOrEmpty(section.Hocon.Content));
                var akkaConfig = section.AkkaConfig;
                Assert.NotNull(akkaConfig);
            } else
            {
                Output.WriteLine("This test is skipped.");
            }
        }

    }
    class OSInfo
    {
        public bool IsWindows { get; }
        public bool IsLinux { get; }
        public bool IsMacOSX { get; }
        public bool IsUnknownLinux { get; }
        public bool IsUnknown { get; }

        public OSInfo()
        {
            string windir = Environment.GetEnvironmentVariable("windir");
            if (!string.IsNullOrEmpty(windir) && windir.Contains(@"\") && Directory.Exists(windir))
            {
                IsWindows = true;
            }
            else if (File.Exists(@"/proc/sys/kernel/ostype"))
            {
                string osType = File.ReadAllText(@"/proc/sys/kernel/ostype");
                if (osType.StartsWith("Linux", StringComparison.OrdinalIgnoreCase))
                {
                    // Note: Android gets here too
                    IsLinux = true;
                }
                else
                {
                    IsUnknownLinux = true;
                }
            }
            else if (File.Exists(@"/System/Library/CoreServices/SystemVersion.plist"))
            {
                // Note: iOS gets here too
                IsMacOSX = true;
            }
            else
            {
                IsUnknown = true;
            }
        }
    }
}
