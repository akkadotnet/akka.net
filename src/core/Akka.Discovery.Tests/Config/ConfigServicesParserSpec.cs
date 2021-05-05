//-----------------------------------------------------------------------
// <copyright file="ConfigServicesParserSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Configuration;
using Akka.Discovery.Config;
using FluentAssertions;
using Xunit;

namespace Akka.Discovery.Tests.Config
{
    public class ConfigServicesParserSpec
    {
        private static Configuration.Config Config => ConfigurationFactory.ParseString(@"
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
            }");

        [Fact]
        public void Config_parsing_must_parse_services()
        {
            var config = Config.GetConfig("services");
            var result = ConfigServicesParser.Parse(config);

            result["service1"].Should().Be(new ServiceDiscovery.Resolved(
                "service1",
                new[]
                {
                    new ServiceDiscovery.ResolvedTarget("cat", 1233),
                    new ServiceDiscovery.ResolvedTarget("dog")
                }));
            
            result["service2"].Should().Be(new ServiceDiscovery.Resolved(
                "service2", 
                ImmutableList<ServiceDiscovery.ResolvedTarget>.Empty));            
        }
    }
}
