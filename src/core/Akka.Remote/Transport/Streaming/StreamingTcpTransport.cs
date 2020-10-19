// //-----------------------------------------------------------------------
// // <copyright file="testTransport.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.IO;
using Akka.Remote.Transport.DotNetty;
using Akka.Streams;
using Akka.Streams.Dsl;
using ByteString = Google.Protobuf.ByteString;
using Dns = System.Net.Dns;
using Tcp = Akka.Streams.Dsl.Tcp;

namespace Akka.Remote.Transport.Streaming
{

    
    class StreamingTcpAssociationHandle : AssociationHandle
    {
        private ISourceQueueWithComplete<IO.ByteString> _queue;
        private IHandleEventListener _listener;
        public StreamingTcpAssociationHandle(Address localAddress,
            Address remoteAddress,
            ISourceQueueWithComplete<IO.ByteString> queue) : base(localAddress, remoteAddress)
        {
            _queue = queue;
        }

        public void RegisterListener(IHandleEventListener listener)
        {
            _listener = listener;
        }
        public StreamingTcpAssociationHandle(Address localAddress,
            Address remoteAddress,
            TaskCompletionSource<ISourceQueueWithComplete<IO.ByteString>> queueTask) : base(localAddress, remoteAddress)
        {
            queueTask.Task.ContinueWith(r => _queue = r.Result);
        }

        public sealed override bool Write(ByteString payload)
        {
            return Write(
                IO.ByteString.FromBytes(
                    ByteStringConverters._getByteArrayUnsafeFunc(payload)
                ));
        }

        public sealed override bool Write(IO.ByteString payload)
        {
            return _queue.OfferAsync(payload)
                .Result is QueueOfferResult
                .Enqueued;
        }
        

        


        public override void Disassociate()
        {
            _queue.Complete();
        }

        public void Notify(IHandleEvent inboundPayload)
        {
            _listener.Notify(inboundPayload);
        }
    }

    class StreamingTcpTransport : Transport
    {
        public sealed override string SchemeIdentifier { get; protected set; } =
            "tcp";

        private StreamingTcpTransportSettings TransportSettings;
        private ActorMaterializer _mat;
        //private DotNettyTransportSettings Settings;

        private Source<Tcp.IncomingConnection, Task<Tcp.ServerBinding>>
            _connectionSource;

        protected readonly TaskCompletionSource<IAssociationEventListener>
            AssociationListenerPromise;

        private Task<Tcp.ServerBinding> _serverBindingTask;
        private Address _addr;
        private EndPoint _listenAddress;
        private EndPoint _outAddr;
        private TaskCompletionSource<Address> _boundAddressSource;

        public StreamingTcpTransport(ActorSystem system, Config config)
        {
            AssociationListenerPromise =
                new TaskCompletionSource<IAssociationEventListener>();
            System = system;
            _mat = ActorMaterializer.Create(System,
                ActorMaterializerSettings.Create(System),
                namePrefix: "streaming-transport");
            if (system.Settings.Config.HasPath("akka.remote.dot-netty.tcp"))
            {
                var dotNettyFallbackConfig =
                    system.Settings.Config.GetConfig(
                        "akka.remote.dot-netty.tcp");
                config = dotNettyFallbackConfig.WithFallback(config);
            }

            if (system.Settings.Config.HasPath("akka.remote.helios.tcp"))
            {
                var heliosFallbackConfig =
                    system.Settings.Config.GetConfig("akka.remote.helios.tcp");
                config = heliosFallbackConfig.WithFallback(config);
            }

            TransportSettings = StreamingTcpTransportSettings.Create(config);

            SocketOptions = ImmutableList.Create<Inet.SocketOption>(
                new Inet.SO.ReceiveBufferSize(
                    TransportSettings.SocketReceiveBufferSize),
                new Inet.SO.SendBufferSize(
                    TransportSettings.SocketSendBufferSize),
                new Inet.SO.ByteBufferPoolSize(TransportSettings.TransportReceiveBufferSize));
        }

        private ImmutableList<Inet.SocketOption> SocketOptions { get; }

