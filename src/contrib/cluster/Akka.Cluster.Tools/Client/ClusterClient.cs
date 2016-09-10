//-----------------------------------------------------------------------
// <copyright file="ClusterClient.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Remote;
using Akka.Util.Internal;

namespace Akka.Cluster.Client
{
    /// <summary>
    /// This actor is intended to be used on an external node that is not member
    /// of the cluster. It acts like a gateway for sending messages to actors
    /// somewhere in the cluster. From the initial contact points it will establish
    /// a connection to a <see cref="ClusterReceptionist"/> somewhere in the cluster. It will
    /// monitor the connection to the receptionist and establish a new connection if
    /// the link goes down. When looking for a new receptionist it uses fresh contact
    /// points retrieved from previous establishment, or periodically refreshed
    /// contacts, i.e. not necessarily the initial contact points.
    /// </summary>
    public sealed class ClusterClient : ActorBase
    {
        #region Messages

        /// <summary>
        /// The message will be delivered to one recipient with a matching path, if any such
        /// exists. If several entries match the path the message will be delivered
        /// to one random destination. The sender of the message can specify that local
        /// affinity is preferred, i.e. the message is sent to an actor in the same local actor
        /// system as the used receptionist actor, if any such exists, otherwise random to any other
        /// matching entry.
        /// </summary>
        [Serializable]
        public sealed class Send
        {
            public Send(string path, object message, bool localAffinity = false)
            {
                Path = path;
                Message = message;
                LocalAffinity = localAffinity;
            }

            public string Path { get; }

            public object Message { get; }

            public bool LocalAffinity { get; }

        }

        /// <summary>
        /// The message will be delivered to all recipients with a matching path.
        /// </summary>
        [Serializable]
        public sealed class SendToAll
        {
            public SendToAll(string path, object message)
            {
                Path = path;
                Message = message;
            }

            public string Path { get; }

            public object Message { get; }

        }

        /// <summary>
        /// The message will be delivered to all recipients Actors that have been registered as subscribers to
        /// to the named topic.
        /// </summary>
        [Serializable]
        public sealed class Publish
        {
            public Publish(string topic, object message)
            {
                Topic = topic;
                Message = message;
            }

            public string Topic { get; }

            public object Message { get; }

        }

        [Serializable]
        internal sealed class RefreshContactsTick
        {
            public static readonly RefreshContactsTick Instance = new RefreshContactsTick();
            private RefreshContactsTick() { }
        }

        [Serializable]
        internal sealed class HeartbeatTick
        {
            public static readonly HeartbeatTick Instance = new HeartbeatTick();
            private HeartbeatTick() { }
        }

        [Serializable]
        internal sealed class ReconnectTimeout
        {
            public static readonly ReconnectTimeout Instance = new ReconnectTimeout();
            private ReconnectTimeout() { }
        }

        #endregion

        /// <summary>
        /// Factory method for <see cref="ClusterClient"/> <see cref="Actor.Props"/>.
        /// </summary>
        public static Props Props(ClusterClientSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return Actor.Props.Create(() => new ClusterClient(settings)).WithDeploy(Deploy.Local);
        }

        private ILoggingAdapter _log = Context.GetLogger();
        private readonly ClusterClientSettings _settings;
        private readonly DeadlineFailureDetector _failureDetector;
        private ImmutableHashSet<ActorPath> _contactPaths;
        private readonly ActorSelection[] _initialContactsSelections;
        private ActorSelection[] _contacts;
        private ImmutableHashSet<ActorPath> _contactPathsPublished;
        private ImmutableList<IActorRef> _subscribers;
        private readonly ICancelable _heartbeatTask;
        private ICancelable _refreshContactsCancelable;
        private readonly Queue<Tuple<object, IActorRef>> _buffer;

