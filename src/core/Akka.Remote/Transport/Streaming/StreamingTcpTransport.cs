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
            return _queue
                    .OfferAsync(
                        IO.ByteString.FromBytes(
                            _getByteArrayUnsafeFunc(payload)
                            //payload.ToByteArray()
                            ))
                    .Result is QueueOfferResult
                    .Enqueued;
        }
        

        private static readonly Func<Google.Protobuf.ByteString, byte[]> _getByteArrayUnsafeFunc = build();
        

        private static Func<Google.Protobuf.ByteString, byte[]> build()
        {
            var p = Expression.Parameter(typeof(ByteString));
            return Expression.Lambda<Func<ByteString, byte[]>>(Expression.Field(p,
                    typeof(ByteString).GetField("bytes",
                        BindingFlags.NonPublic | BindingFlags.Instance)), p)
                .Compile();
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
        private ActorMaterializer _mat;
        private DotNettyTransportSettings Settings;
        private Source<Tcp.IncomingConnection, Task<Tcp.ServerBinding>> _connectionSource;
        protected readonly TaskCompletionSource<IAssociationEventListener> AssociationListenerPromise;
        private Task<Tcp.ServerBinding> _serverBindingTask;
        private Address _addr;
        private EndPoint _listenAddress;
        private EndPoint _outAddr;
        private TaskCompletionSource<Address> _boundAddressSource;

        public StreamingTcpTransport(ActorSystem system, Config config)
        {
            AssociationListenerPromise = new TaskCompletionSource<IAssociationEventListener>();
            System = system;
            _mat = ActorMaterializer.Create(System,ActorMaterializerSettings.Create(System), namePrefix:"streaming-transport");
            if (system.Settings.Config.HasPath("akka.remote.dot-netty.tcp"))
            {
                var dotNettyFallbackConfig =
                    system.Settings.Config.GetConfig(
                        "akka.remote.dot-netty.tcp");
                config = dotNettyFallbackConfig.WithFallback(config);
            }
            if (system.Settings.Config.HasPath("akka.remote.helios.tcp"))
            {
                var heliosFallbackConfig = system.Settings.Config.GetConfig("akka.remote.helios.tcp");
                config = heliosFallbackConfig.WithFallback(config);
            }
            
            Settings = DotNettyTransportSettings.Create(config);

            SocketOptions = ImmutableList.Create<Inet.SocketOption>(
                new Inet.SO.ReceiveBufferSize(
                    Settings.ReceiveBufferSize ?? 128 * 1024),
                new Inet.SO.SendBufferSize(
                    Settings.SendBufferSize ?? 64 * 1024),
                new Inet.SO.ByteBufferPoolSize(65536));
        }

        private ImmutableList<Inet.SocketOption> SocketOptions { get; }

        public override long MaximumPayloadBytes
        {
            get { return Settings.MaxFrameSize; }
        }

        public override async Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen()
        {
            if (IPAddress.TryParse(Settings.Hostname, out IPAddress ip))
            {
                _listenAddress = new IPEndPoint(ip, Settings.Port);
                _outAddr = new IPEndPoint(ip,0);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Settings.Hostname))
                {
                    var hostName = Dns.GetHostName();
                    _listenAddress = new DnsEndPoint(hostName, Settings.Port);
                    _outAddr = new DnsEndPoint(hostName,0);
                }
                else
                {
                    _listenAddress =
                        new DnsEndPoint(Settings.Hostname, Settings.Port);
                    _outAddr = new DnsEndPoint(Settings.Hostname,0);
                }
            }
            
            
            _connectionSource =
                System.TcpStream().Bind(Settings.Hostname, Settings.Port,
                    options: SocketOptions, backlog:4096);
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
                            (IPEndPoint)ic.RemoteAddress, "tcp", base.System.Name);
                    var queuePromise =
                        new TaskCompletionSource<
                            ISourceQueueWithComplete<IO.ByteString>>();
                    StreamingTcpAssociationHandle handle;
                    handle = new StreamingTcpAssociationHandle(_boundAddressSource.Task.Result,
                        remoteAddress,
                        queuePromise);
                    handle.ReadHandlerSource.Task.ContinueWith(s =>
                    {   
                        var otherListener = s.Result;
                        handle.RegisterListener(otherListener);
                    }, TaskContinuationOptions.ExecuteSynchronously);
                    var outQueue = ic.HandleWith(buildFlow(handle)
                        .Recover(
                            ex =>
                            {
                                handle.Notify(
                                    new Disassociated(DisassociateInfo.Unknown));
                                return IO.ByteString.Empty;
                            }), _mat);
                    queuePromise.SetResult(outQueue);
                    listener.Notify(new InboundAssociation(handle));
                });
                return NotUsed.Instance;
            }).ToMaterialized(Sink.Ignore<NotUsed>(),Keep.Left).Run(_mat);
            await _serverBindingTask.ContinueWith(sb=>
            {
                _addr =
                    DotNettyTransport.MapSocketToAddress(
                        socketAddress: (IPEndPoint)sb.Result.LocalAddress,
                        schemeIdentifier: SchemeIdentifier,
                        systemName: base.System.Name,
                        hostName: Settings.PublicHostname,
                        publicPort: Settings.PublicPort);
                _boundAddressSource.SetResult(_addr);
            });
            return (_addr, AssociationListenerPromise);
        }

        
        protected async Task<IPEndPoint> DnsToIPEndpoint(DnsEndPoint dns)
        {
            IPEndPoint endpoint;

            var addressFamily = Settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            endpoint = await DnsHelpers.ResolveNameAsync(dns, addressFamily).ConfigureAwait(false);

            return endpoint;
        }
        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }
        public override async Task<AssociationHandle> Associate(Address remoteAddress)
        {
            
            var addr = DotNettyTransport.AddressToSocketAddress(remoteAddress);
            var c =base.System.TcpStream().OutgoingConnection(addr
                ,
                null,SocketOptions
            );
            
            
            var queuePromise =
                    new TaskCompletionSource<
                        ISourceQueueWithComplete<IO.ByteString>>();
            var handle = new StreamingTcpAssociationHandle(_addr,remoteAddress, queuePromise);
            handle.ReadHandlerSource.Task.ContinueWith(s =>
            {   
                var listener = s.Result;
                handle.RegisterListener(listener);
            }, TaskContinuationOptions.ExecuteSynchronously);
            var run = c.JoinMaterialized(buildFlow(handle).Recover(
                    ex =>
                    {
                        handle.Notify(
                            new Disassociated(DisassociateInfo.Unknown));
                        return IO.ByteString.Empty;
                    }),(oc,sq)=>(oc,sq)).Run(_mat);
            queuePromise.SetResult(run.sq);
            return handle;
        }

        
        private Source<IO.ByteString, ISourceQueueWithComplete<IO.ByteString>> sendFlow()
        {
            //TODO: Improve batching voodoo in pipeline.
            return Source
                .Queue<IO.ByteString>(64, OverflowStrategy.DropNew).Async()
                .Via(
                    Framing.SimpleFramingProtocolEncoder(Settings.MaxFrameSize))
                .GroupedWithin(128, TimeSpan.FromMilliseconds(20))
                .SelectMany(r => r)
                .Async()
                .BatchWeighted(32*1024,
                    bs => bs.Count, 
                    /*bs => new List<IO.ByteString>(256) { bs },
                    (bsl, bs) =>
                    {
                        bsl.Add(bs);
                        return bsl;
                    })*/
                bs=> bs,
                    (bs,nb)=>bs.Concat(nb))
                .AddAttributes(Attributes.CreateInputBuffer(128,128))
                .Via(createBufferedSelectFlow2())
                //.Via(Compression.Deflate())
                //.Via(Framing.SimpleFramingProtocolEncoder(Settings.MaxFrameSize))
                ;
        }
        private Flow<IO.ByteString, IO.ByteString, NotUsed>
            createBufferedSelectFlow2()
        {

            return Flow.Create<IO.ByteString>()
                .Select(b=>
                {
                    //logger.Error($"wrote {b.Count}");
                    return b;
                })
                //Put an async boundary here so that we do not fuse
                //And can properly batch to Socket.
                .Async()
                .AddAttributes(Attributes.CreateInputBuffer(1, 1))
                ;
        }

        private Flow<IO.ByteString, IO.ByteString,
            ISourceQueueWithComplete<IO.ByteString>> buildFlow(
            StreamingTcpAssociationHandle handle)
        {
            return Flow.FromSinkAndSource(receiveFlow(handle).Async(),
                sendFlow().Async(),(c,q)=>q);
        }
        private ILoggingAdapter logger => System.Log;
        
        
        private Sink<IO.ByteString, NotUsed> receiveFlow(
            StreamingTcpAssociationHandle handle)
        {
            return Flow.Create<IO.ByteString>()
                //.Via(Framing.SimpleFramingProtocolDecoder(Settings.MaxFrameSize))
                //.Via(Compression.Inflate())
                .Via(Framing.SimpleFramingProtocolDecoder(Settings.MaxFrameSize)).Select(r =>
                {
                        var bs = r.ReadOnlyCompacted();
                        var c = ByteString.CopyFrom(bs.Array, bs.Offset,
                            bs.Count);
                        handle.Notify(
                            new InboundPayload(c));
                    
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