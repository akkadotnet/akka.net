using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using NBench;

namespace Akka.IO.Benchmarks
{
    public class TcpCommunicationBenchmarks
    {
        private const string ElapsedTimeCounterName = "ElapsedTimeCounter";
        
        public const int MessageCount = 100;
        public const int MessageLength = 100;
        private ActorSystem _system;
        private byte[] _message;
        private IActorRef _server;
        private IActorRef _client;
        private Counter _elapsedTimeCounter;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _elapsedTimeCounter = context.GetCounter(ElapsedTimeCounterName);
            
            _system = ActorSystem.Create("system");
            _message = new byte[MessageLength];

            var port = 18765;
            _server = _system.ActorOf(Props.Create(() => new EchoServer(port)));
            _client = _system.ActorOf(Props.Create(() => new Client("127.0.0.1", port)));
        }

        [PerfCleanup]
        public void Cleanup()
        {
            _system.Dispose();
        }

        [PerfBenchmark(
            Description = "Measure Akka.IO.Tcp transport layer performance with simple ping-pong client-server communication",
            NumberOfIterations = 10,
            RunMode = RunMode.Iterations,
            TestMode = TestMode.Measurement
        )]
        [CounterMeasurement(ElapsedTimeCounterName)]
        public void ClientServerBenchmark()
        {
            var watch = Stopwatch.StartNew();
            _client.Ask<CommunicationFinished>(new CommunicationRequest(MessageCount, _message)).Wait();
            watch.Stop();
            _elapsedTimeCounter.Increment(watch.ElapsedTicks);
        }
        
        public class CommunicationRequest
        {
            public CommunicationRequest(int totalMessageCount, byte[] message)
            {
                TotalMessageCount = totalMessageCount;
                Message = message;
            }

            public int TotalMessageCount { get; }
            public byte[] Message { get; }
        }
        
        public class CommunicationFinished { }
        
        private class EchoServer : ReceiveActor
        {
            public EchoServer(int port)
            {
                Context.System.Tcp().Tell(new Tcp.Bind(Self, new IPEndPoint(IPAddress.Any, port)));
                
                Receive<Tcp.Bound>(_ => { });
                Receive<Tcp.Connected>(connected =>
                {
                    var connection = Context.ActorOf(Props.Create(() => new EchoConnection(Sender)));
                    Sender.Tell(new Tcp.Register(connection));
                });
            }
        }
        
        private class EchoConnection : ReceiveActor
        {
            public EchoConnection(IActorRef connection)
            {
                Receive<Tcp.Received>(received =>
                {
                    connection.Tell(Tcp.Write.Create(received.Data));
                });

            }
        }
        
        private class Client : ReceiveActor
        {
            private int _totalMessageCount;
            private byte[] _message;
            private int _receivedCount = 0;
            private readonly DnsEndPoint _endpoint;
            private IActorRef _requester;

            public Client(string host, int port)
            {
                _endpoint = new DnsEndPoint(host, port);
                Become(Waiting);
            }
            
            private void Waiting()
            {
                Receive<CommunicationRequest>(request =>
                {
                    _receivedCount = 0;
                    _totalMessageCount = request.TotalMessageCount;
                    _message = request.Message;
                    _requester = Sender;
                    
                    Context.System.Tcp().Tell(new Tcp.Connect(_endpoint));
                });
                Receive<Tcp.Connected>(_ =>
                {
                    Sender.Tell(new Tcp.Register(Self));
                    Sender.Tell(Tcp.Write.Create(ByteString.FromBytes(_message)));
                    
                    Become(() => Connected(Sender));
                });
                Receive<Tcp.CommandFailed>(_ => throw new Exception("Connection failed"));
            }

            private void Connected(IActorRef connection)
            {
                Receive<Tcp.Received>(_ =>
                {
                    _receivedCount++;
                    if (_receivedCount >= _totalMessageCount)
                    {
                        _requester.Tell(new CommunicationFinished());
                        Become(Waiting);
                    }
                    else
                    {
                        connection.Tell(Tcp.Write.Create(ByteString.FromBytes(_message)));
                    }
                });
            }
        }
    }
}