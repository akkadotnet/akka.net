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
    /// Specs to verify that the <see cref="Akka.Remote.TestKit.UdpLogger"/> and <see cref="UdpLogCollector"/>
    /// can communicate with eachother
    /// </summary>
    public class UdpLoggerSpecs : AkkaSpec
    {
        private Serializer InternalSerializer => Sys.Serialization.FindSerializerFor(typeof(SpecPass));

        private ByteStringSerializer _serializer;

        protected ByteStringSerializer Serializer => _serializer ?? (_serializer = new ByteStringSerializer(InternalSerializer));

        [Fact]
        public void UdpLogCollectorShouldBindAndReceiveMessages()
        {
            var udpCollector = Sys.ActorOf(Props.Create(() => new UdpLogCollector(TestActor)));

            Udp.Instance.Apply(Sys).Manager.Tell(new Udp.Bind(udpCollector, new IPEndPoint(IPAddress.Loopback, 0)), udpCollector);
            var data = Serializer.ToByteString("foo");
            udpCollector.Tell(new Udp.Received(data, new IPEndPoint(IPAddress.Loopback, 0)));
            ExpectMsg<string>().ShouldBe("foo");
        }

        [Fact]
        public void UdpLoggerShouldConnectAndSendMessages()
        {
            var udpCollector = Sys.ActorOf(Props.Create(() => new UdpLogCollector(TestActor)));
            Udp.Instance.Apply(Sys).Manager.Tell(new Udp.Bind(udpCollector, new IPEndPoint(IPAddress.Loopback, 0)), udpCollector);
            Within(TimeSpan.FromSeconds(3.0), () =>
            {
                var remoteAddress = udpCollector.AskAndWait<EndPoint>(UdpLogCollector.GetLocalAddress.Instance, RemainingOrDefault);
                var udpLogger = Sys.ActorOf(Props.Create(() => new UdpLogger(remoteAddress, true)));
                udpLogger.Tell("foo");
                ExpectMsg<string>().ShouldBe("foo");
            });
            
        }
    }
}
