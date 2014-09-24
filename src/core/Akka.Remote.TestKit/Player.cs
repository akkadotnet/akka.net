using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
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
        ActorRef _client;

        public ActorRef Client
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
            
            //TODO: RequiresMessageQueue
            var a = _system.ActorOf(Props.Create<WaitForClientFSMToConnect>());

            return a.Ask<Done>(_client);
        }

        private class WaitForClientFSMToConnect : UntypedActor
        {
            ActorRef _waiting;

            protected override void OnReceive(object message)
            {
                var fsm = message as ActorRef;
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
            _system.Log.Debug("entering barriers " + names.Aggregate((a,b) => a = ", " + b));
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
                _system.Log.Debug("passed barrer {0}", name);
            }
        }
    }

    public class ClientFSM : FSM<ClientFSM.State, ClientFSM.Data>, LoggingFSM
    {
        public enum State
        {
            Connecting,
            AwaitDone,
            Connected,
            Failed
        }

        public class Data
        {
            
        }
    }
}
