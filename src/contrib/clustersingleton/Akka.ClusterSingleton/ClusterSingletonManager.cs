﻿using System;
using System.Collections.Immutable;
using System.Linq;

using Akka.Actor;
using Akka.Cluster;
using Akka.Event;
using Akka.Remote;
using Akka.Util;

namespace Akka.ClusterSingleton
{
    internal sealed class HandOverToMe { }

    internal sealed class HandOverInProgress { }

    internal sealed class HandOverDone { }

    internal sealed class TakeOverFromMe { }

    internal sealed class Cleanup { }

    internal sealed class StartOldestChangedBuffer { }

    internal sealed class HandOverRetry
    {
        public int Count { get; set; }
    }

    internal sealed class TakeOverRetry
    {
        public int Count { get; set; }
    }

    internal interface IClusterSingletonData { }

    internal sealed class Uninitialized : IClusterSingletonData
    {
        private Uninitialized() { }
        static Uninitialized() { }

        private static readonly Uninitialized _instance = new Uninitialized();
        public static Uninitialized Instance { get { return _instance; } }
    }

    internal sealed class YoungerData : IClusterSingletonData
    {
        public Address Oldest { get; set; }
    }

    internal sealed class BecomingOldestData : IClusterSingletonData
    {
        public Address PreviousOldest { get; set; }
    }

    internal sealed class OldestData : IClusterSingletonData
    {
        public IActorRef Singleton { get; set; }
        public bool SingletonTerminated { get; set; }
    }

    internal sealed class WasOldestData : IClusterSingletonData
    {
        public IActorRef Singleton { get; set; }
        public bool SingletonTerminated { get; set; }
        public Address NewOldest { get; set; }
    }

    internal sealed class HandingOverData : IClusterSingletonData
    {
        public IActorRef Singleton { get; set; }
        public IActorRef HandOverTo { get; set; }

    }

    internal sealed class EndData : IClusterSingletonData { }

    internal enum ClusterSingletonState
    {
        Start,
        Oldest,
        Younger,
        BecomingOldest,
        WasOldest,
        HandingOver,
        TakeOver,
        End
    }

    internal sealed class OldestChangedBuffer : UntypedActor
    {

        public sealed class GetNext { }

        public sealed class InitialOldestState
        {
            public Address Oldest { get; set; }
            public bool SafeToBeOldest { get; set; }
        }

        public sealed class OldestChanged
        {
            public Address Oldest { get; set; }
        }

        public OldestChangedBuffer(string role)
        {
            _role = role;
        }

        private string _role;
        private ImmutableSortedSet<Member> _membersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(MemberAgeOrdering.Descending);
        private ImmutableQueue<object> _changes = ImmutableQueue<object>.Empty;

        private readonly Akka.Cluster.Cluster _cluster = Akka.Cluster.Cluster.Get(Context.System);

        private void TrackChanges(Action block)
        {
            var before = _membersByAge.FirstOrDefault();
            block();
            var after = _membersByAge.FirstOrDefault();

            // todo: fix neq comparison
            if (before != after)
                _changes = _changes.Enqueue(new OldestChanged() { Oldest = MemberAddressOrDefault(after) });
        }

        private bool MatchingRole(Member member)
        {
            if (String.IsNullOrEmpty(_role)) return true;
            return member.HasRole(_role);
        }

        private Address MemberAddressOrDefault(Member member)
        {
            return (member == null) ? null : member.Address;
        }

        private void HandleInitial(ClusterEvent.CurrentClusterState state)
        {
            _membersByAge = state.Members.Where(m => (m.Status == MemberStatus.Up || m.Status == MemberStatus.Leaving) && MatchingRole(m)).ToImmutableSortedSet(MemberAgeOrdering.Descending);
            var safeToBeOldest = !state.Members.Any(m => m.Status == MemberStatus.Down || m.Status == MemberStatus.Exiting);
            var initial = new InitialOldestState()
                          {
                              Oldest = MemberAddressOrDefault(_membersByAge.FirstOrDefault()),
                              SafeToBeOldest = safeToBeOldest
                          };
            _changes = _changes.Enqueue(initial);
        }

        private void Add(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() => _membersByAge = _membersByAge.Add(member));
        }

