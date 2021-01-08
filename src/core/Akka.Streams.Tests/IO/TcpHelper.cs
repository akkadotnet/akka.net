//-----------------------------------------------------------------------
// <copyright file="TcpHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
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
                    .WithFallback(
                        ConfigurationFactory.FromResource<ScriptedTest>("Akka.Streams.TestKit.Tests.reference.conf")),
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
                message.Match().With<ClientWrite>(w =>
                {
                    if (!_writePending)
                    {
                        _writePending = true;
                        _connection.Tell(Tcp.Write.Create(w.Bytes, WriteAck.Instance));
                    }
                    else
                        _queuedWrites.Enqueue(w.Bytes);
                }).With<WriteAck>(() =>
                {
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
                }).With<ClientRead>(r =>
                {
                    _readTo = r.ReadTo;
                    _toRead = r.Count;
                    _connection.Tell(Tcp.ResumeReading.Instance);
                }).With<Tcp.Received>(r =>
                {
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
                }).With<PingClose>(p =>
                {
                    _readTo = p.Requester;
                    _connection.Tell(Tcp.ResumeReading.Instance);
                }).With<Tcp.ConnectionClosed>(c =>
                {
                    _readTo.Tell(c);
                    if(!c.IsPeerClosed)
                        Context.Stop(Self);
                }).With<ClientClose>(c =>
                {
                    if (!_writePending)
                        _connection.Tell(c.Cmd);
                    else
                        _closeAfterWrite = c.Cmd;
                });
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
                message.Match().With<Tcp.Bound>(b =>
                {
                    _listener = Sender;
                    _listener.Tell(new Tcp.ResumeAccepting(1));
                    _probe.Tell(b);
                }).With<Tcp.Connected>(() =>
                {
                    var handler = Context.ActorOf(TestClientProps(Sender));
                    _listener.Tell(new Tcp.ResumeAccepting(1));
                    _probe.Tell(handler);
                }).With<ServerClose>(() =>
                {
                    _listener.Tell(Tcp.Unbind.Instance);
                    Context.Stop(Self);
                });
            }
        }

        protected class Server
        {
            private readonly TestKitBase _testkit;

            public Server(TestKitBase testkit, EndPoint address = null)
            {
                _testkit = testkit;
                Address = address ?? TestUtils.TemporaryServerAddress();

                ServerProbe = testkit.CreateTestProbe();
                ServerRef = testkit.ActorOf(TestServerProps(Address, ServerProbe.Ref));
                ServerProbe.ExpectMsg<Tcp.Bound>();
            }

            public EndPoint Address { get; }

            public TestProbe ServerProbe { get; }

            public IActorRef ServerRef { get; }

            public ServerConnection WaitAccept() => new ServerConnection(_testkit, ServerProbe.ExpectMsg<IActorRef>());

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

            public ByteString WaitRead() => _connectionProbe.ExpectMsg<ReadResult>().Bytes;

            public void ConfirmedClose() => _connectionActor.Tell(new ClientClose(Tcp.ConfirmedClose.Instance));

            public void Close() => _connectionActor.Tell(new ClientClose(Tcp.Close.Instance));

            public void Abort() => _connectionActor.Tell(new ClientClose(Tcp.Abort.Instance));

            public void ExpectClosed(Tcp.ConnectionClosed expected) => ExpectClosed(close => close == expected);

            public void ExpectClosed(Predicate<Tcp.ConnectionClosed> isMessage, TimeSpan? max = null)
            {
                max = max ?? TimeSpan.FromSeconds(3);

                _connectionActor.Tell(new PingClose(_connectionProbe.Ref));
                _connectionProbe.FishForMessage((c) => c is Tcp.ConnectionClosed && isMessage((Tcp.ConnectionClosed)c), max);
            }

            public void ExpectTerminated()
            {
                _connectionProbe.Watch(_connectionActor);
                _connectionProbe.ExpectTerminated(_connectionActor);
            }
        }

        protected class TcpReadProbe
        {
            public TcpReadProbe(TestKitBase testkit)
            {
                SubscriberProbe = testkit.CreateManualSubscriberProbe<ByteString>();
                TcpReadSubscription = new Lazy<ISubscription>(() => SubscriberProbe.ExpectSubscription());
            }

            public Lazy<ISubscription> TcpReadSubscription { get; }

            public TestSubscriber.ManualProbe<ByteString> SubscriberProbe { get; }

            public ByteString Read(int count)
            {
                var result = ByteString.Empty;

                while (result.Count < count)
                {
                    TcpReadSubscription.Value.Request(1);
                    result += SubscriberProbe.ExpectNext();
                }

                return result;
            }

            public void Close() => TcpReadSubscription.Value.Cancel();
        }

        protected class TcpWriteProbe
        {
            private long _demand;

            public TcpWriteProbe(TestKitBase testkit)
            {
                PublisherProbe = testkit.CreateManualPublisherProbe<ByteString>();
                TcpWriteSubscription =
                    new Lazy<StreamTestKit.PublisherProbeSubscription<ByteString>>(
                        () => PublisherProbe.ExpectSubscription());
            }

            public Lazy<StreamTestKit.PublisherProbeSubscription<ByteString>> TcpWriteSubscription { get; }

            public TestPublisher.ManualProbe<ByteString> PublisherProbe { get; }

            public void Write(ByteString bytes)
            {
                if (_demand == 0)
                    _demand += TcpWriteSubscription.Value.ExpectRequest();

                TcpWriteSubscription.Value.SendNext(bytes);
                _demand -= 1;
            }

            public void Close() => TcpWriteSubscription.Value.SendComplete();
        }
    }

}
