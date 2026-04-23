//-----------------------------------------------------------------------
// <copyright file="DotNettyTransportLinkLocalFilterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Akka.Remote.Transport.DotNetty;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Regression test for https://github.com/akkadotnet/akka.net/issues/8178
    /// Filters link-local (169.254.x.x) and IPv6 link-local (fe80::/10) addresses from DNS results
    /// to prevent cluster formation failures on multi-NIC Windows hosts with APIPA addresses.
    /// </summary>
    public class DotNettyTransportLinkLocalFilterSpec
    {
        [Fact]
        public void FilterLinkLocalAddresses_should_remove_ipv4_link_local_addresses()
        {
            var addresses = new IPAddress[]
            {
                IPAddress.Parse("10.0.0.5"),
                IPAddress.Parse("169.254.1.1"),
                IPAddress.Parse("192.168.1.10")
            };

            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses);

            filtered.Should().HaveCount(2);
            filtered.Should().Contain(IPAddress.Parse("10.0.0.5"));
            filtered.Should().Contain(IPAddress.Parse("192.168.1.10"));
            filtered.Should().NotContain(IPAddress.Parse("169.254.1.1"));
        }

        [Fact]
        public void FilterLinkLocalAddresses_should_remove_ipv6_link_local_addresses()
        {
            var addresses = new IPAddress[]
            {
                IPAddress.Parse("fe80::1"),
                IPAddress.Parse("2001:db8::1")
            };

            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses);

            filtered.Should().HaveCount(1);
            filtered.Should().Contain(IPAddress.Parse("2001:db8::1"));
            filtered.Should().NotContain(IPAddress.Parse("fe80::1"));
        }

        [Fact]
        public void FilterLinkLocalAddresses_should_keep_loopback_addresses()
        {
            // localhost should still resolve correctly
            var addresses = new IPAddress[]
            {
                IPAddress.Loopback,
                IPAddress.IPv6Loopback
            };

            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses);

            filtered.Should().HaveCount(2);
            filtered.Should().Contain(IPAddress.Loopback);
            filtered.Should().Contain(IPAddress.IPv6Loopback);
        }

        [Fact]
        public void FilterLinkLocalAddresses_should_return_empty_when_only_link_local()
        {
            var addresses = new IPAddress[]
            {
                IPAddress.Parse("169.254.1.1"),
                IPAddress.Parse("169.254.2.2")
            };

            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses);

            filtered.Should().BeEmpty();
        }

        [Fact]
        public void FilterLinkLocalAddresses_should_keep_all_normal_addresses()
        {
            var addresses = new IPAddress[]
            {
                IPAddress.Parse("10.0.0.5"),
                IPAddress.Parse("172.16.0.1"),
                IPAddress.Parse("192.168.1.10"),
                IPAddress.Loopback,
                IPAddress.IPv6Loopback,
                IPAddress.Parse("2001:db8::1")
            };

            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses);

            filtered.Should().HaveCount(addresses.Length);
            filtered.Should().BeEquivalentTo(addresses);
        }

        [Fact]
        public void FilterLinkLocalAddresses_should_mixed_ipv4_and_ipv6()
        {
            var addresses = new IPAddress[]
            {
                IPAddress.Parse("10.0.0.5"),
                IPAddress.Parse("169.254.1.1"),
                IPAddress.Parse("fe80::1"),
                IPAddress.Parse("2001:db8::1")
            };

            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses);

            filtered.Should().HaveCount(2);
            filtered.Should().Contain(IPAddress.Parse("10.0.0.5"));
            filtered.Should().Contain(IPAddress.Parse("2001:db8::1"));
        }

        [Fact]
        public void FilterLinkLocalAddresses_should_handle_empty_array()
        {
            var addresses = Array.Empty<IPAddress>();

            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses);

            filtered.Should().BeEmpty();
        }

        [Fact]
        public void FilterLinkLocalAddresses_should_preserve_order()
        {
            var addresses = new IPAddress[]
            {
                IPAddress.Parse("192.168.1.10"),
                IPAddress.Parse("10.0.0.5"),
                IPAddress.Parse("169.254.1.1")
            };

            var filtered = DotNettyTransport.FilterLinkLocalAddresses(addresses);

            filtered.Should().HaveCount(2);
            filtered[0].Should().Be(IPAddress.Parse("192.168.1.10"));
            filtered[1].Should().Be(IPAddress.Parse("10.0.0.5"));
        }
    }
}
