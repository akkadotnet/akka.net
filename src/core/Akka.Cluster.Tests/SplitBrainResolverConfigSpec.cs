//-----------------------------------------------------------------------
// <copyright file="SplitBrainResolverConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class SplitBrainResolverConfigSpec
    {
        [Fact]
        public void StaticQuorum_must_parse_settings_from_config()
        {
            var config = ConfigurationFactory.ParseString(@"
                quorum-size = 5
                role = test-role");
            var strategy = new StaticQuorum(config);

            strategy.QuorumSize.Should().Be(5);
            strategy.Role.Should().Be("test-role");
        }

        [Fact]
        public void KeepMajority_must_parse_settings_from_config()
        {
            var config = ConfigurationFactory.ParseString(@"
                role = test-role");
            var strategy = new KeepMajority(config);
            
            strategy.Role.Should().Be("test-role");
        }

        [Fact]
        public void KeepOldest_must_parse_settings_from_config()
        {
            var config = ConfigurationFactory.ParseString(@"
                down-if-alone = true
                role = test-role");
            var strategy = new KeepOldest(config);

            strategy.Role.Should().Be("test-role");
            strategy.DownIfAlone.Should().Be(true);
        }

        [Fact]
        public void KeepReferee_must_parse_settings_from_config()
        {
            var config = ConfigurationFactory.ParseString(@"
                address = ""akka.tcp://system@localhost:5000/""
                down-all-if-less-than-nodes = 3");
            var strategy = new KeepReferee(config);

            strategy.Role.Should().BeNull();
            strategy.DownAllIfLessThanNodes.Should().Be(3);
            strategy.Address.Should().Be(Address.Parse("akka.tcp://system@localhost:5000/"));
        }

        [Fact]
        public void SplitBrainResolver_should_be_picked_up_if_set_as_downing_provider()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka {
                    actor.provider = cluster
                    remote.dot-netty.tcp.port = 0
                    cluster {
                        down-removal-margin = 10s
                        downing-provider-class = ""Akka.Cluster.SplitBrainResolver, Akka.Cluster""
                        split-brain-resolver {
                            stable-after = 40s
                            active-strategy = static-quorum
                            static-quorum.quorum-size = 3
                        }
                    }
                }");

            using (var system = ActorSystem.Create("system", config))
            {
                var cluster = Cluster.Get(system);

                cluster.DowningProvider.Should().BeOfType<SplitBrainResolver>();
                var provider = (SplitBrainResolver)cluster.DowningProvider;

                provider.Strategy.Should().BeOfType<StaticQuorum>();
                provider.DownRemovalMargin.Should().Be(TimeSpan.FromSeconds(10));
                provider.StableAfter.Should().Be(TimeSpan.FromSeconds(40));
            }
        }

        [Fact]
        public void SplitBrainResolver_should_work_with_default_timeouts()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka {
                    actor.provider = cluster
                    remote.dot-netty.tcp.port = 0
                    cluster {
                        downing-provider-class = ""Akka.Cluster.SplitBrainResolver, Akka.Cluster""
                        split-brain-resolver {
                            active-strategy = keep-majority
                        }
                    }
                }");

            using (var system = ActorSystem.Create("system", config))
            {
                var cluster = Cluster.Get(system);

                cluster.DowningProvider.Should().BeOfType<SplitBrainResolver>();
                var provider = (SplitBrainResolver)cluster.DowningProvider;

                provider.Strategy.Should().BeOfType<KeepMajority>();
                provider.DownRemovalMargin.Should().Be(TimeSpan.Zero);      // default is zero
                provider.StableAfter.Should().Be(TimeSpan.FromSeconds(20)); // default is 20s
            }
        }
    }
}
