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
using System.Net;
using System.Net.Sockets;
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

        public override bool Write(ByteString payload)
        {

            try
            {
                return _queue?.OfferAsync(IO.ByteString.FromBytes(payload.ToByteArray())).Result is QueueOfferResult
                    .Enqueued;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        

        public override void Disassociate()
        {
            _queue.Complete();
        }

        public void Notify(IHandleEvent inboundPayload)
        {
            _listener?.Notify(inboundPayload);
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
        private Source<Tcp.IncomingConnection, NotUsed> _inboundConnectionSource;
        private Address _addr;
        private EndPoint _listenAddress;
        private EndPoint _outAddr;

        public StreamingTcpTransport(ActorSystem system, Config config)
        {
            AssociationListenerPromise = new TaskCompletionSource<IAssociationEventListener>();
            System = system;
            _mat = ActorMaterializer.Create(system,ActorMaterializerSettings.Create(System), namePrefix:"streaming-transport");
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

        }

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
                    _outAddr = new DnsEndPoint(Settings.Hostname,0);
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
                    options: ImmutableList.Create<Inet.SocketOption>(
                        new Inet.SO.SendBufferSize(Settings.SendBufferSize
                                                   ?? 256 * 1024),
                        new Inet.SO.ReceiveBufferSize(
                            Settings.ReceiveBufferSize ?? 256 * 1024)), backlog:4096);
            var mappedAddr = _listenAddress is IPEndPoint p
                ? p
                : await DnsToIPEndpoint(_listenAddress as DnsEndPoint);
                            
            _addr = DnsHelpers.MapSocketToAddress(
                socketAddress: mappedAddr, 
                schemeIdentifier: SchemeIdentifier,
                systemName: base.System.Name,
                hostName: Settings.PublicHostname,
                publicPort: Settings.PublicPort);
            //_connectionSource.Via
            _serverBindingTask = _connectionSource.Select
            (ic =>
            {
                
                AssociationListenerPromise.Task.ContinueWith(r =>
                {
                    var listener = r.Result;
                    var remoteAddress =
                        DnsHelpers.MapSocketToAddress(
                            (IPEndPoint)ic.RemoteAddress, "tcp", base.System.Name);
                    var queuePromise =
                        new TaskCompletionSource<
                            ISourceQueueWithComplete<IO.ByteString>>();
                    StreamingTcpAssociationHandle handle;
                    handle = new StreamingTcpAssociationHandle(_addr,
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
            
            var c =base.System.TcpStream().OutgoingConnection(
                DnsHelpers.AddressToSocketAddress(remoteAddress),
                _outAddr,
                ImmutableList.Create<Inet.SocketOption>(
                    new Inet.SO.ReceiveBufferSize(
                        Settings.ReceiveBufferSize ?? 256 * 1024),
                    new Inet.SO.SendBufferSize(
                        Settings.SendBufferSize ?? 256 * 1024)));
            
            
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
                .Queue<IO.ByteString>(512, OverflowStrategy.DropNew)
                .Via(
                    Framing.SimpleFramingProtocolEncoder(Settings.MaxFrameSize))
                .GroupedWithin(256, TimeSpan.FromMilliseconds(40))
                .SelectMany(r => r)
                .Async()
                .BatchWeighted(32*1000,
                    bs => bs.Count, bs => new List<IO.ByteString>(256) { bs },
                    (bsl, bs) =>
                    {
                        bsl.Add(bs);
                        return bsl;
                    })
                .AddAttributes(Attributes.CreateInputBuffer(256,256))
                .Via(createBufferedSelectFlow());
        }

        private Flow<List<IO.ByteString>, IO.ByteString, NotUsed>
            createBufferedSelectFlow()
        {

            return Flow.Create<List<IO.ByteString>>()
                .Select(bsl =>
                {
                    //logger.Error(
                    //    $"Wrote {bsl.Count} - {bsl.Sum(r => r.Count)} bytes");
                    return new IO.ByteString(bsl);
                })
                //Put an async boundary here so that we do not fuse
                //And can properly batch to Socket.
                .Async()
                .AddAttributes(Attributes.CreateInputBuffer(1, 1));
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
                .Via(Framing.SimpleFramingProtocolDecoder(Settings.MaxFrameSize)).Select(r =>
                {
                    var bs = ByteString.CopyFrom(r.ToArray());
                    handle.Notify(
                        new InboundPayload(bs));
                    return  NotUsed.Instance;
                } ).ToMaterialized(Sink.Ignore<NotUsed>() ,Keep.Left);
        }

        public override async Task<bool> Shutdown()
        {
            await _serverBindingTask.Result.Unbind().ConfigureAwait(false);
            return true;
        }
    }
}