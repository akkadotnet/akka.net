//-----------------------------------------------------------------------
// <copyright file="TcpHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Reactive.Streams;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.IO
{
    public abstract class TcpHelper : AkkaSpec
    {
        protected TcpHelper(string config, ITestOutputHelper helper)
            : base(
                ConfigurationFactory.ParseString(config)
                    .WithFallback(StreamTestDefaultMailbox.DefaultConfig),
                helper)
        {
            Settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(4, 4);
            Materializer = Sys.Materializer(Settings);
        }

        protected ActorMaterializerSettings Settings { get; }
        protected ActorMaterializer Materializer { get; }

        protected sealed class ClientWrite
        {
            public ClientWrite(ByteString bytes)
            {
                Bytes = bytes;
            }

            public ByteString Bytes { get; }
        }

        protected sealed class ClientRead
        {
            public ClientRead(int count, IActorRef readTo)
            {
                Count = count;
                ReadTo = readTo;
            }

            public int Count { get; }

            public IActorRef ReadTo { get; }
        }

        protected sealed class ClientClose
        {
            public ClientClose(Tcp.CloseCommand cmd)
            {
                Cmd = cmd;
            }

            public Tcp.CloseCommand Cmd { get; }
        }

        protected sealed class ReadResult
        {
            public ReadResult(ByteString bytes)
            {
                Bytes = bytes;
            }

            public ByteString Bytes { get; }
        }

        // FIXME: Workaround object just to force a ResumeReading that will poll for a possibly pending close event
        // See https://github.com/akka/akka/issues/16552
        // remove this and corresponding code path once above is fixed
        protected sealed class PingClose
        {
            public PingClose(IActorRef requester)
            {
                Requester = requester;
            }

            public IActorRef Requester { get; }
        }

        protected sealed class WriteAck : Tcp.Event
        {
            public static WriteAck Instance { get; } = new WriteAck();

            private WriteAck() { }
        }

        protected static Props TestClientProps(IActorRef connection)
            => Props.Create(() => new TestClient(connection)).WithDispatcher("akka.test.stream-dispatcher");

        protected static Props TestServerProps(EndPoint address, IActorRef probe)
            => Props.Create(() => new TestServer(address, probe)).WithDispatcher("akka.test.stream-dispatcher");


        protected class TestClient : UntypedActor
        {
            private readonly IActorRef _connection;
            private readonly Queue<ByteString> _queuedWrites = new Queue<ByteString>();
            private bool _writePending;
            private int _toRead;
            private ByteString _readBuffer = ByteString.Empty;
            private IActorRef _readTo = Context.System.DeadLetters;
            private Tcp.CloseCommand _closeAfterWrite;
            
            public TestClient(IActorRef connection)
            {
                _connection = connection;
                connection.Tell(new Tcp.Register(Self, keepOpenOnPeerClosed: true, useResumeWriting: false));
            }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case ClientWrite w:
                        if (!_writePending)
                        {
                            _writePending = true;
                            _connection.Tell(Tcp.Write.Create(w.Bytes, WriteAck.Instance));
                        }
                        else
                            _queuedWrites.Enqueue(w.Bytes);
                        break;
                    
                    case WriteAck _:
                        if (_queuedWrites.Count != 0)
                        {
                            var next = _queuedWrites.Dequeue();
                            _connection.Tell(Tcp.Write.Create(next, WriteAck.Instance));
                        }
                        else
                        {
                            _writePending = false;
                            if(_closeAfterWrite != null)
                                _connection.Tell(_closeAfterWrite);
                        }
                        break;
                    
                    case ClientRead r:
                        _readTo = r.ReadTo;
                        _toRead = r.Count;
                        _connection.Tell(Tcp.ResumeReading.Instance);
                        break;
                    
                    case Tcp.Received r:
                        _readBuffer += r.Data;
                        if (_readBuffer.Count >= _toRead)
                        {
                            _readTo.Tell(new ReadResult(_readBuffer));
                            _readBuffer = ByteString.Empty;
                            _toRead = 0;
                            _readTo = Context.System.DeadLetters;
                        }
                        else
                            _connection.Tell(Tcp.ResumeReading.Instance);
                        break;
                    
                    case PingClose p:
                        _readTo = p.Requester;
                        _connection.Tell(Tcp.ResumeReading.Instance);
                        break;
                    
                    case Tcp.ConnectionClosed c:
                        _readTo.Tell(c);
                        if(!c.IsPeerClosed)
                            Context.Stop(Self);
                        break;
                    
                    case ClientClose c:
                        if (!_writePending)
                            _connection.Tell(c.Cmd);
                        else
                            _closeAfterWrite = c.Cmd;
                        break;
                }
            }
        }

        protected sealed class ServerClose
        {
            public static ServerClose Instance { get; } = new ServerClose();

            private ServerClose() { }
        }

        protected class TestServer : UntypedActor
        {
            private readonly IActorRef _probe;
            private IActorRef _listener = Nobody.Instance;

            public TestServer(EndPoint address, IActorRef probe)
            {
                _probe = probe;
                Context.System.Tcp().Tell(new Tcp.Bind(Self, address, pullMode: true));
            }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case Tcp.Bound b:
                        _listener = Sender;
                        _listener.Tell(new Tcp.ResumeAccepting(1));
                        _probe.Tell(b);
                        break;
                    
                    case Tcp.Connected _:
                        var handler = Context.ActorOf(TestClientProps(Sender));
                        _listener.Tell(new Tcp.ResumeAccepting(1));
                        _probe.Tell(handler);
                        break;
                    
                    case ServerClose _:
                        _listener.Tell(Tcp.Unbind.Instance);
                        Context.Stop(Self);
                        break;
                }
            }
        }

        protected class Server
        {
            private readonly TestKitBase _testkit;
            private TestProbe _serverProbe;
            private IActorRef _serverRef;
            private bool _initialized;

            public Server(TestKitBase testkit, EndPoint address = null)
            {
                _testkit = testkit;
                Address = address ?? TestUtils.TemporaryServerAddress();
            }

            public async Task<Server> InitializeAsync()
            {
                _serverProbe = _testkit.CreateTestProbe();
                _serverRef = _testkit.ActorOf(TestServerProps(Address, _serverProbe.Ref));
                await _serverProbe.ExpectMsgAsync<Tcp.Bound>();
                _initialized = true;
                return this;
            }

            public EndPoint Address { get; }

            public TestProbe ServerProbe
            {
                get
                {
                    EnsureInitialized();
                    return _serverProbe;
                }
            }

            public IActorRef ServerRef
            {
                get
                {
                    EnsureInitialized();
                    return _serverRef;
                }
            }

            private void EnsureInitialized()
            {
                if(!_initialized)
                    throw new InvalidOperationException("Server not initialized, please call InitializeAsync");
            }
            
            public async Task<ServerConnection> WaitAcceptAsync()
            {
                var actor = await ServerProbe.ExpectMsgAsync<IActorRef>();
                return new ServerConnection(_testkit, actor);
            }

            public void Close() => ServerRef.Tell(ServerClose.Instance);
        }

        protected class ServerConnection
        {
            private readonly IActorRef _connectionActor;
            private readonly TestProbe _connectionProbe;

            public ServerConnection(TestKitBase testkit, IActorRef connectionActor)
            {
                _connectionActor = connectionActor;
                _connectionProbe = testkit.CreateTestProbe();
            }

            public void Write(ByteString bytes) => _connectionActor.Tell(new ClientWrite(bytes));

            public void Read(int count) => _connectionActor.Tell(new ClientRead(count, _connectionProbe.Ref));

            public async Task<ByteString> WaitReadAsync() => (await _connectionProbe.ExpectMsgAsync<ReadResult>()).Bytes;

            public void ConfirmedClose() => _connectionActor.Tell(new ClientClose(Tcp.ConfirmedClose.Instance));

            public void Close() => _connectionActor.Tell(new ClientClose(Tcp.Close.Instance));

            public void Abort() => _connectionActor.Tell(new ClientClose(Tcp.Abort.Instance));

            public async Task ExpectClosedAsync(
                Tcp.ConnectionClosed expected,
                CancellationToken cancellationToken = default) 
                => await ExpectClosedAsync(close => close == expected, cancellationToken: cancellationToken);

            public async Task ExpectClosedAsync(
                Predicate<Tcp.ConnectionClosed> isMessage,
                TimeSpan? max = null,
                CancellationToken cancellationToken = default)
            {
                max ??= TimeSpan.FromSeconds(3);

                _connectionActor.Tell(new PingClose(_connectionProbe.Ref));
                await _connectionProbe.FishForMessageAsync(
                    c => c is Tcp.ConnectionClosed closed && isMessage(closed), max, cancellationToken: cancellationToken);
            }

            public async Task ExpectTerminatedAsync(CancellationToken cancellationToken = default)
            {
                _connectionProbe.Watch(_connectionActor);
                await _connectionProbe.ExpectTerminatedAsync(_connectionActor, cancellationToken: cancellationToken);
                _connectionProbe.Unwatch(_connectionActor);
            }
        }

        protected class TcpReadProbe
        {
            private ISubscription _tcpReadSubscription;
            
            public TcpReadProbe(TestKitBase testkit)
            {
                SubscriberProbe = testkit.CreateManualSubscriberProbe<ByteString>();
            }

            public async Task<ISubscription> TcpReadSubscription()
            {
                return _tcpReadSubscription ??= await SubscriberProbe.ExpectSubscriptionAsync();
            }

            public TestSubscriber.ManualProbe<ByteString> SubscriberProbe { get; }

            public async Task<ByteString> ReadAsync(int count)
            {
                var result = ByteString.Empty;

                while (result.Count < count)
                {
                    (await TcpReadSubscription()).Request(1);
                    result += await SubscriberProbe.ExpectNextAsync();
                }

                return result;
            }

            public async Task CloseAsync() => (await TcpReadSubscription()).Cancel();
        }

        protected class TcpWriteProbe
        {
            private StreamTestKit.PublisherProbeSubscription<ByteString> _tcpWriteSubscription;
            private long _demand;

            public TcpWriteProbe(TestKitBase testkit)
            {
                PublisherProbe = testkit.CreateManualPublisherProbe<ByteString>();
            }

            public async Task<StreamTestKit.PublisherProbeSubscription<ByteString>> TcpWriteSubscription()
            {
                return _tcpWriteSubscription ??= await PublisherProbe.ExpectSubscriptionAsync();
            }

            public TestPublisher.ManualProbe<ByteString> PublisherProbe { get; }

            public async Task WriteAsync(ByteString bytes)
            {
                var subscription = await TcpWriteSubscription();
                if (_demand == 0)
                    _demand += await subscription.ExpectRequestAsync();

                subscription.SendNext(bytes);
                _demand -= 1;
            }

            public async Task CloseAsync() => (await TcpWriteSubscription()).SendComplete();
        }
    }

}