        public ClusterClient(ClusterClientSettings settings)
        {
            if (settings.InitialContacts.Count == 0)
            {
                throw new ArgumentException("Initial contacts for cluster client cannot be empty");
            }

            _settings = settings;
            _failureDetector = new DeadlineFailureDetector(_settings.AcceptableHeartbeatPause, _settings.HeartbeatInterval);

            _contactPaths = settings.InitialContacts.ToImmutableHashSet();
            _initialContactsSelections = _contactPaths.Select(Context.ActorSelection).ToArray();
            _contacts = _initialContactsSelections;

            SendGetContacts();

            _contactPathsPublished = ImmutableHashSet<ActorPath>.Empty;
            _subscribers = ImmutableList<IActorRef>.Empty;

            _heartbeatTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                settings.HeartbeatInterval,
                settings.HeartbeatInterval,
                Self,
                HeartbeatTick.Instance,
                Self);

            _refreshContactsCancelable = null;
            ScheduleRefreshContactsTick(settings.EstablishingGetContactsInterval);
            Self.Tell(RefreshContactsTick.Instance);

            _buffer = new Queue<Tuple<object, IActorRef>>();
        }

        private void ScheduleRefreshContactsTick(TimeSpan interval)
        {
            if (_refreshContactsCancelable != null)
            {
                _refreshContactsCancelable.Cancel();
                _refreshContactsCancelable = null;
            }

            _refreshContactsCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                interval,
                interval,
                Self,
                RefreshContactsTick.Instance,
                Self);
        }

        protected override void PostStop()
        {
            base.PostStop();
            _heartbeatTask.Cancel();

            if (_refreshContactsCancelable != null)
            {
                _refreshContactsCancelable.Cancel();
                _refreshContactsCancelable = null;
            }
        }

        protected override bool Receive(object message)
        {
            return Establishing(message);
        }

        private bool Establishing(object message)
        {
            ICancelable connectTimerCancelable = null;
            if (_settings.ReconnectTimeout.HasValue)
            {
                connectTimerCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(
                    _settings.ReconnectTimeout.Value,
                    Self,
                    ReconnectTimeout.Instance,
                    Self);
            }

            if (message is ClusterReceptionist.Contacts)
            {
                var contacts = (ClusterReceptionist.Contacts)message;

                if (contacts.ContactPoints.Count > 0)
                {
                    _contactPaths = contacts.ContactPoints.Select(ActorPath.Parse).ToImmutableHashSet();
                    _contacts = _contactPaths.Select(Context.ActorSelection).ToArray();
                    _contacts.ForEach(c => c.Tell(new Identify(null)));
                }

                PublishContactPoints();
            }
            else if (message is ActorIdentity)
            {
                var actorIdentify = (ActorIdentity)message;
                var receptionist = actorIdentify.Subject;

                if (receptionist != null)
                {
                    _log.Info("Connected to [{0}]", receptionist.Path);
                    ScheduleRefreshContactsTick(_settings.RefreshContactsInterval);
                    SendBuffered(receptionist);
                    Context.Become(Active(receptionist));
                    connectTimerCancelable?.Cancel();
                    _failureDetector.HeartBeat();
                }
                else
                {
                    // ok, use another instead
                }
            }
            else if (message is HeartbeatTick)
            {
                _failureDetector.HeartBeat();
            }
            else if (message is RefreshContactsTick)
            {
                SendGetContacts();
            }
            else if (message is Send)
            {
                var send = (Send)message;
                Buffer(new Akka.Cluster.Tools.PublishSubscribe.Send(send.Path, send.Message, send.LocalAffinity));
            }
            else if (message is SendToAll)
            {
                var sendToAll = (SendToAll)message;
                Buffer(new Akka.Cluster.Tools.PublishSubscribe.SendToAll(sendToAll.Path, sendToAll.Message));
            }
            else if (message is Publish)
            {
                var publish = (Publish)message;
                Buffer(new Akka.Cluster.Tools.PublishSubscribe.Publish(publish.Topic, publish.Message));
            }
            else if (message is ReconnectTimeout)
            {
                _log.Warning("Receptionist reconnect not successful within {0} stopping cluster client", _settings.ReconnectTimeout);
                Context.Stop(Self);
            }
            else
            {
                return ContactPointMessages(message);
            }

            return true;
        }

        private Receive Active(IActorRef receptionist)
        {
            return message =>
            {
                if (message is Send)
                {
                    var send = (Send)message;
                    receptionist.Forward(new Akka.Cluster.Tools.PublishSubscribe.Send(send.Path, send.Message, send.LocalAffinity));
                }
                else if (message is SendToAll)
                {
                    var sendToAll = (SendToAll)message;
                    receptionist.Forward(new Akka.Cluster.Tools.PublishSubscribe.SendToAll(sendToAll.Path, sendToAll.Message));
                }
                else if (message is Publish)
                {
                    var publish = (Publish)message;
                    receptionist.Forward(new Akka.Cluster.Tools.PublishSubscribe.Publish(publish.Topic, publish.Message));
                }
                else if (message is HeartbeatTick)
                {
                    if (!_failureDetector.IsAvailable)
                    {
                        _log.Info("Lost contact with [{0}], restablishing connection", receptionist);
                        SendGetContacts();
                        ScheduleRefreshContactsTick(_settings.EstablishingGetContactsInterval);
                        Context.Become(Establishing);
                        _failureDetector.HeartBeat();
                    }
                    else
                    {
                        receptionist.Tell(ClusterReceptionist.Heartbeat.Instance);
                    }
                }
                else if (message is ClusterReceptionist.HeartbeatRsp)
                {
                    _failureDetector.HeartBeat();
                }
                else if (message is RefreshContactsTick)
                {
                    receptionist.Tell(ClusterReceptionist.GetContacts.Instance);
                }
                else if (message is ClusterReceptionist.Contacts)
                {
                    var contacts = (ClusterReceptionist.Contacts)message;

                    // refresh of contacts
                    if (contacts.ContactPoints.Count > 0)
                    {
                        _contactPaths = contacts.ContactPoints.Select(ActorPath.Parse).ToImmutableHashSet();
                        _contacts = _contactPaths.Select(Context.ActorSelection).ToArray();
                    }
                    PublishContactPoints();
                }
                else if (message is ActorIdentity)
                {
                    // ok, from previous establish, already handled
                }
                else
                {
                    return ContactPointMessages(message);
                }

                return true;
            };
        }

        private bool ContactPointMessages(object message)
        {
            if (message is SubscribeContactPoints)
            {
                var subscriber = Sender;
                subscriber.Tell(new ContactPoints(_contactPaths));
                _subscribers = _subscribers.Add(subscriber);
                Context.Watch(subscriber);
            }
            else if (message is UnsubscribeContactPoints)
            {
                var subscriber = Sender;
                _subscribers = _subscribers.Where(c => !c.Equals(subscriber)).ToImmutableList();
            }
            else if (message is Terminated)
            {
                var terminated = (Terminated)message;
                Self.Tell(UnsubscribeContactPoints.Instance, terminated.ActorRef);
            }
            else if (message is GetContactPoints)
            {
                Sender.Tell(new ContactPoints(_contactPaths));
            }
            else return false;

            return true;
        }

        private void SendGetContacts()
        {
            ActorSelection[] sendTo;
            if (_contacts.Length == 0)
                sendTo = _initialContactsSelections;
            else if (_contacts.Length == 1)
                sendTo = _initialContactsSelections.Union(_contacts).ToArray();
            else
                sendTo = _contacts;

            if (_log.IsDebugEnabled)
                _log.Debug("Sending GetContacts to [{0}]", string.Join(", ", sendTo.AsEnumerable()));

            sendTo.ForEach(c => c.Tell(ClusterReceptionist.GetContacts.Instance));
        }

        private void Buffer(object message)
        {
            if (_settings.BufferSize == 0)
            {
                _log.Debug("Receptionist not available and buffering is disabled, dropping message [{0}]", message.GetType().Name);
            }
            else if (_buffer.Count == _settings.BufferSize)
            {
                var m = _buffer.Dequeue();
                _log.Debug("Receptionist not available, buffer is full, dropping first message [{0}]", m.Item1.GetType().Name);
                _buffer.Enqueue(Tuple.Create(message, Sender));
            }
            else
            {
                _log.Debug("Receptionist not available, buffering message type [{0}]", message.GetType().Name);
                _buffer.Enqueue(Tuple.Create(message, Sender));
            }
        }

        private void SendBuffered(IActorRef receptionist)
        {
            _log.Debug("Sending buffered messages to receptionist");
            while (_buffer.Count != 0)
            {
                var t = _buffer.Dequeue();
                receptionist.Tell(t.Item1, t.Item2);
            }
        }

        private void PublishContactPoints()
        {
            foreach (var cp in _contactPaths)
            {
                if (!_contactPathsPublished.Contains(cp))
                {
                    var contactPointAdded = new ContactPointAdded(cp);
                    _subscribers.ForEach(s => s.Tell(contactPointAdded));
                }
            }

            foreach (var cp in _contactPathsPublished)
            {
                if (!_contactPaths.Contains(cp))
                {
                    var contactPointRemoved = new ContactPointRemoved(cp);
                    _subscribers.ForEach(s => s.Tell(contactPointRemoved));
                }
            }

            _contactPathsPublished = _contactPaths;
        }
    }

    /// <summary>
    /// Declares a super type for all events emitted by the `ClusterClient`
    /// in relation to contact points being added or removed.
    /// </summary>
    public interface IContactPointChange
    {
        ActorPath ContactPoint { get; }
    }

    /// <summary>
    /// Emitted to a subscriber when contact points have been
    /// received by the <see cref="ClusterClient"/> and a new one has been added.
    /// </summary>
    public sealed class ContactPointAdded : IContactPointChange
    {
        public ContactPointAdded(ActorPath contactPoint)
        {
            ContactPoint = contactPoint;
        }

        public ActorPath ContactPoint { get; }
    }

    /// <summary>
    /// Emitted to a subscriber when contact points have been
    /// received by the <see cref="ClusterClient"/> and a new one has been added.
    /// </summary>
    public sealed class ContactPointRemoved : IContactPointChange
    {
        public ContactPointRemoved(ActorPath contactPoint)
        {
            ContactPoint = contactPoint;
        }

        public ActorPath ContactPoint { get; }
    }

    public interface ISubscribeContactPoints
    {
    }

    /// <summary>
    /// Subscribe to a cluster client's contact point changes where
    /// it is guaranteed that a sender receives the initial state
    /// of contact points prior to any events in relation to them
    /// changing.
    /// The sender will automatically become unsubscribed when it
    /// terminates.
    /// </summary>
    public sealed class SubscribeContactPoints : ISubscribeContactPoints
    {
        public static readonly SubscribeContactPoints Instance = new SubscribeContactPoints();
        private SubscribeContactPoints() { }
    }

    public interface IUnsubscribeContactPoints
    {
    }

    /// <summary>
    /// Explicitly unsubscribe from contact point change events.
    /// </summary>
    public sealed class UnsubscribeContactPoints : IUnsubscribeContactPoints
    {
        public static readonly UnsubscribeContactPoints Instance = new UnsubscribeContactPoints();
        private UnsubscribeContactPoints() { }
    }

    public interface IGetContactPoints
    {
    }

    /// <summary>
    /// Get the contact points known to this client. A <see cref="ContactPoints"/> message
    /// will be replied.
    /// </summary>
    public sealed class GetContactPoints : IGetContactPoints
    {
        public static readonly GetContactPoints Instance = new GetContactPoints();
        private GetContactPoints() { }
    }

    /// <summary>
    /// The reply to <see cref="GetContactPoints"/>.
    /// </summary>
    public sealed class ContactPoints
    {
        public ContactPoints(ImmutableHashSet<ActorPath> contactPoints)
        {
            ContactPointsList = contactPoints;
        }

        public ImmutableHashSet<ActorPath> ContactPointsList { get; }
    }
}
