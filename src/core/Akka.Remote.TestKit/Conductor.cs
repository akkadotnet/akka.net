using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote.Transport;
using Akka.Util;
using Helios.Exceptions;
using Helios.Net;
using Helios.Topology;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// The conductor is the one orchestrating the test: it governs the
    /// <see cref="Akka.Remote.TestKit.Controller">'s ports to which all
    /// Players connect, it issues commands to their
    /// <see cref="Akka.Remote.TestKit.NetworkFailureInjector"></see> and provides support
    /// for barriers using the <see cref="Akka.Remote.TestKit.BarrierCoordinator"></see>.
    /// All of this is bundled inside the <see cref="TestConductor"/>
    /// </summary>
    partial class TestConductor //Conductor trait in JVM version
    {
        ActorRef _controller;

        public ActorRef Controller
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
        public async Task<INode> StartController(int participants, RoleName name, INode controllerPort)
        {
            if(_controller != null) throw new Exception("TestConductorServer was already started");
            _controller = _system.ActorOf(new Props(typeof (Controller), new object[] {participants, controllerPort}),
                "controller");
            //TODO: Need to review this async stuff
            var node = await _controller.Ask<INode>(TestKit.Controller.GetSockAddr.Instance).ConfigureAwait(false);
            await StartClient(name, node).ConfigureAwait(false);
            return node;
        }

        public Task<INode> SockAddr()
        {
            return _controller.Ask<INode>(TestKit.Controller.GetSockAddr.Instance);
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
            return Controller.Ask<Done>(new Throttle(node, target, direction, rateMBit));
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
            //TODO: What is helios equivalent of this?
            /*if(!Transport.DefaultAddress.Protocol.Contains(".trttl.gremlin."))
                throw new ConfigurationException("To use this feature you must activate the failure injector adapters " +
                    "(trttl, gremlin) by specifying `testTransport(on = true)` in your MultiNodeConfig.");*/
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
            return Controller.Ask<Done>(new Disconnect(node, target, false));
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
            return Controller.Ask<Done>(new Disconnect(node, target, true));
        }

        /// <summary>
        /// Tell the actor system at the remote node to shut itself down. The node will also be
        /// removed, so that the remaining nodes may still pass subsequent barriers.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="exitValue">is the return code which shall be given to System.exit</param>
        /// <returns></returns>
        public Task<Done> Exit(RoleName node, int exitValue)
        {
            // the recover is needed to handle ClientDisconnectedException exception,
            // which is normal during shutdown
            try
            {
                return Controller.Ask<Done>(new Terminate(node, new Right<bool, int>(exitValue)));
            }
            catch (Controller.ClientDisconnectedException)
            {
                return Task.FromResult(Done.Instance);
            }
        }

        /// <summary>
        /// Tell the actor system at the remote node to shut itself down without
        /// awaiting termination of remote-deployed children. The node will also be
        /// removed, so that the remaining nodes may still pass subsequent barriers.
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be affected</param>
        /// <param name="abort"></param>
        /// <returns></returns>
        public Task<Done> Shutdown(RoleName node, bool abort = false)
        {
            // the recover is needed to handle ClientDisconnectedException exception,
            // which is normal during shutdown
            try
            {
                return Controller.Ask<Done>(new Terminate(node, new Left<bool, int>(abort)));
            }
            catch (Controller.ClientDisconnectedException)
            {
                return Task.FromResult(Done.Instance);
            }
        }

        /// <summary>
        /// Obtain the list of remote host names currently registered.
        /// </summary>
        public Task<IEnumerable<RoleName>> GetNodes()
        {
            return Controller.Ask<IEnumerable<RoleName>>(TestKit.Controller.GetNodes.Instance);
        }

        /// <summary>
        /// Remove a remote host from the list, so that the remaining nodes may still
        /// pass subsequent barriers. This must be done before the client connection
        /// breaks down in order to affect an “orderly” removal (i.e. without failing
        /// present and future barriers).
        /// </summary>
        /// <param name="node">is the symbolic name of the node which is to be removed</param>
        /// <returns></returns>
        public Task<Done> RemoveNode(RoleName node)
        {
            return Controller.Ask<Done>(new Remove(node));
        }
    }

    /// <summary>
    /// This handler is what's used to process events which occur on <see cref="RemoteConnection"/>.
    /// 
    /// It's only purpose is to dispatch incoming messages to the right <see cref="ServerFSM"/> actor. There is
    /// one shared instance fo this class for all <see cref="IConnection"/>s accepted by one <see cref="Controller"/>.
    /// </summary>
    internal class ConductorHandler : IHeliosConnectionHandler
    {
        private readonly LoggingAdapter _log;
        private readonly ActorRef _controller;
        private readonly ConcurrentDictionary<IConnection, ActorRef> _clients = new ConcurrentDictionary<IConnection, ActorRef>();

        public ConductorHandler(ActorRef controller, LoggingAdapter log)
        {
            _controller = controller;
            _log = log;
        }

        public async void OnConnect(INode remoteAddress, IConnection responseChannel)
        {
            _log.Debug("connection from {0}", responseChannel.RemoteHost);
            //TODO: Seems wrong to create new RemoteConnection here
            var fsm = await _controller.Ask<ActorRef>(new Controller.CreateServerFSM(new RemoteConnection(responseChannel, this)), TimeSpan.FromMilliseconds(Int32.MaxValue));
            _clients.AddOrUpdate(responseChannel, fsm, (connection, @ref) => fsm);
        }

        public void OnDisconnect(HeliosConnectionException cause, IConnection closedChannel)
        {
            _log.Debug("disconnect from {0}", closedChannel.RemoteHost);
            var fsm = _clients[closedChannel];
            fsm.Tell(new Controller.ClientDisconnected(new RoleName(null)));
            ActorRef removedActor;
            _clients.TryRemove(closedChannel, out removedActor);
        }

        public void OnMessage(object message, IConnection responseChannel)
        {
            _log.Debug(string.Format("message from {0}: {1}", responseChannel.RemoteHost, message));
            if (message is INetworkOp)
            {
                _clients[responseChannel].Tell(message);
            }
            else
            {
                _log.Debug(string.Format("client {0} sent garbage `{1}`, disconnecting", responseChannel.RemoteHost, message));
                responseChannel.Close();
            }
        }

        public void OnException(Exception ex, IConnection erroredChannel)
        {
            _log.Warn(string.Format("handled network error from {0}: {1}", erroredChannel.RemoteHost, ex.Message));
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
    class ServerFSM : FSM<ServerFSM.State, ActorRef>, LoggingFSM
    {
        readonly RemoteConnection _channel;
        readonly ActorRef _controller;
        RoleName _roleName;

        public enum State
        {
            Initial,
            Ready
        }

        public ServerFSM(ActorRef controller, RemoteConnection channel)
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
                _channel.Close();
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
                    Log.Warning("client {0}, sent not Hello in first message (instead {1}), disconnecting", _channel.RemoteHost.ToEndPoint(), @event.FsmEvent);
                    _channel.Close();
                    return Stop();
                }
                if (@event.FsmEvent is IToClient)
                {
                    Log.Warning("cannot send {0} in state Initial", @event.FsmEvent);
                    return Stay();
                }
                if (@event.FsmEvent is StateTimeout)
                {
                    Log.Info("closing channel to {0} because of Hello timeout", _channel.RemoteHost.ToEndPoint());
                    _channel.Close();
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
                    Log.Warning("client {0} sent unsupported message {1}", _channel.RemoteHost.ToEndPoint(), @event.FsmEvent);
                    return Stop();
                }
                var toClient = @event.FsmEvent as IToClient;
                if (toClient != null)
                {
                    if (toClient.Msg is IUnconfirmedClientOp)
                    {
                        _channel.Write(toClient.Msg);
                        return Stay();                        
                    }
                    if (@event.StateData == null)
                    {
                        _channel.Write(toClient.Msg);
                        return Stay().Using(Sender);
                    }

                    Log.Warning("cannot send {0} while waiting for previous ACK", toClient.Msg);
                    return Stay();
                }

                return null;
            });

            Initialize();
        }
    }
}
