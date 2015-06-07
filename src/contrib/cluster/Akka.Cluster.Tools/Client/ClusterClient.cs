using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.PubSub;
using Akka.Event;
using Akka.Remote;

namespace Akka.Cluster.Tools.Client
{
    public class ClusterClientSettings
    {
        public static ClusterClientSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.client");
            var initialContacts = config.GetStringList("initial-contacts").Select(ActorPath.Parse).ToImmutableSortedSet();

            return new ClusterClientSettings(initialContacts,
                config.GetTimeSpan("establishing-get-contacts-interval"),
                config.GetTimeSpan("refresh-contacts-interval"),
                config.GetTimeSpan("heartbeat-interval"),
                config.GetTimeSpan("acceptable-heartbeat-pause"));
        }

        public readonly IImmutableSet<ActorPath> InitialContacts;
        public readonly TimeSpan EstablishingGetContactsInterval;
        public readonly TimeSpan RefreshContactsInterval;
        public readonly TimeSpan HeartbeatInterval;
        public readonly TimeSpan AcceptableHeartbeatPause;

        /**
         * @param initialContacts Actor paths of the `ClusterReceptionist` actors on
         *   the servers (cluster nodes) that the client will try to contact initially.
         * @param establishingGetContactsInterval Interval at which the client retries
         *   to establish contact with one of ClusterReceptionist on the servers (cluster nodes)
         * @param refreshContactsInterval Interval at which the client will ask the
         *   `ClusterReceptionist` for new contact points to be used for next reconnect.
         * @param heartbeatInterval How often failure detection heartbeat messages for detection
         *   of failed connections should be sent.
         * @param acceptableHeartbeatPause Number of potentially lost/delayed heartbeats that will
         *   be accepted before considering it to be an anomaly. The ClusterClient is using the
         *   [[akka.remote.DeadlineFailureDetector]], which will trigger if there are no heartbeats
         *   within the duration `heartbeatInterval + acceptableHeartbeatPause`.
         */
        public ClusterClientSettings(
            IImmutableSet<ActorPath> initialContacts,
            TimeSpan establishingGetContactsInterval,
            TimeSpan refreshContactsInterval,
            TimeSpan heartbeatInterval,
            TimeSpan acceptableHeartbeatPause)
        {
            InitialContacts = initialContacts;
            EstablishingGetContactsInterval = establishingGetContactsInterval;
            RefreshContactsInterval = refreshContactsInterval;
            HeartbeatInterval = heartbeatInterval;
            AcceptableHeartbeatPause = acceptableHeartbeatPause;
        }

        public ClusterClientSettings WithInitialContacts(IEnumerable<ActorPath> initialContacts)
        {
            return Copy(initialContacts: initialContacts);
        }

        public ClusterClientSettings WithEstablishingGetContactsInterval(TimeSpan value)
        {
            return Copy(establishingGetContactsInterval: value);
        }

        public ClusterClientSettings WithRefreshContactsInterval(TimeSpan value)
        {
            return Copy(refreshContactsInterval: value);
        }

        public ClusterClientSettings WithHeartbeatInterval(TimeSpan value)
        {
            return Copy(heartbeatInterval: value);
        }

