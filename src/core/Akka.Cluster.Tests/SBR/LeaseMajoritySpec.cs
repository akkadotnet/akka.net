//-----------------------------------------------------------------------
// <copyright file="LeaseMajoritySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster.Configuration;
using Akka.Cluster.SBR;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Tests.SBR
{
    public class LeaseMajoritySpec : AkkaSpec
    {
        private readonly Config _default;
        private readonly Config _blank;
        private readonly Config _named;

        public LeaseMajoritySpec(ITestOutputHelper output)
            : base(output)
        {
            _default = ConfigurationFactory.ParseString(@"
                akka.cluster.split-brain-resolver.lease-majority.lease-implementation = ""akka.coordination.lease.kubernetes""
                ")
                .WithFallback(ClusterConfigFactory.Default());

            _blank = ConfigurationFactory.ParseString(@"
                akka.cluster.split-brain-resolver.lease-majority {
                    lease-name = "" ""
                }").WithFallback(_default);

            _named = ConfigurationFactory.ParseString(@"
                akka.cluster.split-brain-resolver.lease-majority {
                    lease-name = ""shopping-cart-akka-sbr""
                }").WithFallback(_default);
        }

        [Fact]
        public void Split_Brain_Resolver_Lease_Majority_provider_must_read_the_configured_name()
        {
            new SplitBrainResolverSettings(_default).LeaseMajoritySettings.LeaseName.Should().BeNull();
            new SplitBrainResolverSettings(_blank).LeaseMajoritySettings.LeaseName.Should().BeNull();
            new SplitBrainResolverSettings(_named).LeaseMajoritySettings.LeaseName.Should().Be("shopping-cart-akka-sbr");
        }

        [Fact]
        public void Split_Brain_Resolver_Lease_Majority_provider_must_use_a_safe_name()
        {
            new SplitBrainResolverSettings(_default).LeaseMajoritySettings.SafeLeaseName("sysName").Should().Be("sysName-akka-sbr");
            new SplitBrainResolverSettings(_blank).LeaseMajoritySettings.SafeLeaseName("sysName").Should().Be("sysName-akka-sbr");
            new SplitBrainResolverSettings(_named).LeaseMajoritySettings.SafeLeaseName("sysName").Should().Be("shopping-cart-akka-sbr");
        }
    }
}
