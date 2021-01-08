//-----------------------------------------------------------------------
// <copyright file="RemotePathParsingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.TestKit;
using FsCheck;
using FsCheck.Xunit;
using static Akka.Util.RuntimeDetector;

namespace Akka.Tests.Actor
{
    /// <summary>
    /// Generates a range of random options for DNS
    /// </summary>
    public static class EndpointGenerators
    {
        public static Arbitrary<IPEndPoint> IpEndPoints()
        {
            // TODO: Mono does not support IPV6 Uris correctly https://bugzilla.xamarin.com/show_bug.cgi?id=43649 (Aaronontheweb 9/13/2016)
            if (IsMono)
                return Arb.From(Gen.Elements<IPEndPoint>(new IPEndPoint(IPAddress.Loopback, 1337),
               new IPEndPoint(IPAddress.Any, 1337)));
            return Arb.From(Gen.Elements<IPEndPoint>(new IPEndPoint(IPAddress.Loopback, 1337),
              new IPEndPoint(IPAddress.IPv6Loopback, 1337),
              new IPEndPoint(IPAddress.Any, 1337), new IPEndPoint(IPAddress.IPv6Any, 1337)));
        }

        /// <summary>
        /// Includes IPV4 / IPV6 "any" addresses
        /// </summary>
        /// <returns></returns>
        public static Arbitrary<EndPoint> AllEndpoints()
        {
            // TODO: Mono does not support IPV6 Uris correctly https://bugzilla.xamarin.com/show_bug.cgi?id=43649 (Aaronontheweb 9/13/2016)
            if (IsMono)
                return Arb.From(Gen.Elements<EndPoint>(new IPEndPoint(IPAddress.Loopback, 1337),
               new DnsEndPoint("localhost", 1337), new IPEndPoint(IPAddress.Any, 1337)));
            return Arb.From(Gen.Elements<EndPoint>(new IPEndPoint(IPAddress.Loopback, 1337),
               new IPEndPoint(IPAddress.IPv6Loopback, 1337),
               new DnsEndPoint("localhost", 1337), new IPEndPoint(IPAddress.Any, 1337),
               new IPEndPoint(IPAddress.IPv6Any, 1337)));
        }

        public static string ExtractHost(EndPoint endpoint)
        {
            if (endpoint is IPEndPoint)
                return ((IPEndPoint)endpoint).Address.ToString();
            return ((DnsEndPoint)endpoint).Host;
        }


        public static Address ParseAddress(EndPoint ep)
        {
            bool isIp = ep is IPEndPoint;
            IPEndPoint ip = ep as IPEndPoint;
            DnsEndPoint dns = ep as DnsEndPoint;
            var addr = new Address("akka.tcp", "foo", isIp
                ? (ip.Address.AddressFamily == AddressFamily.InterNetworkV6
                    ? // have to explicitly check for IPV6 and bracket it
                    "[" + ip.Address + "]"
                    : ip.Address.ToString())
                : dns.Host, isIp ? ip.Port : dns.Port);
            return addr;
        }

    }

    /// <summary>
    /// Used to verify that actor primitives like <see cref="Address"/> and <see cref="ActorPath"/> can properly
    /// handle each of the following scenarios: IPV4, IVP6, DNS
    /// </summary>
    public class RemotePathParsingSpec : AkkaSpec
    {
        public RemotePathParsingSpec()
        {
            Arb.Register(typeof(EndpointGenerators));
        }

        [Property]
        public Property Address_should_parse_from_any_valid_EndPoint(EndPoint ep)
        {
            var addr = EndpointGenerators.ParseAddress(ep);
            var parsedAddr = Address.Parse(addr.ToString());
            return parsedAddr.Equals(addr).Label($"Should be able to parse endpoint to address and back; expected {addr} but was {parsedAddr}");
        }

        [Property]
        public Property ActorPath_Should_parse_from_any_valid_EndPoint(EndPoint ep)
        {
            var addr = EndpointGenerators.ParseAddress(ep);
            var actorPath = new RootActorPath(addr) / "user" / "foo";
            var serializationFormat = actorPath.ToSerializationFormat();
            var reparsedActorPath = ActorPath.Parse(serializationFormat);
            return actorPath.Equals(reparsedActorPath).Label($"Should be able to parse endpoint to ActorPath and back; expected {actorPath.ToSerializationFormat()} but was {reparsedActorPath.ToSerializationFormat()}");
        }
    }
}