        private ClusterClientSettings Copy(
            IEnumerable<ActorPath> initialContacts = null,
            TimeSpan? establishingGetContactsInterval = null,
            TimeSpan? refreshContactsInterval = null,
            TimeSpan? heartbeatInterval = null,
            TimeSpan? acceptableHeartbeatPause = null)
        {
            return new ClusterClientSettings(
                initialContacts == null ? InitialContacts : ImmutableHashSet.CreateRange(initialContacts),
                establishingGetContactsInterval ?? EstablishingGetContactsInterval,
                refreshContactsInterval ?? RefreshContactsInterval,
                heartbeatInterval ?? HeartbeatInterval,
                acceptableHeartbeatPause ?? AcceptableHeartbeatPause);
        }
    }

    /**
     * This actor is intended to be used on an external node that is not member
     * of the cluster. It acts like a gateway for sending messages to actors
     * somewhere in the cluster. From the initial contact points it will establish
     * a connection to a [[ClusterReceptionist]] somewhere in the cluster. It will
     * monitor the connection to the receptionist and establish a new connection if
     * the link goes down. When looking for a new receptionist it uses fresh contact
     * points retrieved from previous establishment, or periodically refreshed
     * contacts, i.e. not necessarily the initial contact points.
     *
     * You can send messages via the `ClusterClient` to any actor in the cluster
     * that is registered in the [[ClusterReceptionist]].
     * Messages are wrapped in [[ClusterClient.Send]], [[ClusterClient.SendToAll]]
     * or [[ClusterClient.Publish]].
     *
     * 1. [[ClusterClient.Send]] -
     * The message will be delivered to one recipient with a matching path, if any such
     * exists. If several entries match the path the message will be delivered
     * to one random destination. The sender of the message can specify that local
     * affinity is preferred, i.e. the message is sent to an actor in the same local actor
     * system as the used receptionist actor, if any such exists, otherwise random to any other
     * matching entry.
     *
     * 2. [[ClusterClient.SendToAll]] -
     * The message will be delivered to all recipients with a matching path.
     *
     * 3. [[ClusterClient.Publish]] -
     * The message will be delivered to all recipients Actors that have been registered as subscribers to
     * to the named topic.
     *
     *  Use the factory method [[ClusterClient#props]]) to create the
     * [[akka.actor.Props]] for the actor.
     */
    public class ClusterClient : ActorBase, IWithUnboundedStash
    {
        public readonly ClusterClientSettings Settings;
        private readonly DeadlineFailureDetector _failureDetector;
        private readonly ActorSelection[] _initialContactsSelections;
        private readonly ICancelable _heartbeatCancelable;

        private ILoggingAdapter _log;
        private ActorSelection[] _contacts;
        private ICancelable _refreshContactsCancelable = null;

        #region Messages

        [Serializable]
        public sealed class Send
        {
            public readonly string Path;
            public readonly object Message;
            public readonly bool LocalAffinity;

            public Send(string path, object message, bool localAffinity = false)
            {
                Path = path;
                Message = message;
                LocalAffinity = localAffinity;
            }
        }

        [Serializable]
        public sealed class SendToAll
        {
            public readonly string Path;
            public readonly object Message;

            public SendToAll(string path, object message)
            {
                Path = path;
                Message = message;
            }
        }

        [Serializable]
        public sealed class Publish
        {
            public readonly string Topic;
            public readonly object Message;

            public Publish(string topic, object message)
            {
                Topic = topic;
                Message = message;
            }
        }

        [Serializable]
        public sealed class RefreshContactsTick
        {
            public static readonly RefreshContactsTick Instance = new RefreshContactsTick();
            private RefreshContactsTick() { }
        }

        [Serializable]
        public sealed class HeartbeatTick
        {
            public static readonly HeartbeatTick Instance = new HeartbeatTick();
            private HeartbeatTick() { }
        }

        #endregion

        public ClusterClient(ClusterClientSettings settings)
        {
            if (!settings.InitialContacts.Any()) throw new ArgumentException("Initial contacts for cluster client cannot be empty");
            Settings = settings;
            _failureDetector = new DeadlineFailureDetector(Settings.AcceptableHeartbeatPause, () => Settings.HeartbeatInterval.Ticks);
            _initialContactsSelections = settings.InitialContacts.Select(Context.ActorSelection).ToArray();
            _contacts = _initialContactsSelections;

            SendGetContacts();

            _heartbeatCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                settings.HeartbeatInterval, settings.HeartbeatInterval, Self, HeartbeatTick.Instance, Self);

            ScheduleRefreshContactsTick(settings.EstablishingGetContactsInterval);
            Self.Tell(RefreshContactsTick.Instance);
        }

        public IStash Stash { get; set; }
        public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        private void ScheduleRefreshContactsTick(TimeSpan establishingGetContactsInterval)
        {
            if (_refreshContactsCancelable != null) _refreshContactsCancelable.Cancel();

            _refreshContactsCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(Settings.RefreshContactsInterval,
                    Settings.RefreshContactsInterval, Self, RefreshContactsTick.Instance, Self);
        }

        protected override void PostStop()
        {
            base.PostStop();
            _heartbeatCancelable.Cancel();
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
            return message.Match()
                .With<ClusterReceptionist.Contacts>(msg =>
                {
                    if (msg.ContactPoints.Length != 0)
                    {
                        _contacts = msg.ContactPoints;
                        foreach (var contact in _contacts)
                        {
                            contact.Tell(new Identify(null));
                        }
                    }
                })
                .With<ActorIdentity>(msg =>
                {
                    if (msg.Subject != null)
                    {
                        Log.Info("Connected to [{0}]", msg.Subject.Path);
                        ScheduleRefreshContactsTick(Settings.RefreshContactsInterval);
                        Stash.UnstashAll();
                        Context.Become(Active(msg.Subject));
                        _failureDetector.HeartBeat();
                    }
                })
                .With<HeartbeatTick>(_ => _failureDetector.HeartBeat())
                .With<RefreshContactsTick>(_ => SendGetContacts())
                .Default(msg => Stash.Stash())
                .WasHandled;
        }

        private Receive Active(IActorRef receptionist)
        {
            return message => 
                message.Match()
                .With<Send>(send => 
                    receptionist.Forward(new DistributedPubSubMediator.Send(send.Path, send.Message, send.LocalAffinity)))
                .With<SendToAll>(toAll =>
                    receptionist.Forward(new DistributedPubSubMediator.SendToAll(toAll.Path, toAll.Message)))
                .With<Publish>(publish =>
                    receptionist.Forward(new DistributedPubSubMediator.Publish(publish.Topic, publish.Message)))
                .With<RefreshContactsTick>(_ => 
                    receptionist.Tell(ClusterReceptionist.GetContacts.Instance))
                .With<ClusterReceptionist.Contacts>(contacts =>
                {
                    if (contacts.ContactPoints.Length != 0)
                        _contacts = contacts.ContactPoints;
                })
                .With<Terminated>(terminated =>
                {
                    Log.Info("Lost contact with [{0}], restablishing connection", receptionist);
                    SendGetContacts();
                    ScheduleRefreshContactsTick(Settings.EstablishingGetContactsInterval);
                    Context.Become(Establishing);
                })
                .With<ActorIdentity>(_ => { /* ignore */ })
                .WasHandled;
        }

        private void SendGetContacts()
        {
            IEnumerable<ActorSelection> toSend;
            if (_contacts.Length == 0)
                toSend = _initialContactsSelections;
            else if (_contacts.Length == 1)
                toSend = _initialContactsSelections.Union(_contacts);
            else
                toSend = _contacts;

            foreach (var contact in toSend)
            {
                contact.Tell(ClusterReceptionist.GetContacts.Instance);
            }
        }
    }
}