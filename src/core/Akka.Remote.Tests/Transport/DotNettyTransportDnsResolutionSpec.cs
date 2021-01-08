//-----------------------------------------------------------------------
// <copyright file="DotNettyTransportDnsResolutionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if FSCHECK
using System;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Akka.Util.RuntimeDetector;
using Config = Akka.Configuration.Config;
using FluentAssertions;
// ReSharper disable EmptyGeneralCatchClause

namespace Akka.Remote.Tests.Transport
{

    /// <summary>
    /// Generates a range of random options for DNS
    /// </summary>
    public static class EndpointGenerators
    {
        public static Arbitrary<IPEndPoint> IpEndPoints()
        {
            return Arb.From(Gen.Elements<IPEndPoint>(new IPEndPoint(IPAddress.Loopback, 1337),
               new IPEndPoint(IPAddress.IPv6Loopback, 1337),
               new IPEndPoint(IPAddress.Any, 1337), new IPEndPoint(IPAddress.IPv6Any, 1337)));
        }

        public static Arbitrary<DnsEndPoint> DnsEndPoints()
        {
            return Arb.From(Gen.Elements(new DnsEndPoint("localhost", 0)));
        }

        /// <summary>
        /// Includes IPV4 / IPV6 "any" addresses
        /// </summary>
        /// <returns></returns>
        public static Arbitrary<EndPoint> AllEndpoints()
        {
            return Arb.From(Gen.Elements<EndPoint>(new IPEndPoint(IPAddress.Loopback, 0),
               new IPEndPoint(IPAddress.IPv6Loopback, 0),
               new DnsEndPoint("localhost", 0), new IPEndPoint(IPAddress.Any, 0), new IPEndPoint(IPAddress.IPv6Any, 0)));
        }

        public static string ParseAddress(EndPoint endpoint)
        {
            if (endpoint is IPEndPoint)
                return ((IPEndPoint)endpoint).Address.ToString();
            return ((DnsEndPoint)endpoint).Host;
        }
    }


    /// <summary>
    /// Designed to guarantee that the default Akka.Remote transport "does the right thing" with respect
    /// to DNS resolution and IP binding under a variety of scenarios
    /// </summary>
    public class DotNettyTransportDnsResolutionSpec : AkkaSpec
    {
        public DotNettyTransportDnsResolutionSpec(ITestOutputHelper output) : base(output)
        {
            Arb.Register(typeof(EndpointGenerators));
        }

        public Config BuildConfig(string hostname, int? port = null, string publichostname = null, bool useIpv6 = false, bool enforceIpFamily = false)
        {
            return ConfigurationFactory.ParseString(@"akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""")
                .WithFallback("akka.remote.dot-netty.tcp.hostname =\"" + hostname + "\"")
                .WithFallback("akka.remote.dot-netty.tcp.public-hostname =\"" + (publichostname ?? hostname) + "\"")
                .WithFallback("akka.remote.dot-netty.tcp.port = " + (port ?? 0))
                .WithFallback("akka.remote.dot-netty.tcp.enforce-ip-family = " + enforceIpFamily.ToString().ToLowerInvariant())
                .WithFallback("akka.remote.dot-netty.tcp.dns-use-ipv6 = " + useIpv6.ToString().ToLowerInvariant())
                .WithFallback("akka.test.single-expect-default = 1s")
                .WithFallback(Sys.Settings.Config);
        }

        private class AssociationAcker : ReceiveActor
        {
            public AssociationAcker()
            {
                ReceiveAny(o => Sender.Tell("ack"));
            }
        }

        private void Setup(string inboundHostname, string outboundHostname, string inboundPublicHostname = null, string outboundPublicHostname = null, bool useIpv6Dns = false, bool enforceIpFamily = false)
        {
            _inbound = ActorSystem.Create("Sys1", BuildConfig(inboundHostname, 0, inboundPublicHostname, useIpv6Dns, enforceIpFamily));
            _outbound = ActorSystem.Create("Sys2", BuildConfig(outboundHostname, 0, outboundPublicHostname, useIpv6Dns, enforceIpFamily));

            //InitializeLogger(_inbound);
            //InitializeLogger(_outbound);

            _inbound.ActorOf(Props.Create(() => new AssociationAcker()), "ack");
            _outbound.ActorOf(Props.Create(() => new AssociationAcker()), "ack");

            var addrInbound = RARP.For(_inbound).Provider.DefaultAddress;
            var addrOutbound = RARP.For(_outbound).Provider.DefaultAddress;

            _inboundAck = new RootActorPath(addrInbound) / "user" / "ack";
            _outboundAck = new RootActorPath(addrOutbound) / "user" / "ack";

            _inboundProbe = CreateTestProbe(_inbound);
            _outboundProbe = CreateTestProbe(_outbound);
        }

