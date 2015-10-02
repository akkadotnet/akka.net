//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonProxy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Cluster.Tools.Singleton
{
    /**
     * The `ClusterSingletonProxy` works together with the [[akka.cluster.singleton.ClusterSingletonManager]] to provide a
     * distributed proxy to the singleton actor.
     *
     * The proxy can be started on every node where the singleton needs to be reached and used as if it were the singleton
     * itself. It will then act as a router to the currently running singleton instance. If the singleton is not currently
     * available, e.g., during hand off or startup, the proxy will buffer the messages sent to the singleton and then deliver
     * them when the singleton is finally available. The size of the buffer is configurable and it can be disabled by using
     * a buffer size of 0. When the buffer is full old messages will be dropped when new messages are sent via the proxy.
     *
     * The proxy works by keeping track of the oldest cluster member. When a new oldest member is identified, e.g. because
     * the older one left the cluster, or at startup, the proxy will try to identify the singleton on the oldest member by
     * periodically sending an [[akka.actor.Identify]] message until the singleton responds with its
     * [[akka.actor.ActorIdentity]].
     *
     * Note that this is a best effort implementation: messages can always be lost due to the distributed nature of the
     * actors involved.
     */
    public sealed class ClusterSingletonProxy : UntypedActor
    {
        internal sealed class TryToIdentifySingleton
        {
            public static readonly TryToIdentifySingleton Instance = new TryToIdentifySingleton();
            private TryToIdentifySingleton() { }
        }

        /**
         * Scala API: Factory method for `ClusterSingletonProxy` [[akka.actor.Props]].
         *
         * @param singletonManagerPath The logical path of the singleton manager, e.g. `/user/singletonManager`,
         *   which ends with the name you defined in `actorOf` when creating the [[ClusterSingletonManager]].
         * @param settings see [[ClusterSingletonProxySettings]]
         */
        public static Props Props(string singletonManagerPath, ClusterSingletonProxySettings settings)
        {
            return Actor.Props.Create(() => new ClusterSingletonProxy(singletonManagerPath, settings)).WithDeploy(Deploy.Local);
        }

        private readonly ClusterSingletonProxySettings _settings;
        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly Queue<KeyValuePair<object, IActorRef>> _buffer = new Queue<KeyValuePair<object, IActorRef>>(); // queue seems to fit better
        private readonly string[] _singletonPath;
        private int _identityCounter = 0;
        private string _identityId;
        private IActorRef _singleton = null;
        private ICancelable _identityTimer = null;
        private ImmutableSortedSet<Member> _membersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(MemberAgeOrdering.Descending);
        private ILoggingAdapter _log;

        public ClusterSingletonProxy(string singletonManagerPath, ClusterSingletonProxySettings settings)
        {
            _settings = settings;
            _singletonPath = (singletonManagerPath + "/" + settings.SingletonName).Split('/');
            _identityId = CreateIdentifyId(_identityCounter);
        }

        private ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        protected override void OnReceive(object message)
        {
            message.Match()
                .With<ClusterEvent.CurrentClusterState>(HandleInitial)
                .With<ClusterEvent.MemberUp>(m => Add(m.Member))
                .With<ClusterEvent.MemberExited>(m => Remove(m.Member))
                .With<ClusterEvent.MemberRemoved>(m => Remove(m.Member))
                .With<ActorIdentity>(identity =>
                {
                    if (identity.Subject != null)
                    {
                        // if the new singleton is defined, deliver all buffered messages
                        var subject = identity.Subject;
                        Log.Info("Singleton identified at [{0}]", subject.Path);
                        _singleton = subject;
                        Context.Watch(subject);
                        CancelTimer();
                        SendBuffered();
                    }
                })
                .With<TryToIdentifySingleton>(() =>
                {
                    var oldest = _membersByAge.FirstOrDefault();
                    if (oldest != null && _identityTimer != null)
                    {
                        var singletonAddress = new RootActorPath(oldest.Address) / _singletonPath;
                        Log.Debug("Trying to identify singleton at [{0}]", singletonAddress);
                        Context.ActorSelection(singletonAddress).Tell(new Identify(_identityId));
                    }
                })
                .With<Terminated>(terminated =>
                {
                    if (Equals(_singleton, terminated.ActorRef))
                    {
                        // buffering mode, identification of new will start when old node is removed
                        _singleton = null;
                    }
                })
                .Default(msg =>
                {
                    if (_singleton != null)
                    {
                        if (Log.IsDebugEnabled)
                            Log.Debug("Forwarding message of type [{0}] to current singleton instance at [{1}]", msg.GetType(), _singleton.Path);
                        _singleton.Forward(msg);
                    }
                    else
                        Buffer(msg);
                });
        }

        protected override void PreStart()
        {
            CancelTimer();
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent) });
        }

        protected override void PostStop()
        {
            CancelTimer();
            _cluster.Unsubscribe(Self);
        }

        private void CancelTimer()
        {
            if (_identityTimer != null)
            {
                _identityTimer.Cancel();
                _identityTimer = null;
            }
        }

        private bool MatchingRole(Member member)
        {
            if (string.IsNullOrEmpty(_settings.Role)) return true;

            return member.HasRole(_settings.Role);
        }

        /**
         * Discard old singleton ActorRef and send a periodic message to self to identify the singleton.
         */
        private void IdentifySingleton()
        {
            Log.Debug("Creating singleton identification timer...");
            _identityCounter++;
            _identityId = CreateIdentifyId(_identityCounter);
            _singleton = null;
            CancelTimer();
            _identityTimer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                initialDelay: TimeSpan.FromMilliseconds(1), //TODO: this should be TimeSpan.Zero
                interval: _settings.SingletonIdentificationInterval,
                receiver: Self,
                message: TryToIdentifySingleton.Instance,
                sender: Self);
        }

        private void TrackChanges(Action block)
        {
            var before = _membersByAge.FirstOrDefault();
            block();
            var after = _membersByAge.FirstOrDefault();

            // if the head has changed, I need to find the new singleton
            if (!Equals(before, after)) IdentifySingleton();
        }

        private void HandleInitial(ClusterEvent.CurrentClusterState state)
        {
            TrackChanges(() => _membersByAge = state.Members.Where(m => m.Status == MemberStatus.Up && MatchingRole(m)).ToImmutableSortedSet(MemberAgeOrdering.Descending));
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

        private string CreateIdentifyId(int i)
        {
            return "identify-singleton-" + string.Join("/", _singletonPath) + i;
        }

        private void Buffer(object message)
        {
            if (_settings.BufferSize == 0)
                Log.Debug("Singleton not available and buffering is disabled, dropping message [{0}]", message.GetType());
            else if (_buffer.Count == _settings.BufferSize)
            {
                var first = _buffer.Dequeue();
                Log.Debug("Singleton not available, buffer is full, dropping first message [{0}]", first.Key.GetType());
                _buffer.Enqueue(new KeyValuePair<object, IActorRef>(message, Sender));
            }
            else
            {
                Log.Debug("Singleton not available, buffering message type [{0}]", message.GetType());
                _buffer.Enqueue(new KeyValuePair<object, IActorRef>(message, Sender));
            }
        }

        private void SendBuffered()
        {
            Log.Debug("Sending buffered messages to current singleton instance");
            while (_buffer.Count != 0)
            {
                var pair = _buffer.Dequeue();
                _singleton.Tell(pair.Key, pair.Value);
            }
        }
    }

    /**
     * @param singletonName The actor name of the singleton actor that is started by the [[ClusterSingletonManager]].
     * @param role The role of the cluster nodes where the singleton can be deployed. If None, then any node will do.
     * @param singletonIdentificationInterval Interval at which the proxy will try to resolve the singleton instance.
     * @param bufferSize If the location of the singleton is unknown the proxy will buffer this number of messages
     *   and deliver them when the singleton is identified. When the buffer is full old messages will be dropped
     *   when new messages are sent viea the proxy. Use 0 to disable buffering, i.e. messages will be dropped
     *   immediately if the location of the singleton is unknown.
     */
    public sealed class ClusterSingletonProxySettings
    {
        public static ClusterSingletonProxySettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.singleton-proxy");
            if (config == null)
                throw new ConfigurationException(string.Format("Cannot create {0}: akka.cluster.singleton-proxy configuration node not found", typeof(ClusterSingletonProxySettings)));

            return Create(config);
        }

        public static ClusterSingletonProxySettings Create(Config config)
        {
            var role = config.GetString("role");
            if (role == string.Empty) role = null;

            return new ClusterSingletonProxySettings(
                singletonName: config.GetString("singleton-name"),
                role: role,
                singletonIdentificationInterval: config.GetTimeSpan("singleton-identification-interval"),
                bufferSize: config.GetInt("buffer-size"));
        }

        public readonly string SingletonName;
        public readonly string Role;
        public readonly TimeSpan SingletonIdentificationInterval;
        public readonly int BufferSize;

        public ClusterSingletonProxySettings(string singletonName, string role, TimeSpan singletonIdentificationInterval, int bufferSize)
        {
            if (string.IsNullOrEmpty(singletonName))
                throw new ArgumentNullException("singletonName");
            if (singletonIdentificationInterval == TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonProxySettings.SingletonIdentificationInterval must be positive", "singletonIdentificationInterval");
            if (bufferSize <= 0)
                throw new ArgumentException("ClusterSingletonProxySettings.BufferSize must be positive", "bufferSize");

            SingletonName = singletonName;
            Role = role;
            SingletonIdentificationInterval = singletonIdentificationInterval;
            BufferSize = bufferSize;
        }

        public ClusterSingletonProxySettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        public ClusterSingletonProxySettings WithRole(string role)
        {
            return new ClusterSingletonProxySettings(
                singletonName: SingletonName,
                role: role,
                singletonIdentificationInterval: SingletonIdentificationInterval,
                bufferSize: BufferSize);
        }

        public ClusterSingletonProxySettings WithSingletonIdentificationInterval(string singletonIdentificationInterval)
        {
            return Copy(singletonIdentificationInterval: SingletonIdentificationInterval);
        }

        public ClusterSingletonProxySettings WithBufferSize(int bufferSize)
        {
            return Copy(bufferSize: bufferSize);
        }

        public ClusterSingletonProxySettings Copy(string singletonName = null, string role = null,
            TimeSpan? singletonIdentificationInterval = null, int? bufferSize = null)
        {
            return new ClusterSingletonProxySettings(
                singletonName: singletonName ?? SingletonName,
                role: role ?? Role,
                singletonIdentificationInterval: singletonIdentificationInterval ?? SingletonIdentificationInterval,
                bufferSize: bufferSize ?? BufferSize);
        }
    }
}
