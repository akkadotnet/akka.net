//-----------------------------------------------------------------------
// <copyright file="Conductor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote.Transport;
using Akka.Util;
using DotNetty.Transport.Channels;
using Akka.Configuration;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// The conductor is the one orchestrating the test: it governs the
    /// <see cref="Akka.Remote.TestKit.Controller"/>'s ports to which all
    /// Players connect, it issues commands to their
    /// <see cref="FailureInjectorTransportAdapter"/> and provides support
    /// for barriers using the <see cref="Akka.Remote.TestKit.BarrierCoordinator"/>.
    /// All of this is bundled inside the <see cref="TestConductor"/>
    /// </summary>
    partial class TestConductor //Conductor trait in JVM version
    {
        IActorRef _controller;

        public IActorRef Controller
        {
            get
            {
                if(_controller == null) throw new IllegalStateException("TestConductorServer was not started");
                return _controller;
            }
        }

        /// <summary>
        /// Start the <see cref="Controller"/>, which in turn will
        /// bind to a TCP port as specified in the `akka.testconductor.port` config
        /// property, where 0 denotes automatic allocation. Since the latter is
        /// actually preferred, a `Future[Int]` is returned which will be completed
        /// with the port number actually chosen, so that this can then be communicated
        /// to the players for their proper start-up.
        ///
        /// This method also invokes Player.startClient,
        /// since it is expected that the conductor participates in barriers for
        /// overall coordination. The returned Future will only be completed once the
        /// client’s start-up finishes, which in fact waits for all other players to
        /// connect.
        /// </summary>
        /// <param name="participants">participants gives the number of participants which shall connect
        ///  before any of their startClient() operations complete
        /// </param>
        /// <param name="name"></param>
        /// <param name="controllerPort"></param>
        /// <returns></returns>
        public async Task<IPEndPoint> StartController(int participants, RoleName name, IPEndPoint controllerPort)
        {
            if(_controller != null) throw new IllegalStateException("TestConductorServer was already started");
            _controller = _system.ActorOf(Props.Create(() => new Controller(participants, controllerPort)),
               "controller");

            var node = await _controller.Ask<IPEndPoint>(TestKit.Controller.GetSockAddr.Instance, Settings.QueryTimeout).ConfigureAwait(false);
            await StartClient(name, node).ConfigureAwait(false);
            return node;
        }

        /// <summary>
        /// Make the remoting pipeline on the node throttle data sent to or received
        /// from the given remote peer. Throttling works by delaying packet submission
        /// within the netty pipeline until the packet would have been completely sent
        /// according to the given rate, the previous packet completion and the current
        /// packet length. In case of large packets they are split up if the calculated
        /// end pause would exceed `akka.testconductor.packet-split-threshold`
        /// (roughly). All of this uses the system’s scheduler, which is not
        /// terribly precise and will execute tasks later than they are schedule (even
        /// on average), but that is countered by using the actual execution time for
        /// determining how much to send, leading to the correct output rate, but with
        /// increased latency.
        /// 
        /// ====Note====
        /// To use this feature you must activate the failure injector and throttler
        /// transport adapters by specifying `testTransport(on = true)` in your MultiNodeConfig.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be throttled</param>
        /// <param name="direction">can be either `Direction.Send`, `Direction.Receive` or `Direction.Both`</param>
        /// <param name="rateMBit">is the maximum data rate in MBit</param>
        /// <returns></returns>
        public Task<Done> Throttle(RoleName node, RoleName target, ThrottleTransportAdapter.Direction direction,
            float rateMBit)
        {
            RequireTestConductorTransport();
            return Controller.Ask<Done>(new Throttle(node, target, direction, rateMBit), Settings.QueryTimeout);
        }

        /// <summary>
        /// Switch the helios pipeline of the remote support into blackhole mode for
        /// sending and/or receiving: it will just drop all messages right before
        /// submitting them to the Socket or right after receiving them from the
        /// Socket.
        /// 
        ///  ====Note====
        /// To use this feature you must activate the failure injector and throttler
        /// transport adapters by specifying `testTransport(on = true)` in your MultiNodeConfig.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be impeded</param>
        /// <param name="direction">can be either `Direction.Send`, `Direction.Receive` or `Direction.Both`</param>
        /// <returns></returns>
        public Task<Done> Blackhole(RoleName node, RoleName target, ThrottleTransportAdapter.Direction direction)
        {
            return Throttle(node, target, direction, 0f);
        }

        private void RequireTestConductorTransport()
        {
            // Verifies that the Throttle and  FailureInjector TransportAdapters are active
            if(!Transport.DefaultAddress.Protocol.Contains(".trttl.gremlin."))
                throw new ConfigurationException("To use this feature you must activate the failure injector adapters " +
                    "(trttl, gremlin) by specifying `TestTransport(on = true)` in your MultiNodeConfig.");
        }

        /// <summary>
        /// Switch the Helios pipeline of the remote support into pass through mode for
        /// sending and/or receiving.
        /// 
        /// ====Note====
        /// To use this feature you must activate the failure injector and throttler
        /// transport adapters by specifying `testTransport(on = true)` in your MultiNodeConfig.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be impeded</param>
        /// <param name="direction">can be either `Direction.Send`, `Direction.Receive` or `Direction.Both`</param>
        /// <returns></returns>
        public Task<Done> PassThrough(RoleName node, RoleName target, ThrottleTransportAdapter.Direction direction)
        {
            return Throttle(node, target, direction, -1f);
        }

        /// <summary>
        /// Tell the remote support to TCP_RESET the connection to the given remote
        /// peer. It works regardless of whether the recipient was initiator or
        /// responder.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be impeded</param>
        /// <returns></returns>
        public Task<Done> Disconnect(RoleName node, RoleName target)
        {
            return Controller.Ask<Done>(new Disconnect(node, target, false), Settings.QueryTimeout);
        }

        /// <summary>
        /// Tell the remote support to TCP_RESET the connection to the given remote
        /// peer. It works regardless of whether the recipient was initiator or
        /// responder.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="target">is the symbolic name of the other node to which connectivity shall be impeded</param>
        /// <returns></returns>
        public Task<Done> Abort(RoleName node, RoleName target)
        {
            return Controller.Ask<Done>(new Disconnect(node, target, true), Settings.QueryTimeout);
        }

        /// <summary>
        /// Tell the actor system at the remote node to shut itself down. The node will also be
        /// removed, so that the remaining nodes may still pass subsequent barriers.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="exitValue">is the return code which shall be given to System.exit</param>
        /// <exception cref="InvalidOperationException">TBD</exception>
        /// <returns>TBD</returns>
        public Task<Done> Exit(RoleName node, int exitValue)
        {
            // the recover is needed to handle ClientDisconnectedException exception,
            // which is normal during shutdown
            return Controller.Ask(new Terminate(node, new Right<bool, int>(exitValue)), Settings.QueryTimeout).ContinueWith(t =>
            {
                if(t.Result is Done) return Done.Instance;
                var failure = t.Result as FSMBase.Failure;
                if (failure != null && failure.Cause is Controller.ClientDisconnectedException) return Done.Instance;

                throw new InvalidOperationException($"Expected Done but received {t.Result}");
            });
        }

        /// <summary>
        /// Tell the actor system at the remote node to shut itself down without
        /// awaiting termination of remote-deployed children. The node will also be
        /// removed, so that the remaining nodes may still pass subsequent barriers.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="abort">TBD</param>
        /// <exception cref="InvalidOperationException">TBD</exception>
        /// <returns>TBD</returns>
        public Task<Done> Shutdown(RoleName node, bool abort = false)
        {
            // the recover is needed to handle ClientDisconnectedException exception,
            // which is normal during shutdown
            return Controller.Ask(new Terminate(node, new Left<bool, int>(abort)), Settings.QueryTimeout).ContinueWith(t =>
            {
                if (t.Result is Done) return Done.Instance;
                var failure = t.Result as FSMBase.Failure;
                if (failure != null && failure.Cause is Controller.ClientDisconnectedException) return Done.Instance;

                throw new InvalidOperationException($"Expected Done but received {t.Result}");
            });
        }

        /// <summary>
        /// Obtain the list of remote host names currently registered.
        /// </summary>
        public Task<IEnumerable<RoleName>> GetNodes()
        {
            return Controller.Ask<IEnumerable<RoleName>>(TestKit.Controller.GetNodes.Instance, Settings.QueryTimeout);
        }

        /// <summary>
        /// Remove a remote host from the list, so that the remaining nodes may still
        /// pass subsequent barriers. This must be done before the client connection
        /// breaks down in order to affect an "orderly" removal (i.e. without failing
        /// present and future barriers).
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be removed</param>
        /// <returns></returns>
        public Task<Done> RemoveNode(RoleName node)
        {
            return Controller.Ask<Done>(new Remove(node), Settings.QueryTimeout);
        }
    }

    internal class ConductorHandler : ChannelHandlerAdapter
    {
        private readonly ILoggingAdapter _log;
        private readonly IActorRef _controller;
        private readonly ConcurrentDictionary<IChannel, IActorRef> _clients = new ConcurrentDictionary<IChannel, IActorRef>();

        /// <summary>
        /// A single <see cref="ConductorHandler"/> gets shared across all of the connections between 
        /// server and clients.
        /// </summary>
        public override bool IsSharable => true;

        public ConductorHandler(IActorRef controller, ILoggingAdapter log)
        {
            _controller = controller;
            _log = log;
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            _log.Debug("connection from {0}", context.Channel.RemoteAddress);

            // Duration of this Ask operation needs to be infinite
            var channel = context.Channel;
            channel.Configuration.AutoRead = false;
            _controller.Ask<IActorRef>(new Controller.CreateServerFSM(channel),
                TimeSpan.FromMilliseconds(Int32.MaxValue)).ContinueWith(tr =>
                {
                    var fsm = tr.Result;
                    _log.Debug("created server FSM {0}", fsm);
                    _clients.AddOrUpdate(channel, fsm, (connection, @ref) => fsm);
                    channel.Configuration.AutoRead = true;
                });
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            var channel = context.Channel;
            _log.Debug("disconnect from {0}", channel.RemoteAddress);
            if (_clients.TryGetValue(channel, out var fsm))
            {
                fsm.Tell(new Controller.ClientDisconnected(new RoleName(null)));
                IActorRef removedActor;
                _clients.TryRemove(channel, out removedActor);
            }
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var channel = context.Channel;
            _log.Debug("message from {0}: {1}", channel.RemoteAddress, message);
            if (message is INetworkOp)
            {
                if (_clients.TryGetValue(channel, out var fsm))
                    fsm.Tell(message);
                else
                    _log.Warning("Failed to get client for {0}", channel);
            }
            else
            {
                _log.Debug("client {0} sent garbage `{1}`, disconnecting", channel.RemoteAddress, message);
                channel.CloseAsync();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            var channel = context.Channel;
            _log.Warning("handled network error from {0}: {1} {2}", channel.RemoteAddress, exception.Message, exception.StackTrace);
        }

        public override Task CloseAsync(IChannelHandlerContext context)
        {
            _log.Info("Server: disconnecting {0} from {1}", context.Channel.LocalAddress, context.Channel.RemoteAddress);
            return base.CloseAsync(context);
        }
    }

    /// <summary>
    /// The server part of each client connection is represented by a ServerFSM.
    /// The Initial state handles reception of the new client’s
    /// <see cref="Hello"/> message (which is needed for all subsequent
    /// node name translations).
    /// 
    /// In the Ready state, messages from the client are forwarded to the controller
    /// and <see cref="EndpointManager.Send"/> requests are sent, but the latter is
    /// treated specially: all client operations are to be confirmed by a
    /// <see cref="Done"/> message, and there can be only one such
    /// request outstanding at a given time (i.e. a Send fails if the previous has
    /// not yet been acknowledged).
    /// 
    /// INTERNAL API.
    /// </summary>
    internal class ServerFSM : FSM<ServerFSM.State, IActorRef>, ILoggingFSM
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        readonly IChannel _channel;
        readonly IActorRef _controller;        
        RoleName _roleName;

        public enum State
        {
            Initial,
            Ready
        }

        public ServerFSM(IActorRef controller, IChannel channel)
        {
            _controller = controller;
            _channel = channel;

            InitFSM();
        }

        protected void InitFSM()
        {
            StartWith(State.Initial, null);

            WhenUnhandled(@event =>
            {
                var clientDisconnected = @event.FsmEvent as Controller.ClientDisconnected;
                if (clientDisconnected != null)
                {
                    if(@event.StateData != null)
                        @event.StateData.Tell(new Failure(new Controller.ClientDisconnectedException("client disconnected in state " + StateName + ": " + _channel)));                   
                    return Stop();
                }
                return null;
            });

            OnTermination(@event =>
            {
                _controller.Tell(new Controller.ClientDisconnected(_roleName));
                _channel.CloseAsync();
            });

            When(State.Initial, @event =>
            {
                var hello = @event.FsmEvent as Hello;
                if (hello != null)
                {
                    _roleName = new RoleName(hello.Name);
                    _controller.Tell(new Controller.NodeInfo(_roleName, hello.Address, Self));
                    return GoTo(State.Ready);
                }
                if (@event.FsmEvent is INetworkOp)
                {
                    _log.Warning("client {0}, sent not Hello in first message (instead {1}), disconnecting", _channel.RemoteAddress, @event.FsmEvent);
                    _channel.CloseAsync();
                    return Stop();
                }
                if (@event.FsmEvent is IToClient)
                {
                    _log.Warning("cannot send {0} in state Initial", @event.FsmEvent);
                    return Stay();
                }
                if (@event.FsmEvent is StateTimeout)
                {
                    _log.Info("closing channel to {0} because of Hello timeout", _channel.RemoteAddress);
                    _channel.CloseAsync();
                    return Stop();
                }
                return null;
            }, TimeSpan.FromSeconds(10));

            When(State.Ready, @event =>
            {
                if (@event.FsmEvent is Done && @event.StateData != null)
                {
                    @event.StateData.Tell(@event.FsmEvent);
                    return Stay().Using(null);
                }
                if (@event.FsmEvent is IServerOp)
                {
                    _controller.Tell(@event.FsmEvent);
                    return Stay();
                }
                if (@event.FsmEvent is INetworkOp)
                {
                    _log.Warning("client {0} sent unsupported message {1}", _channel.RemoteAddress, @event.FsmEvent);
                    return Stop();
                }
                var toClient = @event.FsmEvent as IToClient;
                if (toClient != null)
                {
                    if (toClient.Msg is IUnconfirmedClientOp)
                    {
                        _channel.WriteAndFlushAsync(toClient.Msg);
                        return Stay();                        
                    }
                    if (@event.StateData == null)
                    {
                        _channel.WriteAndFlushAsync(toClient.Msg);
                        return Stay().Using(Sender);
                    }

                    _log.Warning("cannot send {0} while waiting for previous ACK", toClient.Msg);
                    return Stay();
                }

                return null;
            });

            Initialize();
        }
    }
}

