//-----------------------------------------------------------------------
// <copyright file="Player.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote.Transport;
using Helios.Exceptions;
using Helios.Net;
using Helios.Topology;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// The Player is the client component of the
    /// test conductor extension. It registers with
    /// the conductor's controller
    ///  in order to participate in barriers and enable network failure injection
    /// </summary>
    partial class TestConductor //Player trait in JVM version
    {
        IActorRef _client;

        public IActorRef Client
        {
            get
            {
                if(_client == null) throw new IllegalStateException("TestConductor client not yet started");
                if(_system.TerminationTask.IsCompleted) throw new IllegalStateException("TestConductor unavailable because system is terminated; you need to StartNewSystem() before this point");
                return _client;
            }
        }

        /// <summary>
        /// Connect to the conductor on the given port (the host is taken from setting
        /// `akka.testconductor.host`). The connection is made asynchronously, but you
        /// should await completion of the returned Future because that implies that
        /// all expected participants of this test have successfully connected (i.e.
        /// this is a first barrier in itself). The number of expected participants is
        /// set in <see cref="TestConductor"/>`.startController()`.
        /// </summary>
        public Task<Done> StartClient(RoleName name, INode controllerAddr)
        {
            if(_client != null) throw new IllegalStateException("TestConductorClient already started");
            _client =
                _system.ActorOf(new Props(typeof (ClientFSM),
                    new object[] {name, controllerAddr}), "TestConductorClient");
            
            //TODO: IRequiresMessageQueue
            var a = _system.ActorOf(Props.Create<WaitForClientFSMToConnect>());

            return a.Ask<Done>(_client);
        }

        private class WaitForClientFSMToConnect : UntypedActor
        {
            IActorRef _waiting;

            protected override void OnReceive(object message)
            {
                var fsm = message as IActorRef;
                if (fsm != null)
                {
                    _waiting = Sender;
                    fsm.Tell(new FSMBase.SubscribeTransitionCallBack(Self));
                    return;
                }
                var transition = message as FSMBase.Transition<ClientFSM.State>;
                if (transition != null)
                {
                    if (transition.From == ClientFSM.State.Connecting && transition.To == ClientFSM.State.AwaitDone)
                        return;
                    if (transition.From == ClientFSM.State.AwaitDone && transition.To == ClientFSM.State.Connected)
                    {
                        _waiting.Tell(Done.Instance);
                        Context.Stop(Self);
                        return;
                    }
                    _waiting.Tell(new Exception("unexpected transition: " + transition));
                    Context.Stop(Self);
                }
                var currentState = message as FSMBase.CurrentState<ClientFSM.State>;
                if (currentState != null)
                {
                    if (currentState.State == ClientFSM.State.Connected)
                    {
                        _waiting.Tell(Done.Instance);
                        Context.Stop(Self);
                        return;

                    }
                }
            }
        }

        /// <summary>
        /// Enter the named barriers, one after the other, in the order given. Will
        /// throw an exception in case of timeouts or other errors.
        /// </summary>
        public void Enter(string name)
        {
            Enter(Settings.BarrierTimeout, ImmutableList.Create(name));
        }

        /// <summary>
        /// Enter the named barriers, one after the other, in the order given. Will
        /// throw an exception in case of timeouts or other errors.
        /// </summary>
        public void Enter(TimeSpan timeout, ImmutableList<string> names)
        {
            _system.Log.Debug("entering barriers {0}", names.Aggregate((a,b) => a = ", " + b));
            var stop = Deadline.Now + timeout;

            foreach (var name in names)
            {
                var barrierTimeout = stop.TimeLeft;
                if (barrierTimeout.Ticks < 0)
                {
                    _client.Tell(new ToServer<FailBarrier>(new FailBarrier(name)));
                    throw new TimeoutException("Server timed out while waiting for barrier " + name);
                }
                try
                {
                    var askTimeout = barrierTimeout + Settings.QueryTimeout;
                    //TODO: Wait?
                    _client.Ask(new ToServer<EnterBarrier>(new EnterBarrier(name, barrierTimeout)), askTimeout).Wait();
                }
                catch (OperationCanceledException)
                {
                    _client.Tell(new ToServer<FailBarrier>(new FailBarrier(name)));
                    throw new TimeoutException("Client timed out while waiting for barrier " + name);
                }
                _system.Log.Debug("passed barrier {0}", name);
            }
        }

        public Task<Address> GetAddressFor(RoleName name)
        {
            //TODO: QueryTimeout implicit?
            return _client.Ask<Address>(new ToServer<GetAddress>(new GetAddress(name)));
        }
    }

    /// <summary>
    /// This is the controlling entity on the player
    /// side: in a first step it registers itself with a symbolic name and its remote
    /// address at the <see cref="Controller"/>, then waits for the
    /// `Done` message which signals that all other expected test participants have
    /// done the same. After that, it will pass barrier requests to and from the
    /// coordinator and react to the Conductors’s
    /// requests for failure injection.
    /// 
    /// Note that you can't perform requests concurrently, e.g. enter barrier
    /// from one thread and ask for node address from another thread.
    /// 
    /// INTERNAL API.
    /// </summary>
    class ClientFSM : FSM<ClientFSM.State, ClientFSM.Data>, ILoggingFSM
        //TODO: RequireMessageQueue
    {
        public enum State
        {
            Connecting,
            AwaitDone,
            Connected,
            Failed
        }

        internal class Data
        {
            readonly RemoteConnection _channel;
            public RemoteConnection Channel { get { return _channel; } }
            readonly Tuple<string, IActorRef> _runningOp;
            public Tuple<string, IActorRef> RunningOp { get { return _runningOp; } }
            
            public Data(RemoteConnection channel, Tuple<string, IActorRef> runningOp)
            {
                _channel = channel;
                _runningOp = runningOp;
            }

            protected bool Equals(Data other)
            {
                return Equals(_channel, other._channel) && Equals(_runningOp, other._runningOp);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((Data) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((_channel != null ? _channel.GetHashCode() : 0) * 397) 
                        ^ (_runningOp != null ? _runningOp.GetHashCode() : 0);
                }
            }

            public static bool operator ==(Data left, Data right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(Data left, Data right)
            {
                return !Equals(left, right);
            }

            public Data Copy(Tuple<string, IActorRef> runningOp)
            {
                return new Data(Channel, runningOp);
            }
        }

        internal class Connected : INoSerializationVerificationNeeded
        {
            readonly RemoteConnection _channel;
            public RemoteConnection Channel{get { return _channel; }}

            public Connected(RemoteConnection channel)
            {
                _channel = channel;
            }

            protected bool Equals(Connected other)
            {
                return Equals(_channel, other._channel);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((Connected) obj);
            }

            public override int GetHashCode()
            {
                return (_channel != null ? _channel.GetHashCode() : 0);
            }

            public static bool operator ==(Connected left, Connected right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(Connected left, Connected right)
            {
                return !Equals(left, right);
            }
        }

        internal class ConnectionFailure : Exception
        {
            public ConnectionFailure(string message) : base(message)
            {
            }
        }

        internal class Disconnected
        {
            private Disconnected() { }
            private static readonly Disconnected _instance = new Disconnected();

            public static Disconnected Instance
            {
                get
                {
                    return _instance;
                }
            }            
        }

        private readonly ILoggingAdapter _log = Context.GetLogger();
        readonly TestConductorSettings _settings;
        readonly PlayerHandler _handler;
        readonly RoleName _name;

        public ClientFSM(RoleName name, INode controllerAddr)
        {
            _settings = TestConductor.Get(Context.System).Settings;
            _handler = new PlayerHandler(controllerAddr, _settings.ClientReconnects, _settings.ReconnectBackoff,
                _settings.ClientSocketWorkerPoolSize, Self, Logging.GetLogger(Context.System, "PlayerHandler"),
                Context.System.Scheduler);
            _name = name;

            InitFSM();
        }

        public void InitFSM()
        {
            StartWith(State.Connecting, new Data(null, null));

            When(State.Connecting, @event =>
            {
                if (@event.FsmEvent is IClientOp)
                {
                    return Stay().Replying(new Status.Failure(new IllegalStateException("not connected yet")));
                }
                var connected = @event.FsmEvent as Connected;
                if (connected != null)
                {
                    connected.Channel.Write(new Hello(_name.Name, TestConductor.Get(Context.System).Address));
                    return GoTo(State.AwaitDone).Using(new Data(connected.Channel, null));
                }
                if (@event.FsmEvent is ConnectionFailure)
                {
                    return GoTo(State.Failed);
                }
                if (@event.FsmEvent is StateTimeout)
                {
                    _log.Error("connect timeout to TestConductor");
                    return GoTo(State.Failed);
                }

                return null;
            }, _settings.ConnectTimeout);

            When(State.AwaitDone, @event =>
            {
                if (@event.FsmEvent is Done)
                {
                    _log.Debug("received Done: starting test");
                    return GoTo(State.Connected);
                }
                if (@event.FsmEvent is INetworkOp)
                {
                    _log.Error("Received {0} instead of Done", @event.FsmEvent);
                    return GoTo(State.Failed);
                }
                if (@event.FsmEvent is IServerOp)
                {
                    return Stay().Replying(new Failure(new IllegalStateException("not connected yet")));
                }
                if (@event.FsmEvent is StateTimeout)
                {
                    _log.Error("connect timeout to TestConductor");
                    return GoTo(State.Failed);
                }
                return null;
            }, _settings.BarrierTimeout);

            When(State.Connected, @event =>
            {
                if (@event.FsmEvent is Disconnected)
                {
                    _log.Info("disconnected from TestConductor");
                    throw new ConnectionFailure("disconnect");
                }
                if(@event.FsmEvent is ToServer<Done> && @event.StateData.Channel != null && @event.StateData.RunningOp == null)
                {
                    @event.StateData.Channel.Write(Done.Instance);
                    return Stay();
                }
                var toServer = @event.FsmEvent as IToServer;
                if (toServer != null && @event.StateData.Channel != null &&
                    @event.StateData.RunningOp == null)
                {
                    @event.StateData.Channel.Write(toServer.Msg);
                    string token = null;
                    var enterBarrier = @event.FsmEvent as ToServer<EnterBarrier>;
                    if (enterBarrier != null) token = enterBarrier.Msg.Name;
                    else
                    {
                        var getAddress = @event.FsmEvent as ToServer<GetAddress>;
                        if (getAddress != null) token = getAddress.Msg.Node.Name;
                    }
                    return Stay().Using(@event.StateData.Copy(runningOp: Tuple.Create(token, Sender)));
                }
                if (toServer != null && @event.StateData.Channel != null &&
                    @event.StateData.RunningOp != null)
                {
                    _log.Error("cannot write {0} while waiting for {1}", toServer.Msg, @event.StateData.RunningOp);
                    return Stay();
                }
                if (@event.FsmEvent is IClientOp && @event.StateData.Channel != null)
                {
                    var barrierResult = @event.FsmEvent as BarrierResult;
                    if (barrierResult != null)
                    {
                        if (@event.StateData.RunningOp == null)
                        {
                            _log.Warning("did not expect {1}", @event.FsmEvent);
                        }
                        else
                        {
                            object response;
                            if (barrierResult.Name != @event.StateData.RunningOp.Item1)
                            {
                                response =
                                    new Failure(
                                        new Exception("wrong barrier " + barrierResult + " received while waiting for " +
                                                      @event.StateData.RunningOp.Item1));
                            }
                            else if (!barrierResult.Success)
                            {
                                response =
                                    new Failure(
                                        new Exception("barrier failed:" + @event.StateData.RunningOp.Item1));
                            }
                            else
                            {
                                response = barrierResult.Name;
                            }
                            @event.StateData.RunningOp.Item2.Tell(response);
                        }
                        return Stay().Using(@event.StateData.Copy(runningOp: null));
                    }
                    var addressReply = @event.FsmEvent as AddressReply;
                    if (addressReply != null)
                    {
                        if (@event.StateData.RunningOp == null)
                        {
                            _log.Warning("did not expect {0}", @event.FsmEvent);
                        }
                        else
                        {
                            @event.StateData.RunningOp.Item2.Tell(addressReply.Addr);
                        }
                        return Stay().Using(@event.StateData.Copy(runningOp: null));
                    }
                    var throttleMsg = @event.FsmEvent as ThrottleMsg;
                    if (@event.FsmEvent is ThrottleMsg)
                    {
                        ThrottleMode mode;
                        if (throttleMsg.RateMBit < 0.0f) mode = Unthrottled.Instance;
                        else if (throttleMsg.RateMBit < 0.0f) mode = Blackhole.Instance;
                        else mode = new TokenBucket(1000, throttleMsg.RateMBit*125000, 0, 0);

                        var cmdTask =
                            TestConductor.Get(Context.System)
                                .Transport.ManagementCommand(new SetThrottle(throttleMsg.Target, throttleMsg.Direction,
                                    mode));

                        var self = Self;
                        cmdTask.ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                throw new ConfigurationException("Throttle was requested from the TestConductor, but no transport " +
                                    "adapters available that support throttling. Specify 'testTransport(on=true)' in your MultiNodeConfig");
                            self.Tell(new ToServer<Done>(Done.Instance));
                        });
                        return Stay();
                    }
                    if (@event.FsmEvent is DisconnectMsg)
                        return Stay(); //FIXME is this the right EC for the future below?
                    // FIXME: Currently ignoring, needs support from Remoting
                    var terminateMsg = @event.FsmEvent as TerminateMsg;
                    if (terminateMsg != null)
                    {
                        if (terminateMsg.ShutdownOrExit.IsLeft && terminateMsg.ShutdownOrExit.ToLeft().Value == false)
                        {
                            Context.System.Shutdown();
                            return Stay();
                        }
                        if (terminateMsg.ShutdownOrExit.IsLeft && terminateMsg.ShutdownOrExit.ToLeft().Value == true)
                        {
                            //TODO: terminate more aggressively with Abort
                            //Context.System.AsInstanceOf<ActorSystemImpl>().Abort();
                            Context.System.Shutdown();
                            return Stay();
                        }
                        if (terminateMsg.ShutdownOrExit.IsRight)
                        {
                            Environment.Exit(terminateMsg.ShutdownOrExit.ToRight().Value);
                            return Stay();
                        }
                    }
                    if (@event.FsmEvent is Done) return Stay(); //FIXME what should happen?
                }
                return null;
            });

            When(State.Failed, @event =>
            {
                if (@event.FsmEvent is IClientOp)
                {
                    return Stay().Replying(new Status.Failure(new Exception("cannot do " + @event.FsmEvent + " while failed")));
                }
                if (@event.FsmEvent is INetworkOp)
                {
                    _log.Warning("ignoring network message {0} while Failed", @event.FsmEvent);
                    return Stay();
                }
                return null;
            });

            OnTermination(@event =>
            {
                if (@event.StateData.Channel != null) @event.StateData.Channel.Close();
            });

            Initialize();            
        }
    }

    /// <summary>
    /// This handler only forwards messages received from the conductor to the <see cref="ClientFSM"/>
    /// 
    /// INTERNAL API.
    /// </summary>
    class PlayerHandler : IHeliosConnectionHandler
    {
        readonly INode _server;
        int _reconnects;
        readonly TimeSpan _backoff;
        readonly int _poolSize;
        readonly IActorRef _fsm;
        readonly ILoggingAdapter _log;
        readonly IScheduler _scheduler;
        private bool _loggedDisconnect = false;
        
        Deadline _nextAttempt;
        
        public PlayerHandler(INode server, int reconnects, TimeSpan backoff, int poolSize, IActorRef fsm,
            ILoggingAdapter log, IScheduler scheduler)
        {
            _server = server;
            _reconnects = reconnects;
            _backoff = backoff;
            _poolSize = poolSize;
            _fsm = fsm;
            _log = log;
            _scheduler = scheduler;

            Reconnect();
        }

        public void OnException(Exception ex, IConnection erroredChannel)
        {
            _log.Debug("channel {0} exception {1}", erroredChannel, ex);
            if (ex is HeliosConnectionException && _reconnects > 0)
            {
                _reconnects -= 1;
                _scheduler.Advanced.ScheduleOnce(_nextAttempt.TimeLeft, Reconnect);
                return;
            }
            _fsm.Tell(new ClientFSM.ConnectionFailure(ex.ToString()));
        }

        private void Reconnect()
        {
            _nextAttempt = Deadline.Now + _backoff;
            RemoteConnection.CreateConnection(Role.Client, _server, _poolSize, this);
        }

        public void OnConnect(INode remoteAddress, IConnection responseChannel)
        {
            _log.Debug("connected to {0}", responseChannel.RemoteHost);
            _fsm.Tell(new ClientFSM.Connected(new RemoteConnection(responseChannel, this)));
        }

        public void OnDisconnect(HeliosConnectionException cause, IConnection closedChannel)
        {
            if (!_loggedDisconnect) //added this to help mute log messages
            {
                _loggedDisconnect = true;
                _log.Debug("disconnected from {0}", closedChannel.RemoteHost);
                
            }
            _fsm.Tell(PoisonPill.Instance);
            //TODO: Some logic here in JVM version to execute this on a different pool to the Netty IO pool
            RemoteConnection.Shutdown(closedChannel);
        }

        public void OnMessage(object message, IConnection responseChannel)
        {
            _log.Debug("disconnected from {0}, {1}", responseChannel.RemoteHost, message);
            if (message is INetworkOp)
            {
                _fsm.Tell(message);
                return;
            }
            _log.Info("server {0} sent garbage '{1}', disconnecting", responseChannel.RemoteHost, message);
            responseChannel.Close();
        }
    }
}