        public override long MaximumPayloadBytes
        {
            get { return TransportSettings.MaxFrameSize; }
        }

        public override async
            Task<(Address, TaskCompletionSource<IAssociationEventListener>)>
            Listen()
        {
            if (IPAddress.TryParse(TransportSettings.Hostname,
                out IPAddress ip))
            {
                _listenAddress = new IPEndPoint(ip, TransportSettings.Port);
                _outAddr = new IPEndPoint(ip, 0);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(TransportSettings.Hostname))
                {
                    var hostName = Dns.GetHostName();
                    _listenAddress =
                        new DnsEndPoint(hostName, TransportSettings.Port);
                    _outAddr = new DnsEndPoint(hostName, 0);
                }
                else
                {
                    _listenAddress =
                        new DnsEndPoint(TransportSettings.Hostname,
                            TransportSettings.Port);
                    _outAddr = new DnsEndPoint(TransportSettings.Hostname, 0);
                }
            }


            _connectionSource =
                System.TcpStream().Bind(TransportSettings.Hostname,
                    TransportSettings.Port,
                    options: SocketOptions,
                    backlog: TransportSettings.ConnectionBacklog);
            var mappedAddr = _listenAddress is IPEndPoint p
                ? p
                : await DnsToIPEndpoint(_listenAddress as DnsEndPoint);


            _boundAddressSource = new TaskCompletionSource<Address>();
            //_connectionSource.Via
            _serverBindingTask = _connectionSource.Select
            (ic =>
            {
                AssociationListenerPromise.Task.ContinueWith(r =>
                {
                    var listener = r.Result;
                    var remoteAddress =
                        DotNettyTransport.MapSocketToAddress(
                            (IPEndPoint)ic.RemoteAddress, "tcp",
                            base.System.Name);
                    var queuePromise =
                        new TaskCompletionSource<
                            ISourceQueueWithComplete<IO.ByteString>>();
                    var handleAddr = _boundAddressSource.Task.Result;

                    var handle = CreateStreamingTcpAssociationHandle(handleAddr,
                        remoteAddress, queuePromise);
                    var outQueue = ic.HandleWith(buildFlow(handle)
                        .Recover(
                            ex =>
                            {
                                handle.Notify(
                                    new Disassociated(DisassociateInfo
                                        .Unknown));
                                return IO.ByteString.Empty;
                            }), _mat);
                    queuePromise.SetResult(outQueue);
                    listener.Notify(new InboundAssociation(handle));
                });
                return NotUsed.Instance;
            }).ToMaterialized(Sink.Ignore<NotUsed>(), Keep.Left).Run(_mat);
            await _serverBindingTask.ContinueWith(sb =>
            {
                _addr =
                    DotNettyTransport.MapSocketToAddress(
                        socketAddress: (IPEndPoint)sb.Result.LocalAddress,
                        schemeIdentifier: SchemeIdentifier,
                        systemName: base.System.Name,
                        hostName: TransportSettings.PublicHostname,
                        publicPort: TransportSettings.PublicPort);
                _boundAddressSource.SetResult(_addr);
            });
            return (_addr, AssociationListenerPromise);
        }

        private static StreamingTcpAssociationHandle
            CreateStreamingTcpAssociationHandle(Address handleAddr,
                Address remoteAddress, TaskCompletionSource<ISourceQueueWithComplete<IO.ByteString>> queuePromise)
        {
            StreamingTcpAssociationHandle handle;
            handle = new StreamingTcpAssociationHandle(handleAddr
                ,
                remoteAddress,
                queuePromise);
            handle.ReadHandlerSource.Task.ContinueWith(s =>
            {
                var otherListener = s.Result;
                handle.RegisterListener(otherListener);
            }, TaskContinuationOptions.ExecuteSynchronously);
            return handle;
        }


