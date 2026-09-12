//-----------------------------------------------------------------------
// <copyright file="DowningProviderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit.Attributes;
using Akka.Util;
using FluentAssertions;
using Xunit;
using TestAutoDowning = Akka.Cluster.TestKit.AutoDowning;

namespace Akka.Cluster.Tests
{
    internal class FailingDowningProvider : IDowningProvider
    {
        public FailingDowningProvider(ActorSystem system, Cluster cluster)
        {
        }

        public TimeSpan DownRemovalMargin { get; } = TimeSpan.FromSeconds(20);

        public Props DowningActorProps
        {
            get
            {
                throw new ConfigurationException("this provider never works");
            }
        }
    }

    internal class DummyDowningProvider : IDowningProvider
    {
        public readonly AtomicBoolean ActorPropsAccessed = new(false);
        public DummyDowningProvider(ActorSystem system, Cluster cluster)
        {
        }

        public TimeSpan DownRemovalMargin { get; } = TimeSpan.FromSeconds(20);

        public Props DowningActorProps
        {
            get
            {
                ActorPropsAccessed.Value = true;
                return null;
            }
        }
    }

    public class DowningProviderSpec : AkkaSpec
    {
        public DowningProviderSpec(ITestOutputHelper output)
            : base(output)
        {
        }

        public readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
          akka {
            loglevel = WARNING
            actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            remote {
              dot-netty.tcp {
                hostname = ""127.0.0.1""
                port = 0
              }
            }
          }
        ");

        [Fact]
        public void Downing_provider_should_default_to_KeepMajority()
        {
            using (var system = ActorSystem.Create("default", BaseConfig))
            {
                Cluster.Get(system).DowningProvider.Should().BeOfType<Akka.Cluster.SBR.SplitBrainResolverProvider>();
            }
        }

        [Fact]
        public void Downing_provider_should_ignore_removed_auto_down_setting()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.cluster.downing-provider-class = """"
                akka.cluster.auto-down-unreachable-after=18s");
            using (var system = ActorSystem.Create("auto-downing", config.WithFallback(BaseConfig)))
            {
                Cluster.Get(system).DowningProvider.Should().BeOfType<NoDowning>();
            }
        }

        [Fact(DisplayName = "Active removed auto-down setting must produce migration warning text")]
        public void Downing_provider_should_produce_warning_when_removed_auto_down_setting_is_active()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.cluster.auto-down-unreachable-after = 18s");

            Cluster.GetRemovedAutoDownWarning(config).Should()
                .Contain("`akka.cluster.auto-down-unreachable-after` was removed in Akka.NET v1.6 and is ignored.");
        }

        [Theory(DisplayName = "Disabled removed auto-down setting must not log a warning")]
        [InlineData("off")]
        [InlineData("false")]
        [InlineData("no")]
        public void Downing_provider_should_not_warn_when_removed_auto_down_setting_is_disabled(string configuredValue)
        {
            var config = ConfigurationFactory.ParseString($@"
                akka.cluster.auto-down-unreachable-after = {configuredValue}");

            Cluster.GetRemovedAutoDownWarning(config).Should().BeNull();
        }

        [Fact(DisplayName = "TestKit AutoDowning configuration must load the TestKit provider")]
        public async Task Downing_provider_should_load_testkit_auto_downing_from_generated_config()
        {
            var delay = TimeSpan.FromSeconds(18);
            var config = TestAutoDowning.GetConfig(delay).WithFallback(BaseConfig);

            config.GetString("akka.cluster.downing-provider-class")
                .Should().Be(typeof(TestAutoDowning).AssemblyQualifiedName);
            config.GetTimeSpan("akka.cluster.testkit.auto-down-unreachable-after", null)
                .Should().Be(delay);

            var system = ActorSystem.Create("testkit-auto-downing", config);
            try
            {
                var provider = Cluster.Get(system).DowningProvider;
                provider.Should().BeOfType<TestAutoDowning>();
                provider.DowningActorProps.Should().NotBeNull();
                provider.DowningActorProps!.NewActor().Should().NotBeNull();
            }
            finally
            {
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
            }
        }

        [Fact(DisplayName = "TestKit AutoDowning configuration must reject a negative delay")]
        public void Downing_provider_should_reject_negative_testkit_auto_down_delay()
        {
            var getConfig = () => TestAutoDowning.GetConfig(TimeSpan.FromMilliseconds(-1));

            getConfig.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void Downing_provider_should_use_specified_downing_provider()
        {
            var config = ConfigurationFactory.ParseString(
                @"akka.cluster.downing-provider-class = ""Akka.Cluster.Tests.DummyDowningProvider, Akka.Cluster.Tests""");
            using (var system = ActorSystem.Create("auto-downing", config.WithFallback(BaseConfig)))
            {
                var downingProvider = Cluster.Get(system).DowningProvider;
                downingProvider.Should().BeOfType<DummyDowningProvider>();
                AwaitCondition(() =>
                    ((DummyDowningProvider)downingProvider).ActorPropsAccessed.Value,
                    TimeSpan.FromSeconds(3));
            }
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public void Downing_provider_should_stop_the_cluster_if_the_downing_provider_throws_exception_in_props()
        {
            var config = ConfigurationFactory.ParseString(
                @"akka.cluster.downing-provider-class = ""Akka.Cluster.Tests.FailingDowningProvider, Akka.Cluster.Tests""");

            var system = ActorSystem.Create("auto-downing", config.WithFallback(BaseConfig));

            var cluster = Cluster.Get(system);
            cluster.Join(cluster.SelfAddress);

            AwaitCondition(() => cluster.IsTerminated, TimeSpan.FromSeconds(3));

            Shutdown(system);
        }
    }
}
