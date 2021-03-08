//-----------------------------------------------------------------------
// <copyright file="ConfigServiceDiscoverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Discovery.Tests.Config
{
    public class ConfigServiceDiscoverySpec : TestKit.Xunit2.TestKit
    {
        private static Configuration.Config Config => ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                discovery {
                    method = config
                    config {
                        services {
                            service1 {
                                endpoints = [
                                    ""cat:1233"",
                                    ""dog""
                                ]
                            }
                            service2 {
                                endpoints = []
                            }
                        }
                    }
                }
            }");

        private readonly ServiceDiscovery _discovery;

        public ConfigServiceDiscoverySpec()
            : base(Config, "ConfigDiscoverySpec")
        {
            _discovery = Discovery.Get(Sys).Default;
        }

        [Fact]
        public void Config_discovery_must_load_from_config()
        {
            var result = _discovery.Lookup("service1", 100.Milliseconds()).Result;
            result.ServiceName.Should().Be("service1");
            result.Addresses.Should().Contain(new[]
            {
                new ServiceDiscovery.ResolvedTarget("cat", 1233),
                new ServiceDiscovery.ResolvedTarget("dog")
            });
        }

        [Fact]
        public void Config_discovery_must_return_no_resolved_targets_if_not_in_config()
        {
            var result = _discovery.Lookup("dontexist", 100.Milliseconds()).Result;
            result.ServiceName.Should().Be("dontexist");
            result.Addresses.Should().BeEmpty();
        }
    }
}
