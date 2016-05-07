using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    public class NetworkStreamTransportSpec : TransportSpec
    {
        private const string BaseConfig = @"
#akka.loglevel = WARNING
akka.remote {
    flush-wait-on-shutdown = 60s
    use-dispatcher = ""akka.remote.default-remote-dispatcher""
    default-remote-dispatcher = {
        type = ForkJoinDispatcher
        dedicated-thread-pool {
            thread-count = 4
        }
}
";

        // The values are lowered to be able to test them without using too much memory.
        private const string TransportConfig = @"
port = 0
stream-write-buffer-size = 127b
stream-read-buffer-size = 89b
maximum-frame-size = 128000b
chunked-read-threshold = 17b
frame-size-hard-limit = 67108864b
";

        public NetworkStreamTransportSpec()
            : base("networkstream", TransportConfig, BaseConfig)
        { }
    }

    public abstract class TransportSpec : TransportSpecBase
    {
        protected TransportSpec(string transportId, string baseTransportConfig, string baseConfig = null)
            : base(transportId, baseTransportConfig, baseConfig)
        { }

        [Fact]
        public void Connect_two_transports_and_send_ping_pong()
        {
            Association1.Write("ping");
            Association2.ExpectPayload("ping");

            Association2.Write("pong");
            Association1.ExpectPayload("pong");
        }

        [Fact]
        public void Can_call_disassociate_twice_on_association()
        {
            Association1.Disassociate();
            Association1.Disassociate();
            Association2.ExpectDisassociated();
        }

        [Fact]
        public void Can_create_multiple_associations()
        {
            var ass1 = Transport1.Connect(Transport2);
            var ass2 = Transport1.Connect(Transport2);

            Transport2.ExpectInboundAssociation(ass1);
            Transport2.ExpectInboundAssociation(ass2);
        }

        [Fact]
        public void Random_message_size_stress_test()
        {
            int messageCount = 10000;

            RunRandomWrites(Association1, Association2, messageCount).Wait();
        }

        [Fact]
        public void Random_chaos_stress_test()
        {
            int connectionCount = 10;
            int approxMsgPerConnection = 100;

            List<Task> tasks = new List<Task>();

            for (int i = 0; i < connectionCount; i++)
                tasks.Add(RunRandomSession(approxMsgPerConnection));

            Task.WaitAll(tasks.ToArray());
        }
    }
}