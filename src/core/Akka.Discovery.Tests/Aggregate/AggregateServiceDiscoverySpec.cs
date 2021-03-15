//-----------------------------------------------------------------------
// <copyright file="AggregateServiceDiscoverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Discovery.Tests.Aggregate
{
    public class StubbedServiceDiscovery : ServiceDiscovery
    {
        public StubbedServiceDiscovery(ExtendedActorSystem system)
        {
        }

        public override Task<Resolved> Lookup(Lookup query, TimeSpan resolveTimeout)
        {
            return query.ServiceName switch
            {
                "stubbed" => Task.FromResult(new Resolved(query.ServiceName, new[] {new ResolvedTarget("stubbed1", 1234)})),
                "fail" => throw new Exception("No resolving for you!"),
                _ => Task.FromResult(new Resolved(query.ServiceName, new List<ResolvedTarget>()))
            };
        }
    }

    public class AggregateServiceDiscoverySpec : TestKit.Xunit2.TestKit
    {
        private static Configuration.Config Config => ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                discovery {
                    method = aggregate
                    aggregate {
                        discovery-methods = [""stubbed1"", ""config""]
                    }
                }
            }
            akka.discovery.stubbed1 {
                class = ""Akka.Discovery.Tests.Aggregate.StubbedServiceDiscovery, Akka.Discovery.Tests""
            }
            akka.discovery.config.services {
                config1 {
                    endpoints = [
                        ""cat:1233"", 
                        ""dog:1234""
                    ]
                }
                fail {
                    endpoints = [
                        ""from-config""
                    ]
                }
            }");

        private readonly ServiceDiscovery _discovery;

        public AggregateServiceDiscoverySpec()
            : base(Config, "AggregateDiscoverySpec")
        {
            _discovery = Discovery.Get(Sys).Default;
        }

        [Fact]
        public void Aggregate_service_discovery_must_only_call_first_one_if_returns_results()
        {
            var result = _discovery.Lookup("stubbed", 100.Milliseconds()).Result;
            result.Should().Be(new ServiceDiscovery.Resolved(
                "stubbed",
                new List<ServiceDiscovery.ResolvedTarget>
                {
                    new ServiceDiscovery.ResolvedTarget("stubbed1", 1234)
                }));
        }

        [Fact]
        public void Aggregate_service_discovery_must_move_onto_the_next_if_no_resolved_targets()
        {
            var result = _discovery.Lookup("config1", 100.Milliseconds()).Result;
            result.Should().Be(new ServiceDiscovery.Resolved(
                "config1",
                new List<ServiceDiscovery.ResolvedTarget>
                {
                    new ServiceDiscovery.ResolvedTarget("cat", 1233),
                    new ServiceDiscovery.ResolvedTarget("dog", 1234)
                }));
        }
        
        [Fact]
        public void Aggregate_service_discovery_must_move_onto_next_if_fails()
        {
            var result = _discovery.Lookup("fail", 100.Milliseconds()).Result;
            // Stub fails then result comes from config
            result.Should().Be(new ServiceDiscovery.Resolved(
                "fail",
                new List<ServiceDiscovery.ResolvedTarget>
                {
                    new ServiceDiscovery.ResolvedTarget("from-config")
                }));
        }
    }
}
