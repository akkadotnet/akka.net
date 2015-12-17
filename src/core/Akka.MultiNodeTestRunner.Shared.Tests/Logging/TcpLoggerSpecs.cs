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
    /// Specs to verify that the <see cref="TcpLogWriter"/> and <see cref="TcpLogCollector"/>
    /// can communicate with eachother
    /// </summary>
    public class TcpLoggerSpecs : AkkaSpec
    {
        [Fact]
        public void TcpLogCollectorShouldBindAndReceiveMessages()
        {
            var tcpCollector = Sys.ActorOf(Props.Create(() => new TcpLogCollector(TestActor)));

            Tcp.Instance.Apply(Sys).Manager.Tell(new Tcp.Bind(tcpCollector, new IPEndPoint(IPAddress.Loopback, 0)), tcpCollector);
            var data = ByteString.FromString("foo");
            tcpCollector.Tell(new Tcp.Received(data));
            ExpectMsg<string>().ShouldBe("foo");
        }

        [Fact]
        public void TcpLoggerShouldConnectAndSendMessages()
        {
            var tcpCollector = Sys.ActorOf(Props.Create(() => new TcpLogCollector(TestActor)));
            Tcp.Instance.Apply(Sys).Manager.Tell(new Tcp.Bind(tcpCollector, new IPEndPoint(IPAddress.Loopback, 0)), tcpCollector);
            Within(TimeSpan.FromSeconds(3.0), () =>
            {
                var remoteAddress = tcpCollector.AskAndWait<EndPoint>(TcpLogCollector.GetLocalAddress.Instance, RemainingOrDefault);
                var udpLogger = Sys.ActorOf(Props.Create(() => new TcpLogWriter(remoteAddress, true)));
                udpLogger.Tell("foo");
                ExpectMsg<string>().ShouldBe("foo");
            });
            
        }
    }
}
