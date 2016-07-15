//-----------------------------------------------------------------------
// <copyright file="ConfigurationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration.Hocon;
using System.Linq;
using System.Reflection;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Configuration
{
    public class ConfigurationSpec : AkkaSpec
    {
#if CONFIGURATION
        static ConfigurationSpec()
        {
            AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", System.IO.Path.Combine(typeof(ConfigurationSpec).Assembly.Location + ".config"));
            ResetConfigMechanism();
        }

        private static void ResetConfigMechanism()
        {
            typeof(System.Configuration.ConfigurationManager).GetField("s_initState", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0);
            typeof(System.Configuration.ConfigurationManager).GetField("s_configSystem", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            typeof(System.Configuration.ConfigurationManager)
                .Assembly.GetTypes()
                .Where(x => x.FullName == "System.Configuration.ClientConfigPaths")
                .First()
                .GetField("s_current", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, null);
        }
#endif

        public ConfigurationSpec() : base(ConfigurationFactory.Default())
        {
        }

        [Fact]
        public void The_default_configuration_file_contain_all_configuration_properties()
        {
            var settings = Sys.Settings;
            var config = Sys.Settings.Config;

            // settings.ConfigVersion.ShouldBe(ActorSystem.Version);
            settings.Loggers.Count.ShouldBe(1);
            settings.Loggers[0].ShouldBe(typeof(DefaultLogger).FullName);
            // settings.LoggingFilter.ShouldBe(typeof(DefaultLoggingFilter));
            settings.LoggersDispatcher.ShouldBe(Dispatchers.DefaultDispatcherId);
            settings.LoggerStartTimeout.Seconds.ShouldBe(5);
            settings.LogLevel.ShouldBe("INFO");
            settings.StdoutLogLevel.ShouldBe("WARNING");
            settings.LogConfigOnStart.ShouldBeFalse();
            settings.LogDeadLetters.ShouldBe(10);
            settings.LogDeadLettersDuringShutdown.ShouldBeTrue();

            settings.ProviderClass.ShouldBe(typeof(LocalActorRefProvider).FullName);
            settings.SupervisorStrategyClass.ShouldBe(typeof(DefaultSupervisorStrategy).FullName);
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

            settings.SchedulerClass.ShouldBe(typeof(DedicatedThreadScheduler).FullName);
        }

#if CONFIGURATION
        [Fact]
        public void Deserializes_hocon_configuration_from_net_config_file()
        {
            var section = (AkkaConfigurationSection)System.Configuration.ConfigurationManager.GetSection("akka");
            Assert.NotNull(section);
            Assert.False(string.IsNullOrEmpty(section.Hocon.Content));
            var akkaConfig = section.AkkaConfig;
            Assert.NotNull(akkaConfig);
        }
#endif

        [Fact]
        public void Can_create_config_from_source_object()
        {
            var source = new MyObjectConfig
            {
                StringProperty = "aaa",
                BoolProperty = true,
                IntergerArray = new[]{1,2,3,4 }
            };

            var config = ConfigurationFactory.FromObject(source);

            Assert.Equal("aaa", config.GetString("StringProperty"));
            Assert.Equal(true, config.GetBoolean("BoolProperty"));

            Assert.Equal(new[] { 1, 2, 3, 4 }, config.GetIntList("IntergerArray").ToArray());
        }

        [Fact]
        public void Can_merge_objects()
        {
            var hocon1 = @"
a {
    b = 123
    c = 456
    d = 789
    sub {
        aa = 123
    }
}
";

            var hocon2 = @"
a {
    c = 999
    e = 888
    sub {
        bb = 456
    }
}
";

            var root1 = Parser.Parse(hocon1,null);
            var root2 = Parser.Parse(hocon2, null);

            var obj1 = root1.Value.GetObject();
            var obj2 = root2.Value.GetObject();
            obj1.Merge(obj2);

            var config = new Config(root1);

            Assert.Equal(123, config.GetInt("a.b"));
            Assert.Equal(456, config.GetInt("a.c"));
            Assert.Equal(789, config.GetInt("a.d"));
            Assert.Equal(888, config.GetInt("a.e"));
            Assert.Equal(888, config.GetInt("a.e"));
            Assert.Equal(123, config.GetInt("a.sub.aa"));
            Assert.Equal(456, config.GetInt("a.sub.bb"));

        }

        public class MyObjectConfig
        {
            public string StringProperty { get; set; }
            public bool BoolProperty { get; set; }
            public int[] IntergerArray { get; set; }
        }

        [Fact]
        public void Parsing_empty_string_should_produce_empty_hocon_root()
        {
            var value = Parser.Parse(string.Empty, null).Value;
            value.IsEmpty.ShouldBeTrue();
        }

        [Fact]
        public void Config_Empty_is_Empty()
        {
            ConfigurationFactory.Empty.IsEmpty.ShouldBeTrue();
        }
   }
}

