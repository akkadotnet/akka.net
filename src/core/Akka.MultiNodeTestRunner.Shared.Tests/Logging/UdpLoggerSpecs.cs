using System;
using System.Net;
using Akka.Actor;
using Akka.IO;
using Akka.MultiNodeTestRunner.Shared.Logging;
using Akka.Remote.TestKit;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.MultiNodeTestRunner.Shared.Tests.Logging
{
    /// <summary>
    /// Specs to verify that the <see cref="UdpLogWriter"/> and <see cref="TcpLogCollector"/>
    /// can communicate with eachother
    /// </summary>
    public class UdpLoggerSpecs : AkkaSpec
    {
        [Fact]
        public void UdpLogCollectorShouldBindAndReceiveMessages()
        {
            var udpCollector = Sys.ActorOf(Props.Create(() => new TcpLogCollector(TestActor)));

            Udp.Instance.Apply(Sys).Manager.Tell(new Udp.Bind(udpCollector, new IPEndPoint(IPAddress.Loopback, 0)), udpCollector);
            var data = ByteString.FromString("foo");
            udpCollector.Tell(new Udp.Received(data, new IPEndPoint(IPAddress.Loopback, 0)));
            ExpectMsg<string>().ShouldBe("foo");
        }

        [Fact]
        public void UdpLoggerShouldConnectAndSendMessages()
        {
            var udpCollector = Sys.ActorOf(Props.Create(() => new TcpLogCollector(TestActor)));
            Udp.Instance.Apply(Sys).Manager.Tell(new Udp.Bind(udpCollector, new IPEndPoint(IPAddress.Loopback, 0)), udpCollector);
            Within(TimeSpan.FromSeconds(3.0), () =>
            {
                var remoteAddress = udpCollector.AskAndWait<EndPoint>(TcpLogCollector.GetLocalAddress.Instance, RemainingOrDefault);
                var udpLogger = Sys.ActorOf(Props.Create(() => new UdpLogWriter(remoteAddress, true)));
                udpLogger.Tell("foo");
                ExpectMsg<string>().ShouldBe("foo");
            });
            
        }
    }
}
