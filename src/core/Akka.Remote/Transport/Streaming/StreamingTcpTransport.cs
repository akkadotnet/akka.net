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

        public StreamingTcpAssociationHandle(Address localAddress,
            Address remoteAddress,
            ISourceQueueWithComplete<IO.ByteString> queue) : base(localAddress, remoteAddress)
        {
            _queue = queue;
        }

        public override bool Write(ByteString payload)
        {
            
            return _queue.OfferAsync(IO.ByteString.FromImmutable(payload.ToByteArray())).Result is QueueOfferResult
                .Enqueued;
        }

        public override void Disassociate()
        {
            _queue.Complete();
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
        private ConcurrentDictionary<Address,int> _clientAddr = new ConcurrentDictionary<Address, int>();
        private EndPoint _listenAddress;
        private IHandleEventListener _listner;
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
                    : await DnsToIPEndpoint(_listenAddress as DnsEndPoint)
                ;            
            //var sourceSet = _connectionSource.PreMaterialize(_mat);
            //_serverBindingTask = sourceSet.Item1;
            //_inboundConnectionSource = sourceSet.Item2;
            //_inboundConnectionSource.Select()
            _addr = DotNettyTransport.MapSocketToAddress(
                socketAddress: mappedAddr, 
                schemeIdentifier: SchemeIdentifier,
                systemName: base.System.Name,
                hostName: Settings.PublicHostname,
                publicPort: Settings.PublicPort);
            //_connectionSource.Via
            _serverBindingTask = _connectionSource.Select
            (ic =>
            {
                var receiveFlow = this.receiveFlow();
                var sendFlowPreMat = preMaterializedSendFlow();
                AssociationListenerPromise.Task.ContinueWith(r =>
                {
                    var listener = r.Result;
                    var remoteAddress =
                        DotNettyTransport.MapSocketToAddress(
                            (IPEndPoint)ic.RemoteAddress, "tcp", base.System.Name);
                    AssociationHandle handle;
                    handle = new StreamingTcpAssociationHandle(_addr,
                        remoteAddress,
                        sendFlowPreMat.Item1);
                    handle.ReadHandlerSource.Task.ContinueWith(s =>
                    {   
                        var otherListener = s.Result;
                        RegisterListener(otherListener, remoteAddress, null);
                    }, TaskContinuationOptions.ExecuteSynchronously);
                    ic.HandleWith(
                        Flow.FromSinkAndSource(receiveFlow,
                            sendFlowPreMat.Item2).Async(), _mat);
                    listener.Notify(new InboundAssociation(handle));
                });
                return NotUsed.Instance;
            }).ToMaterialized(Sink.Ignore<NotUsed>(),Keep.Left).Run(_mat);
            return (_addr, AssociationListenerPromise);
        }
        
        protected async Task<IPEndPoint> DnsToIPEndpoint(DnsEndPoint dns)
        {
            IPEndPoint endpoint;
            //if (!Settings.EnforceIpFamily)
            //{
            //    endpoint = await ResolveNameAsync(dns).ConfigureAwait(false);
            //}
            //else
            //{
            var addressFamily = Settings.DnsUseIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            endpoint = await DotNettyTransport.ResolveNameAsync(dns, addressFamily).ConfigureAwait(false);
            //}
            return endpoint;
        }
        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }

        public override async Task<AssociationHandle> Associate(Address remoteAddress)
        {
            
            var c =base.System.TcpStream().OutgoingConnection(
                DotNettyTransport.AddressToSocketAddress(remoteAddress),
                _outAddr,
                ImmutableList.Create<Inet.SocketOption>(
                    new Inet.SO.ReceiveBufferSize(
                        Settings.ReceiveBufferSize ?? 256 * 1024),
                    new Inet.SO.SendBufferSize(
                        Settings.SendBufferSize ?? 256 * 1024)));
            
            var receiveFlow = this.receiveFlow();

            var sendFlowPreMat = preMaterializedSendFlow();
            //TaskCompletionSource<ISourceQueueWithComplete<>
            var handle = new StreamingTcpAssociationHandle(_addr,remoteAddress, sendFlowPreMat.Item1);
            handle.ReadHandlerSource.Task.ContinueWith(s =>
            {   
                var listener = s.Result;
                RegisterListener(listener, remoteAddress, null);
            }, TaskContinuationOptions.ExecuteSynchronously);
            c.Join(Flow.FromSinkAndSource(receiveFlow, sendFlowPreMat.Item2).Async().Recover(
                ex =>
                {
                    _listner.Notify(new Disassociated(DisassociateInfo.Unknown));
                    return IO.ByteString.Empty;
                }) ).Run(_mat);
            return handle;
        }

        private (ISourceQueueWithComplete<IO.ByteString>, Source<IO.ByteString, NotUsed>) preMaterializedSendFlow()
        {
            return sendFlow()
                .PreMaterialize(_mat);
        }

        private Source<IO.ByteString, ISourceQueueWithComplete<IO.ByteString>> sendFlow()
        {
            return Source
                .Queue<IO.ByteString>(5000, OverflowStrategy.DropNew).Async()
                .Via(
                    Framing.SimpleFramingProtocolEncoder(Settings.MaxFrameSize))
                .BatchWeighted(16 * 1024,
                    bs => bs.Count, bs => new List<IO.ByteString>() { bs },
                    (bsl, bs) =>
                    {
                        bsl.Add(bs);
                        return bsl;
                    }).Select( bsl => new IO.ByteString(bsl));
        }

        private Sink<IO.ByteString, NotUsed> receiveFlow()
        {
            return Flow.Create<IO.ByteString>()
                .Via(Framing.SimpleFramingProtocolDecoder(Settings.MaxFrameSize)).Select(r =>
                {
                    var bs = ByteString.CopyFrom(r.ToArray());
                    _listner?.Notify(
                        new InboundPayload(bs));
                    return  NotUsed.Instance;
                } ).ToMaterialized(Sink.Ignore<NotUsed>() ,Keep.Left);
        }

        private void RegisterListener(IHandleEventListener listener, Address remoteAddress, object o)
        {
            this._listner = listener;
        }

        public override async Task<bool> Shutdown()
        {
            await _serverBindingTask.Result.Unbind().ConfigureAwait(false);
            return true;
        }
    }
}