        private void Cleanup()
        {
            Shutdown(_inbound, TimeSpan.FromSeconds(1));
            Shutdown(_outbound, TimeSpan.FromSeconds(1));
        }

        private ActorSystem _inbound;
        private ActorSystem _outbound;
        private ActorPath _inboundAck;
        private ActorPath _outboundAck;
        private TestProbe _inboundProbe;
        private TestProbe _outboundProbe;

        public static bool IsAnyIp(EndPoint ep)
        {
            var ip = ep as IPEndPoint;
            if (ip == null) return false;
            return (ip.Address.Equals(IPAddress.Any) || ip.Address.Equals(IPAddress.IPv6Any));
        }

        [Property]
        public Property HeliosTransport_Should_Resolve_DNS(EndPoint inbound, EndPoint outbound, bool dnsIpv6, bool enforceIpFamily)
        {
            // TODO: Mono does not support IPV6 Uris correctly https://bugzilla.xamarin.com/show_bug.cgi?id=43649 (Aaronontheweb 8/22/2016)
            if (IsMono)
            {
                enforceIpFamily = true;
                dnsIpv6 = false;
            }


            if (IsAnyIp(inbound) || IsAnyIp(outbound)) return true.Label("Can't connect directly to an ANY address");
            try
            {
                try
                {
                    Setup(EndpointGenerators.ParseAddress(inbound),
                          EndpointGenerators.ParseAddress(outbound),
                          useIpv6Dns: dnsIpv6,
                          enforceIpFamily: enforceIpFamily);
                }
                catch
                {
                    //if ip family is enforced, there are some special cases when it is normal to unable 
                    //to create actor system
                    if (enforceIpFamily && IsExpectedFailure(inbound, outbound, dnsIpv6))
                        return true.ToProperty();
                    throw;
                }

                var outboundReceivedAck = true;
                var inboundReceivedAck = true;
                _outbound.ActorSelection(_inboundAck).Tell("ping", _outboundProbe.Ref);
                try
                {
                    _outboundProbe.ExpectMsg("ack");

                }
                catch
                {
                    outboundReceivedAck = false;
                }

                _inbound.ActorSelection(_outboundAck).Tell("ping", _inboundProbe.Ref);
                try
                {
                    _inboundProbe.ExpectMsg("ack");
                }
                catch
                {
                    inboundReceivedAck = false;
                }

                return outboundReceivedAck.Label($"Expected (outbound: {RARP.For(_outbound).Provider.DefaultAddress}) to be able to successfully message and receive reply from (inbound: {RARP.For(_inbound).Provider.DefaultAddress})")
                          .And(inboundReceivedAck.Label($"Expected (inbound: {RARP.For(_inbound).Provider.DefaultAddress}) to be able to successfully message and receive reply from (outbound: {RARP.For(_outbound).Provider.DefaultAddress})"));
            }
            finally
            {
                Cleanup();
            }
        }

        private static bool IsExpectedFailure(EndPoint inbound,
                                              EndPoint outbound,
                                              bool dnsIpv6)
        {
            /*if ip family is enforced, 
                  it is normal to unable to connect between ips
                  if any of them has not-enforced family 
                  examples: 
                   trying to use ipv4 on both sides when ipv6 is enforced
                   trying to use ipv4 + ipv6 when ipv4 or ipv6 is enforced
                */

            var enforcedFamily = dnsIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            var endpointsIpFamilyMismatch = inbound.AddressFamily != enforcedFamily ||
                                            outbound.AddressFamily != enforcedFamily;

            return endpointsIpFamilyMismatch;
        }