        private void Remove(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() => _membersByAge = _membersByAge.Remove(member));
        }

        private void SendFirstChange()
        {
            object change;
            _changes = _changes.Dequeue(out change);
            Context.Parent.Tell(change);
        }

        protected override void PreStart()
        {
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent) });
        }

        protected override void PostStop()
        {
            _cluster.Unsubscribe(Self);
        }

        protected override void OnReceive(object message)
        {
            if (message is ClusterEvent.CurrentClusterState) HandleInitial((ClusterEvent.CurrentClusterState)message);
            else if (message is ClusterEvent.MemberUp) Add(((ClusterEvent.MemberUp)message).Member);
            else if (message is ClusterEvent.MemberExited || message is ClusterEvent.MemberRemoved) Remove(((ClusterEvent.IMemberEvent)(message)).Member);
            else if (message is GetNext && _changes.IsEmpty) Context.Become(OnDeliverNext, discardOld: false);
            else if (message is GetNext) SendFirstChange();
        }

        private void OnDeliverNext(object message)
        {
            if (message is ClusterEvent.CurrentClusterState)
            {
                HandleInitial((ClusterEvent.CurrentClusterState)message);
                SendFirstChange();
                Context.Unbecome();
            }
            else if (message is ClusterEvent.MemberUp)
            {
                var memberUp = (ClusterEvent.MemberUp)message;
                Add(memberUp.Member);
                if (!_changes.IsEmpty)
                {
                    SendFirstChange();
                    Context.Unbecome();
                }
            }
            else if (message is ClusterEvent.MemberExited || message is ClusterEvent.MemberRemoved)
            {
                var memberEvent = (ClusterEvent.IMemberEvent)message;
                Remove(memberEvent.Member);
                if (!_changes.IsEmpty)
                {
                    SendFirstChange();
                    Context.Unbecome();
                }
            }
        }
    }

    public sealed class ClusterSingletonManagerIsStuck : AkkaException
    {
        public ClusterSingletonManagerIsStuck(string message)
            : base(message) { }
    }


    internal sealed class ClusterSingletonManagerActor : FSM<ClusterSingletonState, IClusterSingletonData>
    {
        private const string HandOverRetryTimer = "hand-over-retry";
        private const string TakeOverRetryTimer = "take-over-retry";
        private const string CleanupTimer = "cleanup";

        private bool _oldestChangedReceived = true;
        private bool _selfExited;
        private IActorRef _oldestChangedBuffer;
        private ImmutableDictionary<Address, Deadline> _removed = ImmutableDictionary<Address, Deadline>.Empty;
        private readonly string _role;
        private readonly Props _singletonProps;
        private readonly TimeSpan _retryInterval;
        private readonly string _singletonName;
        private readonly int _maxHandOverRetries;
        private readonly int _maxTakeOverRetries;
        private readonly object _terminationMessage;
        private readonly Akka.Cluster.Cluster _cluster = Akka.Cluster.Cluster.Get(Context.System);
        private readonly ILoggingAdapter _log = Logging.GetLogger(Context.System, "ClusterSingletonManager");

        public ClusterSingletonManagerActor(
                Props singletonProps,
                string singletonName,
                object terminationMessage,
                string role,
                int maxHandOverRetries,
                int maxTakeOverRetries,
                TimeSpan retryInterval
            )
        {
            //Guard.Assert(
            //    maxTakeOverRetries < maxHandOverRetries,
            //    String.Format(
            //        "maxTakeOverRetries [{0}]must be < maxHandOverRetries [{1}]",
            //        maxTakeOverRetries,
            //        maxHandOverRetries));

            //Guard.Assert(
            //    string.IsNullOrEmpty(role) || _cluster.SelfRoles.Contains(role),
            //    String.Format("This cluster member [{0}] doesn't have the role [{1}]", _cluster.SelfAddress, role));

            _singletonProps = singletonProps;
            _singletonName = singletonName;
            _role = role;
            _maxHandOverRetries = maxHandOverRetries;
            _maxTakeOverRetries = maxTakeOverRetries;
            _retryInterval = retryInterval;
            _terminationMessage = terminationMessage;
            InitializeFSM();
        }

        protected override void PreStart()
        {
            //Guard.Assert(!_cluster.IsTerminated, "Cluster node must not be terminated");

            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.MemberExited), typeof(ClusterEvent.MemberRemoved) });
            SetTimer(CleanupTimer, new Cleanup(), TimeSpan.FromMinutes(1.0), repeat: true);
            var self = Self;
            _cluster.RegisterOnMemberUp(() => self.Tell(new StartOldestChangedBuffer()));
        }

        protected override void PostStop()
        {
            CancelTimer(CleanupTimer);
            _cluster.Unsubscribe(Self);
        }

        private void AddRemoved(Address address)
        {
            _removed = _removed.Add(address, Deadline.Now + TimeSpan.FromMinutes(15.0));
        }

        private void CleanupOverdueNotMemberAnyMore()
        {
            _removed = _removed.Where(kv => kv.Value.IsOverdue).ToImmutableDictionary();
        }

        private ActorSelection Peer(Address at)
        {
            return Context.ActorSelection(Self.Path.ToStringWithAddress(at));
        }

        private void GetNextOldestChange()
        {
            if (_oldestChangedReceived)
            {
                _oldestChangedReceived = false;
                _oldestChangedBuffer.Tell(new OldestChangedBuffer.GetNext());
            }
        }

        private State<ClusterSingletonState, IClusterSingletonData> GoToOldest()
        {
            LogInfo("Singleton manager [{0}] starting singleton actor", _cluster.SelfAddress);
            var singleton = Context.Watch(Context.ActorOf(_singletonProps, _singletonName));
            return
                GoTo(ClusterSingletonState.Oldest)
                    .Using(new OldestData() { Singleton = singleton, SingletonTerminated = false });
        }

        private State<ClusterSingletonState, IClusterSingletonData> HandOverDone(IActorRef handOverTo)
        {
            Address newOldest = null;

            if (handOverTo != null)
            {
                newOldest = handOverTo.Path.Address;
                handOverTo.Tell(new HandOverDone());
            }

            LogInfo("Singleton terminated, hand-over done [{0} -> {1}]", _cluster.SelfAddress, newOldest);

            if (_removed.ContainsKey(_cluster.SelfAddress))
            {
                LogInfo("Self removed, stopping ClusterSingletonManager");
                return Stop();
            }
            else if (_selfExited)
            {
                return GoTo(ClusterSingletonState.End).Using(new EndData());
            }

            return GoTo(ClusterSingletonState.Younger).Using(new YoungerData { Oldest = newOldest });
        }

        private State<ClusterSingletonState, IClusterSingletonData> GoToHandingOver(IActorRef singleton, bool singletonTerminated, IActorRef handOverTo)
        {
            if (singletonTerminated) return HandOverDone(handOverTo);

            if (handOverTo != null) handOverTo.Tell(new HandOverInProgress());


            singleton.Tell(_terminationMessage);

            return
                GoTo(ClusterSingletonState.HandingOver)
                    .Using(new HandingOverData { Singleton = singleton, HandOverTo = handOverTo });
        }


        private void InitializeFSM()
        {
            When(ClusterSingletonState.Start,
                @event =>
                {
                    State<ClusterSingletonState, IClusterSingletonData> nextState = null;
                    @event.FsmEvent.Match().With<StartOldestChangedBuffer>(
                        buffer =>
                        {
                            _oldestChangedBuffer =
                                Context.ActorOf(
                                    Actor.Props.Create<OldestChangedBuffer>(_role)
                                        .WithDispatcher(Context.Props.Dispatcher));
                            GetNextOldestChange();
                            nextState = Stay();
                        }).With<OldestChangedBuffer.InitialOldestState>(
                            initialOldestState =>
                            {
                                _oldestChangedReceived = true;
                                var isSelfOldest = _cluster.SelfAddress.Equals(initialOldestState.Oldest);
                                if (isSelfOldest && initialOldestState.SafeToBeOldest)
                                    nextState = GoToOldest();
                                else if (isSelfOldest)
                                    nextState =
                                        GoTo(ClusterSingletonState.BecomingOldest).Using(new BecomingOldestData());
                                else
                                    nextState =
                                        GoTo(ClusterSingletonState.Younger)
                                            .Using(new YoungerData { Oldest = initialOldestState.Oldest });
                            });
                    return nextState;
                });

            When(ClusterSingletonState.Younger,
                @event =>
                {
                    State<ClusterSingletonState, IClusterSingletonData> nextState = null;
                    @event.FsmEvent.Match().With<OldestChangedBuffer.OldestChanged>(
                        oldestChanged =>
                        {
                            StateData.Match().With<YoungerData>(
                                youngerData =>
                                {
                                    LogInfo("Younger observed OldestChanged: [{0} -> myself]", youngerData.Oldest);

                                    _oldestChangedReceived = true;
                                    if (oldestChanged.Oldest.Equals(_cluster.SelfAddress))
                                    {
                                        if (youngerData.Oldest == null) nextState = GoToOldest();
                                        else if (_removed.ContainsKey(youngerData.Oldest)) nextState = GoToOldest();
                                        else
                                        {
                                            Peer(youngerData.Oldest).Tell(new HandOverToMe());
                                            nextState =
                                                GoTo(ClusterSingletonState.BecomingOldest)
                                                    .Using(
                                                        new BecomingOldestData { PreviousOldest = youngerData.Oldest });
                                        }
                                    }
                                    else
                                    {
                                        GetNextOldestChange();
                                        nextState = Stay().Using(new YoungerData { Oldest = oldestChanged.Oldest });
                                    }
                                });
                        }).With<ClusterEvent.MemberRemoved>(
                            memberRemoved =>
                            {
                                StateData.Match().With<YoungerData>(
                                    youngerData =>
                                    {
                                        if (youngerData.Oldest != null
                                            && memberRemoved.Member.Address.Equals(youngerData.Oldest))
                                        {
                                            LogInfo("Previous oldest removed [{0}]", memberRemoved.Member.Address);
                                            AddRemoved(memberRemoved.Member.Address);
                                            nextState = Stay().Using(new YoungerData());
                                        }
                                    });

                                if (memberRemoved.Member.Address.Equals(_cluster.SelfAddress))
                                {
                                    LogInfo("Self removed, stopping ClusterSingletonManager");
                                    nextState = Stop();
                                }
                            });

                    return nextState;
                });

            When(ClusterSingletonState.BecomingOldest,
                @event =>
                {
                    State<ClusterSingletonState, IClusterSingletonData> nextState = null;
                    @event.FsmEvent.Match().With<HandOverInProgress>(
                        () =>
                        {
                            LogInfo("Hand-over in progress at [{0}]", Sender.Path.Address);
                            CancelTimer(HandOverRetryTimer);
                            nextState = Stay();
                        }).With<HandOverDone>(
                            () =>
                            {
                                StateData.Match().With<BecomingOldestData>(
                                    becomingOldest =>
                                    {
                                        if (becomingOldest.PreviousOldest != null)
                                        {
                                            if (Sender.Path.Address.Equals(becomingOldest.PreviousOldest)) nextState = GoToOldest();
                                            else
                                            {
                                                LogInfo(
                                                    "Ignoring HandOverDone in BecomingOldest from [{0}]. Expected previous oldest [{1}]",
                                                    Sender.Path.Address,
                                                    becomingOldest.PreviousOldest);
                                                nextState = Stay();
                                            }
                                        }
                                    });
                            }).With<ClusterEvent.MemberRemoved>(
                                memberRemoved =>
                                {
                                    StateData.Match().With<BecomingOldestData>(
                                        becomingOldest =>
                                        {
                                            if (becomingOldest.PreviousOldest != null
                                                && memberRemoved.Member.Address.Equals(becomingOldest.PreviousOldest))
                                            {
                                                LogInfo("Previous oldest [{0}] removed", becomingOldest.PreviousOldest);
                                                AddRemoved(memberRemoved.Member.Address);
                                                nextState = Stay();
                                            }
                                        });
                                }).With<TakeOverFromMe>(
                                    () =>
                                    {
                                        StateData.Match().With<BecomingOldestData>(
                                            becomingOldest =>
                                            {
                                                if (becomingOldest.PreviousOldest == null)
                                                {
                                                    Sender.Tell(new HandOverToMe());
                                                    nextState =
                                                        Stay()
                                                            .Using(
                                                                new BecomingOldestData
                                                                {
                                                                    PreviousOldest =
                                                                        Sender.Path.Address
                                                                });
                                                }
                                                else
                                                {
                                                    if (becomingOldest.PreviousOldest.Equals(Sender.Path.Address))
                                                    {
                                                        Sender.Tell(new HandOverToMe());
                                                    }
                                                    else
                                                    {
                                                        LogInfo(
                                                            "Ignoring TakeOver request in BecomingOldest from [{0}]. Expected previous oldest [{1}]",
                                                            Sender.Path.Address,
                                                            becomingOldest.PreviousOldest);
                                                    }
                                                    nextState = Stay();
                                                }
                                            });
                                    }).With<HandOverRetry>(
                                        handOverRetry =>
                                        {
                                            StateData.Match().With<BecomingOldestData>(
                                                becomingOldest =>
                                                {
                                                    if (handOverRetry.Count <= _maxHandOverRetries)
                                                    {
                                                        LogInfo(
                                                            "Retry [{0}], sending HandOverToMe to [{1}]",
                                                            handOverRetry.Count,
                                                            becomingOldest.PreviousOldest);
                                                        if (becomingOldest.PreviousOldest != null) Peer(becomingOldest.PreviousOldest).Tell(new HandOverToMe());
                                                        SetTimer(
                                                            HandOverRetryTimer,
                                                            new HandOverRetry { Count = handOverRetry.Count + 1 },
                                                            _retryInterval,
                                                            repeat: false);
                                                        nextState = Stay();
                                                    }
                                                    else if (becomingOldest.PreviousOldest != null
                                                             && _removed.ContainsKey(becomingOldest.PreviousOldest))
                                                    {
                                                        // can't send HandOverToMe, previousOldest unknown for new node (or restart)
                                                        // previous oldest might be down or removed, so no TakeOverFromMe message is received
                                                        LogInfo(
                                                            "Timeout in BecomingOldest. Previous oldest unknown, removed and no TakeOver request.");
                                                        nextState = GoToOldest();
                                                    }
                                                    else
                                                    {
                                                        throw new ClusterSingletonManagerIsStuck(
                                                            String.Format(
                                                                "Becoming singleton oldest was stuck because previous oldest [{0}] is unresponsive",
                                                                becomingOldest.PreviousOldest));
                                                    }
                                                });
                                        });

                    return nextState;
                });

            When(
                ClusterSingletonState.Oldest,
                @event =>
                {
                    State<ClusterSingletonState, IClusterSingletonData> nextState = null;
                    @event.FsmEvent.Match().With<OldestChangedBuffer.OldestChanged>(
                        oldestChanged =>
                        {
                            StateData.Match().With<OldestData>(
                                oldestData =>
                                {
                                    LogInfo(
                                        "Oldest observed OldestChanged: [{0} -> {1}]",
                                        _cluster.SelfAddress,
                                        oldestChanged.Oldest);

                                    _oldestChangedReceived = true;
                                    if (oldestChanged.Oldest != null)
                                    {
                                        if (oldestChanged.Oldest.Equals(_cluster.SelfAddress))
                                        {
                                            nextState = Stay();
                                        }
                                        else if (!_selfExited && _removed.ContainsKey(oldestChanged.Oldest))
                                        {
                                            nextState = GoToHandingOver(
                                                oldestData.Singleton,
                                                oldestData.SingletonTerminated,
                                                null);
                                        }
                                        else
                                        {
                                            Peer(oldestChanged.Oldest).Tell(new TakeOverFromMe());
                                            SetTimer(
                                                TakeOverRetryTimer,
                                                new TakeOverRetry { Count = 1 },
                                                _retryInterval);
                                            nextState =
                                                GoTo(ClusterSingletonState.WasOldest)
                                                    .Using(
                                                        new WasOldestData()
                                                        {
                                                            Singleton = oldestData.Singleton,
                                                            SingletonTerminated =
                                                                oldestData.SingletonTerminated,
                                                            NewOldest = oldestChanged.Oldest
                                                        });
                                        }
                                    }
                                    else
                                    {
                                        SetTimer(TakeOverRetryTimer, new TakeOverRetry { Count = 1 }, _retryInterval);
                                        nextState =
                                            GoTo(ClusterSingletonState.WasOldest)
                                                .Using(
                                                    new WasOldestData
                                                    {
                                                        Singleton = oldestData.Singleton,
                                                        SingletonTerminated =
                                                            oldestData.SingletonTerminated
                                                    });

                                    }
                                });
                        }).With<HandOverToMe>(
                            () =>
                            {
                                StateData.Match().With<OldestData>(
                                    oldestData =>
                                    {
                                        nextState = GoToHandingOver(
                                            oldestData.Singleton,
                                            oldestData.SingletonTerminated,
                                            Sender);
                                    });
                            }).With<Terminated>(
                                terminated =>
                                {
                                    StateData.Match().With<OldestData>(
                                        oldestData =>
                                        {
                                            if (terminated.ActorRef.Equals(oldestData.Singleton))
                                            {
                                                nextState =
                                                    Stay()
                                                        .Using(
                                                            new OldestData
                                                            {
                                                                Singleton = oldestData.Singleton,
                                                                SingletonTerminated = true
                                                            });
                                            }
                                        });
                                });
                    return nextState;
                });

            When(
                ClusterSingletonState.WasOldest,
                @event =>
                {
                    State<ClusterSingletonState, IClusterSingletonData> nextState = null;
                    @event.FsmEvent.Match().With<TakeOverRetry>(
                        takeOverRetry =>
                        {
                            StateData.Match().With<WasOldestData>(
                                wasOldestData =>
                                {
                                    if (takeOverRetry.Count <= _maxTakeOverRetries)
                                    {
                                        LogInfo(
                                            "Retry [{0}], sending TakeOverFromMe to [{1}]",
                                            takeOverRetry.Count,
                                            wasOldestData.NewOldest);
                                        if (wasOldestData.NewOldest != null)
                                        {
                                            Peer(wasOldestData.NewOldest).Tell(new TakeOverFromMe());
                                        }
                                        SetTimer(
                                            TakeOverRetryTimer,
                                            new TakeOverRetry { Count = takeOverRetry.Count + 1 },
                                            _retryInterval);
                                        nextState = Stay();
                                    }
                                    else
                                        throw new ClusterSingletonManagerIsStuck(
                                            String.Format(
                                                "Expected hand-over to [{0}] never occured",
                                                wasOldestData.NewOldest));
                                });

                        }).With<HandOverToMe>(
                            () =>
                            {
                                StateData.Match().With<WasOldestData>(
                                    wasOldestData =>
                                    {
                                        nextState = GoToHandingOver(
                                            wasOldestData.Singleton,
                                            wasOldestData.SingletonTerminated,
                                            Sender);
                                    });
                            }).With<ClusterEvent.MemberRemoved>(
                                memberRemoved =>
                                {
                                    StateData.Match().With<WasOldestData>(
                                        wasOldestData =>
                                        {
                                            if (!_selfExited
                                                && memberRemoved.Member.Address.Equals(wasOldestData.NewOldest))
                                            {
                                                AddRemoved(memberRemoved.Member.Address);
                                                nextState = GoToHandingOver(
                                                    wasOldestData.Singleton,
                                                    wasOldestData.SingletonTerminated,
                                                    null);
                                            }
                                        });
                                }).With<Terminated>(
                                    terminated =>
                                    {
                                        StateData.Match().With<WasOldestData>(
                                            wasOldestData =>
                                            {
                                                if (terminated.ActorRef.Equals(wasOldestData.Singleton))
                                                {
                                                    nextState =
                                                        Stay()
                                                            .Using(
                                                                new WasOldestData
                                                                {
                                                                    Singleton = wasOldestData.Singleton,
                                                                    SingletonTerminated = true,
                                                                    NewOldest = wasOldestData.NewOldest
                                                                });
                                                }
                                            });
                                    });
                    return nextState;
                });

            When(
                ClusterSingletonState.HandingOver,
                @event =>
                {
                    State<ClusterSingletonState, IClusterSingletonData> nextState = null;
                    @event.FsmEvent.Match().With<Terminated>(
                        terminated =>
                        {
                            StateData.Match().With<HandingOverData>(
                                handingOverData =>
                                {
                                    if (terminated.ActorRef.Equals(handingOverData.Singleton))
                                    {
                                        nextState = HandOverDone(handingOverData.HandOverTo);
                                    }
                                });
                        }).With<HandOverToMe>(
                            () =>
                            {
                                StateData.Match().With<HandingOverData>(
                                    handingOverData =>
                                    {
                                        if (Sender.Equals(handingOverData.HandOverTo))
                                        {
                                            Sender.Tell(new HandOverInProgress());
                                            nextState = Stay();
                                        }
                                    });
                            });
                    return nextState;
                });

            When(
                ClusterSingletonState.End,
                @event =>
                {
                    State<ClusterSingletonState, IClusterSingletonData> nextState = null;
                    @event.FsmEvent.Match().With<ClusterEvent.MemberRemoved>(
                        memberRemoved =>
                        {
                            if (memberRemoved.Member.Address.Equals(_cluster.SelfAddress))
                            {
                                LogInfo("Self removed, stopping ClusterSingletonManager");
                                nextState = Stop();
                            }
                        });

                    return nextState;
                });

            WhenUnhandled(
                @event =>
                {
                    State<ClusterSingletonState, IClusterSingletonData> nextState = null;
                    @event.FsmEvent.Match()
                        .With<ClusterEvent.CurrentClusterState>(() =>
                        {
                            nextState = Stay();
                        }).With<ClusterEvent.MemberExited>(
                            memberExited =>
                            {
                                if (memberExited.Member.Address.Equals(_cluster.SelfAddress))
                                {
                                    _selfExited = true;
                                    LogInfo("Exited [{0}]", memberExited.Member.Address);
                                }
                                nextState = Stay();
                            }).With<ClusterEvent.MemberRemoved>(
                                memberRemoved =>
                                {
                                    if (!_selfExited)
                                    {
                                        LogInfo("Member removed [{0}]", memberRemoved.Member.Address);
                                    }
                                    AddRemoved(memberRemoved.Member.Address);
                                    nextState = Stay();
                                }).With<TakeOverFromMe>(
                                    () =>
                                    {
                                        LogInfo(
                                            "Ignoring TakeOver request in [{0}] from [{1}].",
                                            StateName,
                                            Sender.Path.Address);
                                        nextState = Stay();
                                    }).With<Cleanup>(
                                        () =>
                                        {
                                            CleanupOverdueNotMemberAnyMore();
                                            nextState = Stay();
                                        });

                    return nextState;
                });

            OnTransition(
                (from, to) =>
                {
                    LogInfo("ClusterSingletonManager state change [{0} -> {1}] {2}", from, to, StateData.ToString());

                    if (to == ClusterSingletonState.BecomingOldest) SetTimer(HandOverRetryTimer, new HandOverRetry { Count = 1 }, _retryInterval);
                    if (from == ClusterSingletonState.BecomingOldest) CancelTimer(HandOverRetryTimer);
                    if (from == ClusterSingletonState.WasOldest) CancelTimer(TakeOverRetryTimer);
                    if (to == ClusterSingletonState.Younger || to == ClusterSingletonState.Oldest) GetNextOldestChange();
                    if (to == ClusterSingletonState.Younger || to == ClusterSingletonState.End)
                    {
                        if (_removed.ContainsKey(_cluster.SelfAddress))
                        {
                            LogInfo("Self removed, stopping ClusterSingletonManager");
                            Context.Stop(Self);
                        }
                    }
                });

            StartWith(ClusterSingletonState.Start, Uninitialized.Instance);
        }

        public void LogInfo(string message)
        {
            _log.Info(message);
        }

        public void LogInfo(string template, params object[] args)
        {
            _log.Info(String.Format(template, args));
        }
    }


    public static class ClusterSingletonManager
    {
        public static Props Props(
            Props singletonProps,
            string singletonName,
            object terminationMessage,
            string role,
            int maxHandOverRetries,
            int maxTakeOverRetries,
            TimeSpan retryInterval)
        {
            return
                Actor.Props.Create<ClusterSingletonManagerActor>(
                    singletonProps,
                    singletonName,
                    terminationMessage,
                    role,
                    maxHandOverRetries,
                    maxTakeOverRetries,
                    retryInterval).WithDeploy(Deploy.Local);
        }

        public static Props Props(
            Props singletonProps,
            string singletonName,
            object terminationMessage,
            string role)
        {
            return
                ClusterSingletonManager.Props(
                    singletonProps,
                    singletonName,
                    terminationMessage,
                    role,
                    maxHandOverRetries: 10,
                    maxTakeOverRetries: 5,
                    retryInterval: TimeSpan.FromSeconds(1.0));
        }

    }
}
