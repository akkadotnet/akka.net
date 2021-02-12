//-----------------------------------------------------------------------
// <copyright file="DiscoveryConfigurationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Discovery.Tests
{
    public class DiscoveryConfigurationSpec : TestKit.Xunit2.TestKit
    {
        [Fact]
        public void ServiceDiscovery_should_throw_when_no_default_discovery_configured()
        {
            using var sys = ActorSystem.Create("DiscoveryConfigurationSpec");
            var ex = Assert.Throws<ArgumentException>(() => _ = Discovery.Get(sys).Default);
            ex.Message.Should().Contain("No default service discovery implementation configured");
        }

        [Fact]
        public void ServiceDiscovery_should_select_implementation_from_config_by_config_name_inside_namespace()
        {
            var className = typeof(FakeTestDiscovery).TypeQualifiedName();

            using var sys = ActorSystem.Create(
                "DiscoveryConfigurationSpec",
                ConfigurationFactory.ParseString($@"            
                    akka.discovery {{
                        method = akka-mock-inside
                        akka-mock-inside {{
                            class = ""{className}""
                        }}
                    }}").WithFallback(ConfigurationFactory.Load()));

            var discovery = Discovery.Get(sys).Default;
            discovery.Should().BeAssignableTo<FakeTestDiscovery>();
        }

        [Fact]
        public void ServiceDiscovery_should_load_another_implementation_from_config_by_config_name()
        {
            var className1 = typeof(FakeTestDiscovery).TypeQualifiedName();
            var className2 = typeof(FakeTestDiscovery2).TypeQualifiedName();

            using var sys = ActorSystem.Create(
                "DiscoveryConfigurationSpec",
                ConfigurationFactory.ParseString($@"            
                    akka.discovery {{
                        method = mock1
                        mock1 {{
                            class = ""{className1}""
                        }}
                        mock2 {{
                            class = ""{className2}""
                        }}
                    }}").WithFallback(ConfigurationFactory.Load()));

            Discovery.Get(sys).Default.Should().BeAssignableTo<FakeTestDiscovery>();
            Discovery.Get(sys).LoadServiceDiscovery("mock2").Should().BeAssignableTo<FakeTestDiscovery2>();
        }

        [Fact]
        public void ServiceDiscovery_should_return_same_instance_for_same_method()
        {
            var className1 = typeof(FakeTestDiscovery).TypeQualifiedName();
            var className2 = typeof(FakeTestDiscovery2).TypeQualifiedName();

            using var sys = ActorSystem.Create(
                "DiscoveryConfigurationSpec",
                ConfigurationFactory.ParseString($@"            
                    akka.discovery {{
                        method = mock1
                        mock1 {{
                            class = ""{className1}""
                        }}
                        mock2 {{
                            class = ""{className2}""
                        }}
                    }}").WithFallback(ConfigurationFactory.Load()));

            Discovery.Get(sys).LoadServiceDiscovery("mock2").Should().BeSameAs(Discovery.Get(sys).LoadServiceDiscovery("mock2"));
            Discovery.Get(sys).Default.Should().BeSameAs(Discovery.Get(sys).LoadServiceDiscovery("mock1"));
        }

        [Fact]
        public void ServiceDiscovery_should_throw_a_specific_discovery_method_exception()
        {
            var className = typeof(ExceptionThrowingDiscovery).TypeQualifiedName();

            using var sys = ActorSystem.Create(
                "DiscoveryConfigurationSpec",
                ConfigurationFactory.ParseString($@"            
                    akka.discovery {{
                        method = mock1
                        mock1 {{
                            class = ""{className}""
                        }}
                    }}").WithFallback(ConfigurationFactory.Load()));

            Action discoveryInstance = () => _ = Discovery.Get(sys).Default;
            discoveryInstance
                .Should().Throw<TargetInvocationException>()
                .WithInnerException<DiscoveryException>();
        }

        [Fact]
        public void ServiceDiscovery_should_throw_an_argument_exception_for_not_existing_method()
        {
            const string className = "className";

            using var sys = ActorSystem.Create(
                "DiscoveryConfigurationSpec",
                ConfigurationFactory.ParseString($@"            
                    akka.discovery {{
                        method = ""{className}""
                    }}").WithFallback(ConfigurationFactory.Load()));

            Assert.Throws<ArgumentException>(() => _ = Discovery.Get(sys).Default);
        }
    }

    internal class FakeTestDiscovery : ServiceDiscovery
    {
        public override Task<Resolved> Lookup(Lookup lookup, TimeSpan resolveTimeout) =>
            Task.FromResult((Resolved)null);
    }

    internal class FakeTestDiscovery2 : FakeTestDiscovery
    { }

    internal class DiscoveryException : Exception
    {
        public DiscoveryException()
        { }

        public DiscoveryException(string message)
            : base(message)
        { }

        public DiscoveryException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }

    internal class ExceptionThrowingDiscovery : FakeTestDiscovery
    {
        public ExceptionThrowingDiscovery() => Bad();

        private static void Bad() => throw new DiscoveryException("oh no");
    }
}