        [Property]
        public Property HeliosTransport_Should_Resolve_DNS_with_PublicHostname(IPEndPoint inbound, DnsEndPoint publicInbound,
            IPEndPoint outbound, DnsEndPoint publicOutbound, bool dnsUseIpv6, bool enforceIpFamily)
        {
            // TODO: Mono does not support IPV6 Uris correctly https://bugzilla.xamarin.com/show_bug.cgi?id=43649 (Aaronontheweb 8/22/2016)
            if (IsMono)
                enforceIpFamily = true;
            if (IsMono && dnsUseIpv6) return true.Label("Mono DNS does not support IPV6 as of 4.4*");
            if (IsMono &&
                (inbound.AddressFamily == AddressFamily.InterNetworkV6 ||
                (outbound.AddressFamily == AddressFamily.InterNetworkV6))) return true.Label("Mono DNS does not support IPV6 as of 4.4*");

            if (dnsUseIpv6 &&
                (inbound.AddressFamily == AddressFamily.InterNetwork ||
                 (outbound.AddressFamily == AddressFamily.InterNetwork))) return true.Label("Can't connect to IPV4 socket using IPV6 DNS resolution");
            if (!dnsUseIpv6 &&
                (inbound.AddressFamily == AddressFamily.InterNetworkV6 ||
                 (outbound.AddressFamily == AddressFamily.InterNetworkV6))) return true.Label("Need to apply DNS resolution and IP stack verison consistently.");


            try
            {
                try
                {
                    Setup(EndpointGenerators.ParseAddress(inbound),
                          EndpointGenerators.ParseAddress(outbound),
                          EndpointGenerators.ParseAddress(publicInbound),
                          EndpointGenerators.ParseAddress(publicOutbound),
                          dnsUseIpv6,
                          enforceIpFamily);
                }
                catch
                {
                    //if ip family is enforced, there are some special cases when it is normal to unable 
                    //to create actor system
                    if (enforceIpFamily && IsExpectedFailure(inbound, outbound, dnsUseIpv6))
                        return true.ToProperty();
                    throw;
                }
                var outboundReceivedAck = true;
                var inboundReceivedAck = true;
                _outbound.ActorSelection(_inboundAck).Tell("ping", _outboundProbe.Ref);
                try
                {
                    _outboundProbe.ExpectMsg("ack");

                }
                catch
                {
                    outboundReceivedAck = false;
                }

                _inbound.ActorSelection(_outboundAck).Tell("ping", _inboundProbe.Ref);
                try
                {
                    _inboundProbe.ExpectMsg("ack");
                }
                catch
                {
                    inboundReceivedAck = false;
                }


                return outboundReceivedAck.Label($"Expected (outbound: {RARP.For(_outbound).Provider.DefaultAddress}) to be able to successfully message and receive reply from (inbound: {RARP.For(_inbound).Provider.DefaultAddress})")
                        .And(inboundReceivedAck.Label($"Expected (inbound: {RARP.For(_inbound).Provider.DefaultAddress}) to be able to successfully message and receive reply from (outbound: {RARP.For(_outbound).Provider.DefaultAddress})"));
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// Testing for IPV6 issues
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        [Property]
        public Property HeliosTransport_should_map_valid_IPEndpoints_to_Address(IPEndPoint endpoint)
        {
            var addr = DotNettyTransport.MapSocketToAddress(endpoint, "akka.tcp", "foo");
            var parsedEp = (IPEndPoint)DotNettyTransport.AddressToSocketAddress(addr);
            return endpoint.Equals(parsedEp).Label("Should be able to parse endpoint to address and back");
        }

        /// <summary>
        /// Testing public port
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        [Property]
        public Property DotNettyTransport_should_map_valid_IPEndpoints_to_Address_when_using_publicport(IPEndPoint endpoint)
        {
            var addr = DotNettyTransport.MapSocketToAddress(endpoint, "akka.tcp", "foo", publicPort: 1234);
            var parsedEp = (IPEndPoint)DotNettyTransport.AddressToSocketAddress(addr);
            var expectedEndpoint = new IPEndPoint(endpoint.Address, 1234);
            return expectedEndpoint.Equals(parsedEp).Label("Should be able to parse endpoint with publicport");
        }

        [Fact]
        public void DotNettyTransport_should_parse_publicport()
        {
            var config = @"
                public-hostname = localhost
                hostname = 127.0.0.1
                port = 8180
                public-port = 10110
            ";

            var settings = DotNettyTransportSettings.Create(config);
            settings.Port.Should().Be(8180);
            settings.PublicPort.Should().Be(10110);
        }

        /// <summary>
        /// Testing for IPV6 issues
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        [Property]
        public Property HeliosTransport_should_map_valid_IPEndpoints_to_ActorPath(IPEndPoint endpoint)
        {
            // TODO: remove this once Mono Uris support IPV6 https://bugzilla.xamarin.com/show_bug.cgi?id=43649 (8/22/2016 Aaronontheweb)
            if (IsMono && endpoint.AddressFamily == AddressFamily.InterNetworkV6)
                return true.Label("Mono does not currently support Uri.TryParse for IPV6");
            var addr = DotNettyTransport.MapSocketToAddress(endpoint, "akka.tcp", "foo");
            var actorPath = new RootActorPath(addr) / "user" / "foo";
            var serializationFormat = actorPath.ToSerializationFormat();
            var reparsedActorPath = ActorPath.Parse(serializationFormat);
            return actorPath.Equals(reparsedActorPath).Label($"Should be able to parse endpoint to ActorPath and back; expected {actorPath.ToSerializationFormat()} but was {reparsedActorPath.ToSerializationFormat()}");
        }
    }
}
#endif
