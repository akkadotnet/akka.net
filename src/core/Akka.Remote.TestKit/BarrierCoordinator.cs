//-----------------------------------------------------------------------
// <copyright file="BarrierCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Util.Internal;
using Akka.Event;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// 
    /// This barrier coordinator gets informed of players connecting (NodeInfo),
    /// players being deliberately removed (RemoveClient) or failing (ClientDisconnected)
    /// by the controller. It also receives EnterBarrier requests, where upon the first
    /// one received the name of the current barrier is set and all other known clients
    /// are expected to join the barrier, whereupon all of the will be sent the successful
    /// EnterBarrier return message. In case of planned removals, this may just happen
    /// earlier, in case of failures the current barrier (and all subsequent ones) will
    /// be failed by sending BarrierFailed responses.
    ///
    ///INTERNAL API.
    /// </summary>
    internal class BarrierCoordinator : FSM<BarrierCoordinator.State, BarrierCoordinator.Data>, ILoggingFSM
    {
        #region State types and messages

        public enum State
        {
            Idle,
            Waiting
        };

        public sealed class RemoveClient
        {
            public RemoveClient(RoleName name)
            {
                Name = name;
            }

            public RoleName Name { get; private set; }
        }

        public sealed class Data
        {
            public Data(IEnumerable<Controller.NodeInfo> clients, string barrier, IEnumerable<IActorRef> arrived, Deadline deadline) : 
                this(clients == null ? ImmutableHashSet.Create<Controller.NodeInfo>() : ImmutableHashSet.Create(clients.ToArray()), 
                barrier, 
                arrived == null ? ImmutableHashSet.Create<IActorRef>() : ImmutableHashSet.Create(arrived.ToArray()), 
                deadline)
            {
            }

            public Data(ImmutableHashSet<Controller.NodeInfo> clients, string barrier, ImmutableHashSet<IActorRef> arrived, Deadline deadline)
            {
                Deadline = deadline;
                Arrived = arrived;
                Barrier = barrier;
                Clients = clients;
            }

            public ImmutableHashSet<Controller.NodeInfo> Clients { get; private set; }

            public string Barrier { get; private set; }

            public ImmutableHashSet<IActorRef> Arrived { get; private set; }

            public Deadline Deadline { get; private set; }

            public Data Copy(ImmutableHashSet<Controller.NodeInfo> clients = null, string barrier = null,
                ImmutableHashSet<IActorRef> arrived = null, Deadline deadline = null)
            {
                return new Data(clients ?? Clients, 
                    barrier ?? Barrier,
                    arrived ?? Arrived,
                    deadline ?? Deadline);
            }

            private bool Equals(Data other)
            {
                return (ReferenceEquals(Clients, other.Clients) || Clients.SequenceEqual(other.Clients))
                    && string.Equals(Barrier, other.Barrier)
                    && (ReferenceEquals(Arrived, other.Arrived) || Arrived.SequenceEqual(other.Arrived))
                    && Equals(Deadline, other.Deadline);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is Data && Equals((Data) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = (Clients != null ? Clients.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (Barrier != null ? Barrier.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (Arrived != null ? Arrived.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (Deadline != null ? Deadline.GetHashCode() : 0);
                    return hashCode;
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
        }

        public sealed class BarrierTimeoutException : Exception
        {
            public BarrierTimeoutException(Data barrierData)
                : base(string.Format("timeout while waiting for barrier '{0}'", barrierData.Barrier))
            {
                BarrierData = barrierData;
            }

            public Data BarrierData { get; private set; }

            private bool Equals(BarrierTimeoutException other)
            {
                return Equals(BarrierData, other.BarrierData);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is BarrierTimeoutException && Equals((BarrierTimeoutException) obj);
            }

            public override int GetHashCode()
            {
                return (BarrierData != null ? BarrierData.GetHashCode() : 0);
            }

            public static bool operator ==(BarrierTimeoutException left, BarrierTimeoutException right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(BarrierTimeoutException left, BarrierTimeoutException right)
            {
                return !Equals(left, right);
            }
        }

        public sealed class FailedBarrierException : Exception
        {
            public FailedBarrierException(Data barrierData)
                : base(string.Format("failing barrier '{0}'", barrierData.Barrier))
            {
                BarrierData = barrierData;
            }

            public Data BarrierData { get; private set; }

            private bool Equals(FailedBarrierException other)
            {
                return Equals(BarrierData, other.BarrierData);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is FailedBarrierException && Equals((FailedBarrierException) obj);
            }

            public override int GetHashCode()
            {
                return (BarrierData != null ? BarrierData.GetHashCode() : 0);
            }

            public static bool operator ==(FailedBarrierException left, FailedBarrierException right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(FailedBarrierException left, FailedBarrierException right)
            {
                return !Equals(left, right);
            }
        }

        public sealed class DuplicateNodeException : Exception
        {
            public DuplicateNodeException(Data barrierData, Controller.NodeInfo node)
                : base(string.Format(node.ToString()))
            {
                Node = node;
                BarrierData = barrierData;
            }

            public Data BarrierData { get; private set; }

            public Controller.NodeInfo Node { get; private set; }

            private bool Equals(DuplicateNodeException other)
            {
                return Equals(BarrierData, other.BarrierData) && Equals(Node, other.Node);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is DuplicateNodeException && Equals((DuplicateNodeException) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((BarrierData != null ? BarrierData.GetHashCode() : 0)*397) ^ (Node != null ? Node.GetHashCode() : 0);
                }
            }

            public static bool operator ==(DuplicateNodeException left, DuplicateNodeException right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(DuplicateNodeException left, DuplicateNodeException right)
            {
                return !Equals(left, right);
            }
        }

        public sealed class WrongBarrierException : Exception
        {
            public WrongBarrierException(string barrier, IActorRef client, Data barrierData)
                : base(string.Format("tried"))
            {
                BarrierData = barrierData;
                Client = client;
                Barrier = barrier;
            }

            public string Barrier { get; private set; }

            public IActorRef Client { get; private set; }

            public Data BarrierData { get; private set; }

            private bool Equals(WrongBarrierException other)
            {
                return string.Equals(Barrier, other.Barrier) && Equals(Client, other.Client) && Equals(BarrierData, other.BarrierData);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is WrongBarrierException && Equals((WrongBarrierException) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = (Barrier != null ? Barrier.GetHashCode() : 0);
                    hashCode = (hashCode*397) ^ (Client != null ? Client.GetHashCode() : 0);
                    hashCode = (hashCode*397) ^ (BarrierData != null ? BarrierData.GetHashCode() : 0);
                    return hashCode;
                }
            }

            public static bool operator ==(WrongBarrierException left, WrongBarrierException right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(WrongBarrierException left, WrongBarrierException right)
            {
                return !Equals(left, right);
            }
        }

        public sealed class BarrierEmptyException : Exception
        {
            public BarrierEmptyException(Data barrierData, string message)
                : base(message)
            {
                BarrierData = barrierData;
            }

            public Data BarrierData { get; private set; }

            private bool Equals(BarrierEmptyException other)
            {
                return Equals(BarrierData, other.BarrierData);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is BarrierEmptyException && Equals((BarrierEmptyException) obj);
            }

            public override int GetHashCode()
            {
                return (BarrierData != null ? BarrierData.GetHashCode() : 0);
            }

            public static bool operator ==(BarrierEmptyException left, BarrierEmptyException right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(BarrierEmptyException left, BarrierEmptyException right)
            {
                return !Equals(left, right);
            }
        }

        public sealed class ClientLostException : Exception
        {
            public ClientLostException(Data barrierData, RoleName client)
                : base(string.Format("unannounced disconnect of {0}", client))
            {
                Client = client;
                BarrierData = barrierData;
            }

            public Data BarrierData { get; private set; }

            public RoleName Client { get; private set; }

            private bool Equals(ClientLostException other)
            {
                return Equals(BarrierData, other.BarrierData) && Equals(Client, other.Client);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is ClientLostException && Equals((ClientLostException) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((BarrierData != null ? BarrierData.GetHashCode() : 0) * 397) 
                        ^ (Client != null ? Client.GetHashCode() : 0);
                }
            }

            public static bool operator ==(ClientLostException left, ClientLostException right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(ClientLostException left, ClientLostException right)
            {
                return !Equals(left, right);
            }
        }

        #endregion

        public BarrierCoordinator()
        {
            InitFSM();
        }

        //this shall be set to true if all subsequent barriers shall fail
        private bool _failed = false;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        protected override void PreRestart(Exception reason, object message) { }
        protected override void PostRestart(Exception reason)
        {
            _failed = true;
        }

        protected void InitFSM()
        {
            StartWith(State.Idle, new Data(ImmutableHashSet.Create<Controller.NodeInfo>(), "", ImmutableHashSet.Create<IActorRef>(), null));

            WhenUnhandled(@event =>
            {
                State<State, Data> nextState = null;
                var clients = @event.StateData.Clients;
                var arrived = @event.StateData.Arrived;
                @event.FsmEvent.Match()
                    .With<Controller.NodeInfo>(node =>
                    {
                        if (clients.Any(x => x.Name == node.Name)) throw new DuplicateNodeException(@event.StateData, node);
                        nextState = Stay().Using(@event.StateData.Copy(clients.Add(node)));
                    })
                    .With<Controller.ClientDisconnected>(disconnected =>
                    {
                        if (arrived == null || arrived.Count == 0)
                            nextState =
                                Stay()
                                    .Using(
                                        @event.StateData.Copy(clients.Where(x => x.Name != disconnected.Name).ToImmutableHashSet()));
                        else
                        {
                            var client = clients.FirstOrDefault(x => x.Name == disconnected.Name);
                            if (client == null) nextState = Stay();
                            else
                            {
                                throw new ClientLostException(@event.StateData.Copy(clients.Remove(client), arrived:arrived.Where(x => x != client.FSM).ToImmutableHashSet()), disconnected.Name);
                            }
                        }
                    });

                return nextState;
            });

            When(State.Idle, @event =>
            {
                State<State, Data> nextState = null;
                var clients = @event.StateData.Clients;
                @event.FsmEvent.Match()
                    .With<EnterBarrier>(barrier =>
                    {
                        if (_failed)
                            nextState =
                                Stay().Replying(new ToClient<BarrierResult>(new BarrierResult(barrier.Name, false)));
                        else if (clients.Select(x => x.FSM).SequenceEqual(new List<IActorRef>() {Sender}))
                            nextState =
                                Stay().Replying(new ToClient<BarrierResult>(new BarrierResult(barrier.Name, true)));
                        else if (clients.All(x => x.FSM != Sender))
                            nextState =
                                Stay().Replying(new ToClient<BarrierResult>(new BarrierResult(barrier.Name, false)));
                        else
                        {
                            nextState =
                                GoTo(State.Waiting)
                                    .Using(@event.StateData.Copy(barrier: barrier.Name,
                                        arrived: ImmutableHashSet.Create(Sender),
                                        deadline: GetDeadline(barrier.Timeout)));
                        }
                    })
                    .With<RemoveClient>(client =>
                    {
                        if (clients.Count == 0)
                            throw new BarrierEmptyException(@event.StateData,
                                string.Format("cannot remove {0}: no client to remove", client.Name));
                        nextState =
                            Stay().Using(@event.StateData.Copy(clients.Where(x => x.Name != client.Name).ToImmutableHashSet()));
                    });

                return nextState;
            });

            When(State.Waiting, @event =>
            {
                State<State, Data> nextState = null;
                var currentBarrier = @event.StateData.Barrier;
                var clients = @event.StateData.Clients;
                var arrived = @event.StateData.Arrived;
                @event.FsmEvent.Match()
                    .With<EnterBarrier>(barrier =>
                    {
                        if (barrier.Name != currentBarrier)
                            throw new WrongBarrierException(barrier.Name, Sender, @event.StateData);
                        var together = clients.Any(x => x.FSM == Sender)
                            ? @event.StateData.Arrived.Add(Sender)
                            : @event.StateData.Arrived;
                        var enterDeadline = GetDeadline(barrier.Timeout);
                        //we only allow the deadlines to get shorter
                        if (enterDeadline.TimeLeft < @event.StateData.Deadline.TimeLeft)
                        {
                            SetTimer("Timeout", new StateTimeout(), enterDeadline.TimeLeft, false);
                            nextState = HandleBarrier(@event.StateData.Copy(arrived: together, deadline: enterDeadline));
                        }
                        else
                        {
                            nextState = HandleBarrier(@event.StateData.Copy(arrived: together));
                        }
                    })
                    .With<RemoveClient>(client =>
                    {
                        var removedClient = clients.FirstOrDefault(x => x.Name == client.Name);
                        if (removedClient == null) nextState = Stay();
                        else
                        {
                            nextState =
                                HandleBarrier(@event.StateData.Copy(clients.Remove(removedClient),
                                    arrived: arrived.Where(x => x != removedClient.FSM).ToImmutableHashSet()));
                        }
                    })
                    .With<FailBarrier>(barrier =>
                    {
                        if(barrier.Name != currentBarrier) throw new WrongBarrierException(barrier.Name, Sender, @event.StateData);
                        throw new FailedBarrierException(@event.StateData);
                    })
                    .With<StateTimeout>(() =>
                    {
                        throw new BarrierTimeoutException(@event.StateData);
                    });

                return nextState;
            });

            OnTransition((state, nextState) =>
            {
                if (state == State.Idle && nextState == State.Waiting) SetTimer("Timeout", new StateTimeout(), NextStateData.Deadline.TimeLeft, false);
                else if(state == State.Waiting && nextState == State.Idle) CancelTimer("Timeout");
            });

            Initialize();
        }

        public State<State,Data> HandleBarrier(Data data)
        {
            _log.Debug("handleBarrier({0})", data.Barrier);
            if (data.Arrived.Count == 0)
            {
                return GoTo(State.Idle).Using(data.Copy(barrier: string.Empty));
            }
            else if (data.Clients.Select(x => x.FSM).ToImmutableHashSet().Except(data.Arrived).Count == 0)
            {
                foreach (var arrived in data.Arrived)
                {
                    arrived.Tell(new ToClient<BarrierResult>(new BarrierResult(data.Barrier, true)));
                }
                return
                    GoTo(State.Idle)
                        .Using(data.Copy(barrier: string.Empty, arrived: ImmutableHashSet.Create<IActorRef>()));
            }
            else
            {
                return Stay().Using(data);
            }
        }

        public Deadline GetDeadline(TimeSpan? timeout)
        {
            return Deadline.Now + timeout.GetOrElse(TestConductor.Get(Context.System).Settings.BarrierTimeout);
        }
    }
}