        protected async Task<IPEndPoint> DnsToIPEndpoint(DnsEndPoint dns)
        {
            IPEndPoint endpoint;

            var addressFamily = TransportSettings.DnsUseIpv6
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;
            endpoint = await DnsHelpers.ResolveNameAsync(dns, addressFamily)
                .ConfigureAwait(false);

            return endpoint;
        }

        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }
 

        public override async Task<AssociationHandle> Associate(
            Address remoteAddress)
        {
            
            var addr = DotNettyTransport.AddressToSocketAddress(remoteAddress);
            var outConnectionSource = base.System.TcpStream().OutgoingConnection(
                remoteAddress: addr,
                localAddress: null,
                options: SocketOptions
            );
            var queuePromise =
                new TaskCompletionSource<
                    ISourceQueueWithComplete<IO.ByteString>>();
            var handle =
                CreateStreamingTcpAssociationHandle(_addr, remoteAddress,
                    queuePromise);
            var run = outConnectionSource.JoinMaterialized(buildFlow(handle).Recover(
                ex =>
                {
                    handle.Notify(
                        new Disassociated(DisassociateInfo.Unknown));
                    return IO.ByteString.Empty;
                }), (connectionTask, sendQueue) => (connectionTask, sendQueue)).Run(_mat);
            queuePromise.SetResult(run.sendQueue);
            return handle;
        }
        

    private Source<IO.ByteString, ISourceQueueWithComplete<IO.ByteString>> sendFlow()
        {
            //TODO: Improve batching voodoo in pipeline.
            var baseSource =  Source
                .Queue<IO.ByteString>(TransportSettings.SendStreamQueueSize, OverflowStrategy.DropNew)
                .Via(
                    Framing.SimpleFramingProtocolEncoder(TransportSettings.MaxFrameSize));
            
            //If batch delay is 0, we don't want our delay stage.
            if (TransportSettings.BatchGroupMaxMillis > 0)
            {
               baseSource = baseSource.GroupedWithin(TransportSettings.BatchGroupMaxCount, TimeSpan.FromMilliseconds(TransportSettings.BatchGroupMaxMillis))
                    .SelectMany(msg => msg)
                    .Async();
            }
            else
            {
                baseSource = baseSource.Async();
            }

            return baseSource.BatchWeighted(
                    TransportSettings.BatchGroupMaxBytes,
                    msg => msg.Count,
                    msg => msg,
                    (oldMsgs, newMsg) => oldMsgs.Concat(newMsg))
                .AddAttributes(Attributes.CreateInputBuffer(
                    TransportSettings.BatchPumpInputMinBufferSize,
                    TransportSettings.BatchPumpInputMaxBufferSize))
                .Via(bufferedSelectFlow());
        }
        private Flow<IO.ByteString, IO.ByteString, NotUsed>
            bufferedSelectFlow()
        {

            return Flow.Create<IO.ByteString>()
                .Select(b => b)
                //Put an async boundary here so that we do not fuse
                //And can properly batch to Socket.
                .Async()
                .AddAttributes(Attributes.CreateInputBuffer(
                    TransportSettings.SocketStageInputMinBufferSize,
                    TransportSettings.SocketStageInputMinBufferSize));
        }

        private Flow<IO.ByteString, IO.ByteString,
            ISourceQueueWithComplete<IO.ByteString>> buildFlow(
            StreamingTcpAssociationHandle handle)
        {
            return Flow.FromSinkAndSource(receiveFlow(handle).Async(),
                sendFlow().Async(),(_,sendQueue)=>sendQueue);
        }
        private ILoggingAdapter logger => System.Log;
        
        
        private Sink<IO.ByteString, NotUsed> receiveFlow(
            StreamingTcpAssociationHandle handle)
        {
            return Flow.Create<IO.ByteString>()
                .Via(Framing.SimpleFramingProtocolDecoder(TransportSettings.MaxFrameSize))
                .Select(r =>
                {
                    //By using ReadOnlyCompacted() we might get lucky
                    //and save on a copy here.
                    var bs = r.ReadOnlyCompacted();
                    handle.Notify(
                            new InboundPayload(bs));
                        return  NotUsed.Instance;
                } ).ToMaterialized(Sink.Ignore<NotUsed>() ,Keep.None);
        }

        public override async Task<bool> Shutdown()
        {
            await _serverBindingTask.Result.Unbind().ConfigureAwait(false);
            return true;
        }
    }
    
}