//-----------------------------------------------------------------------
// <copyright file="DotNettyTransportLinkLocalFilterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Remote.Transport.DotNetty;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Regression test for https://github.com/akkadotnet/akka.net/issues/8178
    /// </summary>
    public class DotNettyTransportLinkLocalFilterSpec
    {
        public static IEnumerable<object[]> FilterCases()
        {
            // removes IPv4 link-local
            yield return new object[]
            {
                new[] { "10.0.0.5", "169.254.1.1", "192.168.1.10" },
                new[] { "10.0.0.5", "192.168.1.10" }
            };

            // removes IPv6 link-local
            yield return new object[]
            {
                new[] { "fe80::1", "2001:db8::1" },
                new[] { "2001:db8::1" }
            };

            // keeps loopback addresses
            yield return new object[]
            {
                new[] { "127.0.0.1", "::1" },
                new[] { "127.0.0.1", "::1" }
            };

            // returns empty when only link-local
            yield return new object[]
            {
                new[] { "169.254.1.1", "169.254.2.2" },
                new string[] { }
            };

            // keeps all normal addresses
            yield return new object[]
            {
                new[] { "10.0.0.5", "172.16.0.1", "192.168.1.10", "127.0.0.1", "::1", "2001:db8::1" },
                new[] { "10.0.0.5", "172.16.0.1", "192.168.1.10", "127.0.0.1", "::1", "2001:db8::1" }
            };

            // mixed IPv4 and IPv6 link-local
            yield return new object[]
            {
                new[] { "10.0.0.5", "169.254.1.1", "fe80::1", "2001:db8::1" },
                new[] { "10.0.0.5", "2001:db8::1" }
            };

            // empty input
            yield return new object[]
            {
                new string[] { },
                new string[] { }
            };

            // preserves order
            yield return new object[]
            {
                new[] { "192.168.1.10", "10.0.0.5", "169.254.1.1" },
                new[] { "192.168.1.10", "10.0.0.5" }
            };
        }

        [Theory]
        [MemberData(nameof(FilterCases))]
        public void FilterLinkLocalAddresses_should_return_expected_results(
            string[] input, string[] expected)
        {
            var addresses = input.Select(IPAddress.Parse).ToArray();
            var expectedAddresses = expected.Select(IPAddress.Parse).ToArray();

            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses).ToArray();

            filtered.Should().Equal(expectedAddresses);
        }
    }
}
